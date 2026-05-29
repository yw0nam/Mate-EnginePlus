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
        float[] _state = new float[StateOuter * 1 * StateInner];

        public SileroVad(byte[] modelBytes, int sampleRate = 16000)
        {
            _session = new InferenceSession(modelBytes);
            _sampleRate = sampleRate;
        }

        public static SileroVad FromStreamingAssets(string fileName = "silero_vad.onnx", int sampleRate = 16000)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            return new SileroVad(File.ReadAllBytes(path), sampleRate);
        }

        public void ResetState() => Array.Clear(_state, 0, _state.Length);

        public float Process(float[] frame)
        {
            var input = new DenseTensor<float>(frame, new[] { 1, frame.Length });
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
                    if (r.Name == "output") prob = r.AsTensor<float>()[0];
                    else if (r.Name == "stateN") _state = ((DenseTensor<float>)r.AsTensor<float>()).Buffer.ToArray();
                }
                return prob;
            }
        }

        public void Dispose() => _session?.Dispose();
    }
}
