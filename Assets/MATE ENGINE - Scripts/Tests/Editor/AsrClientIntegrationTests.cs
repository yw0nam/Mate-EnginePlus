using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using OpenaiCompatibleAgent.Voice;

namespace OpenaiCompatibleAgent.Tests
{
    public class AsrClientIntegrationTests
    {
        const string RefMp3 = @"D:\codes\waifu\references_voices\七海\merged_audio.mp3";

        // UnityTest (coroutine) so the async HTTP call is polled WITHOUT blocking the main
        // thread. Blocking with .GetAwaiter().GetResult() here deadlocks the editor, because
        // the awaited continuation tries to resume on the main thread that the block holds.
        [UnityTest]
        public IEnumerator Transcribe_ReturnsNonEmptyText_ForKnownSpeech()
        {
            if (!File.Exists(RefMp3)) Assert.Ignore("reference audio not present");

            byte[] audio = File.ReadAllBytes(RefMp3);
            var client = new AsrClient();
            var cts = new CancellationTokenSource(60000);
            Task<string> task = client.TranscribeAsync(audio, cts.Token);
            while (!task.IsCompleted) yield return null;
            cts.Dispose();

            Assert.IsNull(task.Exception, "transcription task threw: " + task.Exception);
            Assert.IsNotEmpty(task.Result, "expected a transcription (is the ASR server up on :5517?)");
        }
    }
}
