using System.IO;
using NUnit.Framework;
using UnityEngine;
using OpenaiCompatibleAgent.Voice;

namespace OpenaiCompatibleAgent.Tests
{
    public class SileroVadIntegrationTests
    {
        [Test]
        public void Silence_YieldsLowProbability()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "silero_vad.onnx");
            if (!File.Exists(path)) Assert.Ignore("silero_vad.onnx not present");

            using (var vad = new SileroVad(File.ReadAllBytes(path)))
            {
                float maxProb = 0f;
                for (int i = 0; i < 20; i++)
                {
                    float p = vad.Process(new float[512]);
                    if (p > maxProb) maxProb = p;
                }
                Assert.Less(maxProb, 0.5f, "silence should stay below speech threshold");
            }
        }

        [Test]
        public void Tone_RunsGraphAndReturnsInRangeProbabilities()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "silero_vad.onnx");
            if (!File.Exists(path)) Assert.Ignore("silero_vad.onnx not present");

            using (var vad = new SileroVad(File.ReadAllBytes(path)))
            {
                float silenceMax = 0f, toneMax = 0f;
                for (int i = 0; i < 20; i++) silenceMax = Mathf.Max(silenceMax, vad.Process(new float[512]));
                vad.ResetState();
                int n = 0;
                for (int i = 0; i < 20; i++)
                {
                    var f = new float[512];
                    for (int j = 0; j < f.Length; j++, n++) f[j] = 0.5f * Mathf.Sin(2f * Mathf.PI * 300f * n / 16000f);
                    toneMax = Mathf.Max(toneMax, vad.Process(f));
                }
                Assert.GreaterOrEqual(silenceMax, 0f);
                Assert.LessOrEqual(toneMax, 1f);
            }
        }
    }
}
