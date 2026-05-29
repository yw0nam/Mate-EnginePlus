using System;
using System.Collections.Generic;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>
    /// Turns a stream of per-frame VAD probabilities into discrete utterances.
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
        readonly float _frameMs;
        readonly int _padFrames;
        readonly Queue<float[]> _preRoll = new Queue<float[]>();
        readonly List<float> _speech = new List<float>();

        bool _inSpeech;
        int _speechFrames;
        int _silenceFrames;

        public VadSegmenter(Config cfg)
        {
            _cfg = cfg ?? new Config();
            _frameMs = 1000f * _cfg.frameSamples / _cfg.sampleRate;
            _padFrames = Math.Max(1, (int)Math.Round(_cfg.speechPadMs / _frameMs));
        }

        public bool IsInSpeech => _inSpeech;

        public void Reset()
        {
            _preRoll.Clear();
            _speech.Clear();
            _inSpeech = false;
            _speechFrames = 0;
            _silenceFrames = 0;
        }

        public void Process(float[] frame, float prob)
        {
            bool isSpeech = prob >= _cfg.threshold;

            if (!_inSpeech)
            {
                _preRoll.Enqueue(frame);
                while (_preRoll.Count > _padFrames) _preRoll.Dequeue();

                if (isSpeech)
                {
                    _speechFrames++;
                    if (_speechFrames * _frameMs >= _cfg.minSpeechMs)
                    {
                        _inSpeech = true;
                        _silenceFrames = 0;
                        _speech.Clear();
                        foreach (var f in _preRoll) _speech.AddRange(f);
                        _preRoll.Clear();
                        OnSpeechStart?.Invoke();
                    }
                }
                else
                {
                    _speechFrames = 0;
                }
                return;
            }

            _speech.AddRange(frame);

            if (isSpeech) _silenceFrames = 0;
            else _silenceFrames++;

            bool silenceEnded = _silenceFrames * _frameMs >= _cfg.minSilenceMs;
            bool maxReached = _speech.Count >= _cfg.maxSpeechMs * _cfg.sampleRate / 1000;

            if (silenceEnded || maxReached)
            {
                int trailing = Math.Max(0, _silenceFrames - _padFrames) * _cfg.frameSamples;
                int keep = Math.Max(0, _speech.Count - trailing);
                var pcm = _speech.GetRange(0, keep).ToArray();

                Reset();
                if (pcm.Length >= _cfg.minSpeechMs * _cfg.sampleRate / 1000)
                    OnSpeechEnd?.Invoke(pcm);
            }
        }
    }
}
