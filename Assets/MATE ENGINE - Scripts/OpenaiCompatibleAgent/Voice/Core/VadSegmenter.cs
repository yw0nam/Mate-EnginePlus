using System;
using System.Collections.Generic;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>
    /// Turns a stream of per-frame VAD probabilities into discrete utterances.
    /// Faithful port of Silero VAD's streaming state machine
    /// (snakers4/silero-vad, src/silero_vad/utils_vad.py — VADIterator / get_speech_timestamps):
    ///  - hysteresis: enter speech at <c>threshold</c>, only treat as silence below
    ///    <c>neg_threshold = threshold - 0.15</c> (so mid-speech probability dips do not chop);
    ///  - <c>temp_end</c> marks where a silence run began; the utterance ends once
    ///    <c>current_sample - temp_end >= min_silence_samples</c>; a frame back above
    ///    <c>threshold</c> cancels the pending end;
    ///  - speech padding on both ends; a min-speech-duration discard measured on the
    ///    UNPADDED segment (onset → silence start).
    /// Model-agnostic: caller supplies the probability for each fixed-size PCM frame.
    /// </summary>
    public class VadSegmenter
    {
        [Serializable]
        public class Config
        {
            public int sampleRate = 16000;
            public int frameSamples = 512;
            public float threshold = 0.5f;
            public int minSpeechMs = 250;
            public int minSilenceMs = 700;
            public int speechPadMs = 150;
            public int maxSpeechMs = 20000;
        }

        public event Action OnSpeechStart;
        public event Action<float[]> OnSpeechEnd;

        readonly Config _cfg;
        readonly float _negThreshold;
        readonly int _minSilenceSamples;
        readonly int _speechPadSamples;
        readonly int _minSpeechSamples;
        readonly int _maxSpeechSamples;
        readonly int _preRollFrames;

        readonly Queue<float[]> _preRoll = new Queue<float[]>();
        readonly List<float> _speech = new List<float>();

        bool _triggered;
        int _currentSample;       // total samples seen (monotonic clock, like VADIterator.current_sample)
        int _tempEnd;             // absolute sample where the current silence run began (0 = none)
        int _speechStartSample;   // absolute sample at speech onset (unpadded)

        public VadSegmenter(Config cfg)
        {
            _cfg = cfg ?? new Config();
            _negThreshold = _cfg.threshold - 0.15f;
            _minSilenceSamples = _cfg.sampleRate * _cfg.minSilenceMs / 1000;
            _speechPadSamples = _cfg.sampleRate * _cfg.speechPadMs / 1000;
            _minSpeechSamples = _cfg.sampleRate * _cfg.minSpeechMs / 1000;
            _maxSpeechSamples = _cfg.sampleRate * _cfg.maxSpeechMs / 1000;
            _preRollFrames = Math.Max(1, (int)Math.Ceiling(_speechPadSamples / (float)_cfg.frameSamples));
        }

        public bool IsInSpeech => _triggered;

        public void Reset()
        {
            _preRoll.Clear();
            _speech.Clear();
            _triggered = false;
            _currentSample = 0;
            _tempEnd = 0;
            _speechStartSample = 0;
        }

        public void Process(float[] frame, float prob)
        {
            _currentSample += frame.Length;

            // Speech resumed before the gap reached min_silence → cancel the pending end.
            if (prob >= _cfg.threshold && _tempEnd != 0)
                _tempEnd = 0;

            if (!_triggered)
            {
                // Keep a short pre-roll so the utterance retains some audio before onset.
                _preRoll.Enqueue(frame);
                while (_preRoll.Count > _preRollFrames) _preRoll.Dequeue();

                if (prob >= _cfg.threshold)
                {
                    _triggered = true;
                    _speechStartSample = _currentSample - frame.Length;
                    _speech.Clear();
                    foreach (var f in _preRoll) _speech.AddRange(f); // current frame already enqueued
                    _preRoll.Clear();
                    OnSpeechStart?.Invoke();
                }
                return;
            }

            // In a speech segment: keep collecting audio.
            _speech.AddRange(frame);

            // Only probabilities BELOW neg_threshold count as silence (hysteresis). Probabilities
            // in [neg_threshold, threshold) are a hangover band: they neither extend nor cancel
            // the silence run — temp_end stays put and the monotonic clock keeps elapsing.
            if (prob < _negThreshold)
            {
                if (_tempEnd == 0) _tempEnd = _currentSample;
                if (_currentSample - _tempEnd >= _minSilenceSamples)
                {
                    EmitEnd();
                    return;
                }
            }

            if (_speech.Count >= _maxSpeechSamples)
                EmitEnd();
        }

        void EmitEnd()
        {
            // Discard if the UNPADDED speech (onset → silence start) is shorter than min_speech.
            int speechEnd = _tempEnd != 0 ? _tempEnd : _currentSample;
            bool longEnough = (speechEnd - _speechStartSample) >= _minSpeechSamples;

            // Keep through temp_end + pad; trim trailing silence beyond the pad.
            int keep = _speech.Count;
            if (_tempEnd != 0)
            {
                int trailing = _currentSample - (_tempEnd + _speechPadSamples);
                if (trailing > 0) keep = Math.Max(0, _speech.Count - trailing);
            }
            var pcm = _speech.GetRange(0, keep).ToArray();

            _triggered = false;
            _tempEnd = 0;
            _speech.Clear();
            _preRoll.Clear();

            if (longEnough) OnSpeechEnd?.Invoke(pcm);
        }
    }
}
