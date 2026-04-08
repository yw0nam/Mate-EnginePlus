using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// Decodes WAV base64 from tts_chunk messages and plays them in sequence order.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TtsAudioPlayer : MonoBehaviour
    {
        private AudioSource _audioSource;
        private readonly SortedList<int, TtsChunkData> _queue = new();
        private int _nextSequence;
        private bool _playing;

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
        /// Enqueue a TTS chunk for playback. Chunks are sorted by sequence number.
        /// </summary>
        public void EnqueueChunk(TtsChunkData chunk)
        {
            if (string.IsNullOrEmpty(chunk.audio_base64)) return;

            _queue[chunk.sequence] = chunk;

            if (!_playing)
                PlayNext();
        }

        /// <summary>
        /// Reset the player for a new turn.
        /// </summary>
        public void Reset()
        {
            _audioSource.Stop();
            _queue.Clear();
            _nextSequence = 0;
            _playing = false;
        }

        private void PlayNext()
        {
            if (!_queue.ContainsKey(_nextSequence)) return;

            var chunk = _queue[_nextSequence];
            _queue.Remove(_nextSequence);
            _nextSequence++;

            var clip = DecodeWavBase64(chunk.audio_base64, $"tts_{chunk.sequence}");
            if (clip == null) { PlayNext(); return; }

            _audioSource.clip = clip;
            _audioSource.Play();
            _playing = true;
        }

        /// <summary>
        /// Whether the player is currently playing or has queued chunks.
        /// </summary>
        public bool IsPlaying => _playing || _queue.Count > 0;

        // =================================================================
        // WAV Decoder
        // =================================================================

        private static AudioClip DecodeWavBase64(string base64, string clipName)
        {
            try
            {
                byte[] wav = Convert.FromBase64String(base64);
                return DecodeWav(wav, clipName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP-TTS] Base64 decode error: {e.Message}");
                return null;
            }
        }

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
