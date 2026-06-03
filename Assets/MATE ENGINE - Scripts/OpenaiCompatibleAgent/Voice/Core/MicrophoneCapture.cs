using System;
using UnityEngine;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>
    /// Captures mic audio at a fixed sample rate and delivers fixed-size frames.
    /// Poll() must be called regularly (e.g. from MonoBehaviour.Update) on the main thread.
    /// </summary>
    public sealed class MicrophoneCapture
    {
        public const int SampleRate = 16000;
        public const int FrameSamples = 512; // 32 ms @16k (Silero v5 frame size)
        const int ClipLengthSec = 1;

        readonly string _device;
        AudioClip _clip;
        int _readPos;
        readonly float[] _carry = new float[FrameSamples];
        int _carryCount;
        float[] _clipBuffer;

        public bool IsRecording { get; private set; }

        /// <param name="device">null/empty = default device.</param>
        public MicrophoneCapture(string device) => _device = string.IsNullOrEmpty(device) ? null : device;

        public bool Start()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[Voice][Mic] No microphone device available.");
                return false;
            }

            _clip = Microphone.Start(_device, true, ClipLengthSec, SampleRate);
            if (_clip == null)
            {
                Debug.LogWarning("[Voice][Mic] Microphone.Start returned null.");
                return false;
            }
            _clipBuffer = new float[_clip.samples * _clip.channels];
            _readPos = 0;
            _carryCount = 0;
            IsRecording = true;
            Debug.Log($"[Voice][Mic] started: device='{(_device ?? "(default)")}' requestedRate={SampleRate} clip.frequency={_clip.frequency} channels={_clip.channels}");
            return true;
        }

        public void Stop()
        {
            if (!IsRecording) return;
            Microphone.End(_device);
            IsRecording = false;
            _clip = null;
        }

        /// <summary>
        /// Advance past buffered audio without emitting any frames. Call while input is gated so
        /// stale audio (e.g. the companion's own TTS) does not flood the consumer when polling resumes.
        /// </summary>
        public void Drain()
        {
            if (!IsRecording || _clip == null) return;
            int writePos = Microphone.GetPosition(_device);
            if (writePos < 0) return;
            _readPos = writePos;
            _carryCount = 0;
        }

        /// <summary>Drain newly recorded samples, invoking onFrame for each complete 512-sample frame.</summary>
        public void Poll(Action<float[]> onFrame)
        {
            if (!IsRecording || _clip == null) return;
            int writePos = Microphone.GetPosition(_device);
            if (writePos < 0) return;

            int total = _clip.samples;
            int available = writePos - _readPos;
            if (available < 0) available += total; // wrapped
            if (available <= 0) return;

            _clip.GetData(_clipBuffer, 0);

            for (int i = 0; i < available; i++)
            {
                int idx = (_readPos + i) % total;
                _carry[_carryCount++] = _clipBuffer[idx];
                if (_carryCount == FrameSamples)
                {
                    var frame = new float[FrameSamples];
                    Array.Copy(_carry, frame, FrameSamples);
                    onFrame(frame);
                    _carryCount = 0;
                }
            }
            _readPos = writePos;
        }
    }
}
