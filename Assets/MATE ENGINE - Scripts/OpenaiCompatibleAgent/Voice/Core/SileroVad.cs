using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UnityEngine;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>Silero VAD v5 ONNX wrapper. One frame in -> speech probability out, carries state.</summary>
    public sealed class SileroVad : IDisposable
    {
        const int StateOuter = 2;
        const int StateInner = 128;

        readonly InferenceSession _session;
        readonly int _sampleRate;
        readonly int _contextSize;     // Silero v5: 64 samples @16k, 32 @8k, prepended to each chunk
        float[] _state = new float[StateOuter * 1 * StateInner];
        float[] _context;

        public SileroVad(byte[] modelBytes, int sampleRate = 16000)
        {
            _session = new InferenceSession(modelBytes);
            _sampleRate = sampleRate;
            _contextSize = sampleRate == 16000 ? 64 : 32;
            _context = new float[_contextSize];
        }

        public static SileroVad FromStreamingAssets(string fileName = "silero_vad.onnx", int sampleRate = 16000)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            return new SileroVad(File.ReadAllBytes(path), sampleRate);
        }

        public void ResetState() { Array.Clear(_state, 0, _state.Length); Array.Clear(_context, 0, _context.Length); }

        public float Process(float[] frame)
        {
            // Silero v5 expects [context | chunk] = 64 + 512 = 576 samples (16k). The context is the
            // last 64 samples of the previous chunk (zeros on the first call). See official utils_vad.py.
            int inLen = _contextSize + frame.Length;
            var buf = new float[inLen];
            Array.Copy(_context, 0, buf, 0, _contextSize);
            Array.Copy(frame, 0, buf, _contextSize, frame.Length);

            var input = new DenseTensor<float>(buf, new[] { 1, inLen });
            var state = new DenseTensor<float>(_state, new[] { StateOuter, 1, StateInner });
            var sr = new DenseTensor<long>(new long[] { _sampleRate }, new int[0]); // scalar

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", input),
                NamedOnnxValue.CreateFromTensor("state", state),
                NamedOnnxValue.CreateFromTensor("sr", sr),
            };

            using (var results = _session.Run(inputs))
            {
                float prob = 0f;
                foreach (var r in results)
                {
                    if (r.Name == "output")
                        prob = r.AsTensor<float>().GetValue(0);
                    else if (r.Name == "stateN")
                        _state = ((DenseTensor<float>)r.AsTensor<float>()).Buffer.ToArray();
                }

                // Carry the last context_size samples of this chunk into the next call.
                Array.Copy(frame, frame.Length - _contextSize, _context, 0, _contextSize);
                return prob;
            }
        }

        public void Dispose() => _session?.Dispose();
    }
}
