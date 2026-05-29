using System.IO;
using System.Threading;
using NUnit.Framework;
using OpenaiCompatibleAgent.Voice;

namespace OpenaiCompatibleAgent.Tests
{
    public class AsrClientIntegrationTests
    {
        const string RefMp3 = @"D:\codes\waifu\references_voices\七海\merged_audio.mp3";

        [Test]
        public void Transcribe_ReturnsNonEmptyText_ForKnownSpeech()
        {
            if (!File.Exists(RefMp3)) Assert.Ignore("reference audio not present");

            byte[] audio = File.ReadAllBytes(RefMp3);
            var client = new AsrClient();
            using (var cts = new CancellationTokenSource(60000))
            {
                string text = client.TranscribeAsync(audio, cts.Token).GetAwaiter().GetResult();
                Assert.IsNotEmpty(text, "expected a transcription (is the ASR server up on :5517?)");
            }
        }
    }
}
