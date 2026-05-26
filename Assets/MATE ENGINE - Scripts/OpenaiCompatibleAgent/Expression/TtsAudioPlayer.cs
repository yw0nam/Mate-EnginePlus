using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// Decodes WAV bytes and plays them in sequence order.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TtsAudioPlayer : MonoBehaviour
    {
        // Phase A6: raw-bytes queue entry (see .sisyphus/plans/hermes-migration.md §5).
        private struct WavQueueEntry
        {
            public byte[] Wav;
            public string Emotion;
        }

        private AudioSource _audioSource;
        private readonly SortedList<int, WavQueueEntry> _queueBytes = new();
        private int _nextSequence;
        private bool _playing;

        public event System.Action<int, string> OnWavChunkStarted;

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (_playing && !_audioSource.isPlaying)
            {
                _playing = false;
                PlayNext();
            }
        }

        /// <summary>
        /// Enqueue raw WAV bytes for playback (Phase A6 — see .sisyphus/plans/hermes-migration.md §5).
        /// Non-base64 path fed by <c>IrodoriClient</c>: bytes are queued by sequence, decoded
        /// lazily inside <see cref="PlayNext"/>, and played through the same <see cref="AudioSource"/>
        /// path. Fires <see cref="OnWavChunkStarted"/> for emotion subscribers.
        /// </summary>
        public void EnqueueWavBytes(int sequence, byte[] wav, string emotion)
        {
            if (wav == null || wav.Length == 0) return;

            _queueBytes[sequence] = new WavQueueEntry
            {
                Wav = wav,
                Emotion = emotion,
            };

            if (!_playing)
                PlayNext();
        }

        /// <summary>
        /// Reset the player for a new turn.
        /// </summary>
        public void Reset()
        {
            _audioSource.Stop();
            _queueBytes.Clear();
            _nextSequence = 0;
            _playing = false;
        }

        private void PlayNext()
        {
            if (_queueBytes.ContainsKey(_nextSequence))
            {
                var entry = _queueBytes[_nextSequence];
                _queueBytes.Remove(_nextSequence);
                int seq = _nextSequence;
                _nextSequence++;

                var clip = DecodeWav(entry.Wav, $"tts_{seq}");
                if (clip == null) { PlayNext(); return; }

                _audioSource.clip = clip;
                _audioSource.Play();
                _playing = true;
                OnWavChunkStarted?.Invoke(seq, entry.Emotion);
                return;
            }
        }

        /// <summary>
        /// Whether the player is currently playing or has queued chunks.
        /// </summary>
        public bool IsPlaying => _playing || _queueBytes.Count > 0;

        public void Pause()
        {
            if (_playing && _audioSource != null) _audioSource.Pause();
        }

        public void Resume()
        {
            if (_playing && _audioSource != null) _audioSource.UnPause();
        }

        // =================================================================
        // WAV Decoder
        // =================================================================

        private static AudioClip DecodeWav(byte[] data, string clipName)
        {
            // WAV header parsing
            if (data.Length < 44) return null;

            int channels = BitConverter.ToInt16(data, 22);
            int sampleRate = BitConverter.ToInt32(data, 24);
            int bitsPerSample = BitConverter.ToInt16(data, 34);

            // Find "data" chunk
            int dataOffset = 12;
            int dataSize = 0;
            while (dataOffset < data.Length - 8)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(data, dataOffset, 4);
                int chunkSize = BitConverter.ToInt32(data, dataOffset + 4);
                if (chunkId == "data")
                {
                    dataOffset += 8;
                    dataSize = chunkSize;
                    break;
                }
                dataOffset += 8 + chunkSize;
            }

            if (dataSize == 0) return null;

            int bytesPerSample = bitsPerSample / 8;
            int sampleCount = dataSize / bytesPerSample;
            int sampleCountPerChannel = sampleCount / channels;

            float[] samples = new float[sampleCount];

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < sampleCount && (dataOffset + i * 2 + 1) < data.Length; i++)
                {
                    short s = BitConverter.ToInt16(data, dataOffset + i * 2);
                    samples[i] = s / 32768f;
                }
            }
            else if (bitsPerSample == 32)
            {
                for (int i = 0; i < sampleCount && (dataOffset + i * 4 + 3) < data.Length; i++)
                {
                    samples[i] = BitConverter.ToSingle(data, dataOffset + i * 4);
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < sampleCount && (dataOffset + i) < data.Length; i++)
                {
                    samples[i] = (data[dataOffset + i] - 128) / 128f;
                }
            }
            else
            {
                Debug.LogWarning($"[DMP-TTS] Unsupported bits per sample: {bitsPerSample}");
                return null;
            }

            var clip = AudioClip.Create(clipName, sampleCountPerChannel, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
