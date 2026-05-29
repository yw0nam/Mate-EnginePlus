using System;
using System.IO;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>Encodes mono float PCM (range [-1,1]) into a 16-bit PCM WAV byte array.</summary>
    public static class WavEncoder
    {
        public static byte[] Encode(float[] samples, int sampleRate)
        {
            if (samples == null) samples = Array.Empty<float>();
            const int channels = 1;
            const int bitsPerSample = 16;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int blockAlign = channels * bitsPerSample / 8;
            int dataSize = samples.Length * 2;

            using (var ms = new MemoryStream(44 + dataSize))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataSize);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(byteRate);
                w.Write((short)blockAlign);
                w.Write((short)bitsPerSample);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataSize);

                for (int i = 0; i < samples.Length; i++)
                {
                    float s = samples[i];
                    if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                    w.Write((short)(s * short.MaxValue));
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
