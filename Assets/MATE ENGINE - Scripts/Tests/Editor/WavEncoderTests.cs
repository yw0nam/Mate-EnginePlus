using NUnit.Framework;
using OpenaiCompatibleAgent.Voice;

namespace OpenaiCompatibleAgent.Tests
{
    public class WavEncoderTests
    {
        [Test]
        public void Encode_ProducesValidRiffWavHeader_ForMono16k()
        {
            float[] pcm = new float[16000]; // 1 second of silence @16k
            byte[] wav = WavEncoder.Encode(pcm, 16000);

            Assert.AreEqual(44 + pcm.Length * 2, wav.Length);
            Assert.AreEqual('R', wav[0]); Assert.AreEqual('I', wav[1]);
            Assert.AreEqual('F', wav[2]); Assert.AreEqual('F', wav[3]);
            Assert.AreEqual('W', wav[8]); Assert.AreEqual('A', wav[9]);
            Assert.AreEqual('V', wav[10]); Assert.AreEqual('E', wav[11]);
            Assert.AreEqual(1, wav[20]); // audio format = PCM
            Assert.AreEqual(1, wav[22]); // num channels = 1
            int sr = wav[24] | (wav[25] << 8) | (wav[26] << 16) | (wav[27] << 24);
            Assert.AreEqual(16000, sr);
            Assert.AreEqual(16, wav[34]); // bits per sample
        }

        [Test]
        public void Encode_ClampsAndScalesSamples()
        {
            float[] pcm = { 0f, 1f, -1f, 2f, -2f };
            byte[] wav = WavEncoder.Encode(pcm, 16000);

            short Sample(int i)
            {
                int off = 44 + i * 2;
                return (short)(wav[off] | (wav[off + 1] << 8));
            }
            Assert.AreEqual(0, Sample(0));
            Assert.AreEqual(short.MaxValue, Sample(1));
            Assert.AreEqual(-short.MaxValue, Sample(2));
            Assert.AreEqual(short.MaxValue, Sample(3));
            Assert.AreEqual(-short.MaxValue, Sample(4));
        }
    }
}
