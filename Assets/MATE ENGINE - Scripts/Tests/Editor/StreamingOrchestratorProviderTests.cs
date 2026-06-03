using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OpenaiCompatibleAgent;

namespace OpenaiCompatibleAgent.Tests
{
    [TestFixture]
    public class StreamingOrchestratorProviderTests
    {
        private sealed class StubTtsClient : ITtsClient
        {
            public Task<byte[]> SynthesizeAsync(string text, string referenceId, CancellationToken ct)
                => Task.FromResult<byte[]>(null);
        }

        [Test]
        public void ParseProvider_ParsesKnownNames()
        {
            Assert.AreEqual(TtsProvider.Irodori, StreamingOrchestrator.ParseProvider("Irodori"));
            Assert.AreEqual(TtsProvider.FishSpeech, StreamingOrchestrator.ParseProvider("FishSpeech"));
        }

        [Test]
        public void ParseProvider_FallsBackToFishSpeech_OnGarbageOrNull()
        {
            Assert.AreEqual(TtsProvider.FishSpeech, StreamingOrchestrator.ParseProvider("nonsense"));
            Assert.AreEqual(TtsProvider.FishSpeech, StreamingOrchestrator.ParseProvider(null));
            Assert.AreEqual(TtsProvider.FishSpeech, StreamingOrchestrator.ParseProvider(""));
            Assert.AreEqual(TtsProvider.FishSpeech, StreamingOrchestrator.ParseProvider("9"));
        }

        [Test]
        public void ResolveActiveClient_PicksByProvider()
        {
            var fish = new StubTtsClient();
            var iro = new StubTtsClient();
            Assert.AreSame(fish, StreamingOrchestrator.ResolveActiveClient(TtsProvider.FishSpeech, fish, iro));
            Assert.AreSame(iro, StreamingOrchestrator.ResolveActiveClient(TtsProvider.Irodori, fish, iro));
        }

        [Test]
        public void CurrentProvider_DefaultsToInspectorDefault_ThenHonorsSet()
        {
            var go = new UnityEngine.GameObject("__orchProviderTest");
            try
            {
                var orch = go.AddComponent<StreamingOrchestrator>();
                Assert.AreEqual(TtsProvider.FishSpeech, orch.CurrentProvider);
                orch.CurrentProvider = TtsProvider.Irodori;
                Assert.AreEqual(TtsProvider.Irodori, orch.CurrentProvider);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
