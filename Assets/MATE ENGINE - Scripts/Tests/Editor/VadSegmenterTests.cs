using System.Collections.Generic;
using NUnit.Framework;
using OpenaiCompatibleAgent.Voice;

namespace OpenaiCompatibleAgent.Tests
{
    public class VadSegmenterTests
    {
        const int FrameSamples = 512;
        const int SampleRate = 16000;

        static float[] Frame(float fill)
        {
            var f = new float[FrameSamples];
            for (int i = 0; i < f.Length; i++) f[i] = fill;
            return f;
        }

        static VadSegmenter MakeSegmenter(out List<float[]> emitted, out List<bool> started)
        {
            var seg = new VadSegmenter(new VadSegmenter.Config
            {
                sampleRate = SampleRate,
                frameSamples = FrameSamples,
                threshold = 0.5f,
                minSpeechMs = 100,
                minSilenceMs = 200,
                speechPadMs = 64,
                maxSpeechMs = 2000
            });
            var e = new List<float[]>(); var s = new List<bool>();
            seg.OnSpeechStart += () => s.Add(true);
            seg.OnSpeechEnd += pcm => e.Add(pcm);
            emitted = e; started = s;
            return seg;
        }

        [Test]
        public void EmitsUtterance_OnSpeechThenSilence()
        {
            var seg = MakeSegmenter(out var emitted, out var started);
            for (int i = 0; i < 10; i++) seg.Process(Frame(0.9f), 0.9f);
            for (int i = 0; i < 10; i++) seg.Process(Frame(0f), 0.0f);
            Assert.AreEqual(1, started.Count, "one speech-start");
            Assert.AreEqual(1, emitted.Count, "one utterance emitted");
            Assert.Greater(emitted[0].Length, FrameSamples * 8, "utterance contains the speech frames");
        }

        [Test]
        public void IgnoresShortBlip_BelowMinSpeech()
        {
            var seg = MakeSegmenter(out var emitted, out var started);
            seg.Process(Frame(0.9f), 0.9f);
            for (int i = 0; i < 10; i++) seg.Process(Frame(0f), 0.0f);
            Assert.AreEqual(0, emitted.Count, "blip shorter than minSpeechMs is discarded");
        }

        [Test]
        public void ForceEnds_AtMaxSpeech()
        {
            var seg = MakeSegmenter(out var emitted, out var started);
            for (int i = 0; i < 100; i++) seg.Process(Frame(0.9f), 0.9f);
            Assert.GreaterOrEqual(emitted.Count, 1, "force-ends at maxSpeechMs");
        }
    }
}
