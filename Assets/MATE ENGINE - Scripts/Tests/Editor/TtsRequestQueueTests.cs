using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenaiCompatibleAgent;
using NUnit.Framework;

namespace OpenaiCompatibleAgent.Tests
{
    [TestFixture]
    public class TtsRequestQueueTests
    {
        [Test]
        public async Task Enqueue_PreservesSequence_EvenIfCompletionReordered()
        {
            FakeTtsClient fake = new FakeTtsClient();
            fake.SetDelay(0, TimeSpan.FromMilliseconds(300));
            fake.SetDelay(1, TimeSpan.FromMilliseconds(100));
            fake.SetDelay(2, TimeSpan.FromMilliseconds(200));

            TtsRequestQueue queue = new TtsRequestQueue(fake);
            List<int> emitted = new List<int>();
            queue.OnResult = (seq, wav, emotion) => emitted.Add(seq);

            queue.Enqueue(0, "0", null, "voice");
            queue.Enqueue(1, "1", null, "voice");
            queue.Enqueue(2, "2", null, "voice");

            await queue.WaitBarrierAsync(TimeSpan.FromSeconds(5));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, emitted);
        }

        [Test]
        public async Task WaitBarrierAsync_TimesOutAndCancelsStragglers()
        {
            FakeTtsClient fake = new FakeTtsClient(TimeSpan.FromSeconds(60));
            TtsRequestQueue queue = new TtsRequestQueue(fake);
            List<byte[]> emitted = new List<byte[]>();
            queue.OnResult = (seq, wav, emotion) => emitted.Add(wav);

            Stopwatch stopwatch = Stopwatch.StartNew();
            queue.Enqueue(0, "0", null, "voice");
            await queue.WaitBarrierAsync(TimeSpan.FromSeconds(1));
            stopwatch.Stop();

            Assert.Less(stopwatch.Elapsed.TotalSeconds, 1.5d);
            Assert.AreEqual(1, emitted.Count);
            Assert.IsNull(emitted[0]);
        }

        [Test]
        public async Task Reset_ClearsState()
        {
            FakeTtsClient fake = new FakeTtsClient(TimeSpan.FromMilliseconds(10));
            TtsRequestQueue queue = new TtsRequestQueue(fake);
            List<int> emitted = new List<int>();
            queue.OnResult = (seq, wav, emotion) => emitted.Add(seq);

            queue.Enqueue(0, "0", null, "voice");
            queue.Enqueue(1, "1", null, "voice");
            await queue.WaitBarrierAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, emitted.Count);

            queue.Reset();
            queue.Enqueue(0, "0", null, "voice");
            await queue.WaitBarrierAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(3, emitted.Count);
            Assert.AreEqual(0, emitted[2]);
        }

        private sealed class FakeTtsClient : ITtsClient
        {
            private readonly Dictionary<int, TimeSpan> _delayBySeq = new Dictionary<int, TimeSpan>();
            private readonly TimeSpan _defaultDelay;

            public FakeTtsClient()
                : this(TimeSpan.Zero)
            {
            }

            public FakeTtsClient(TimeSpan defaultDelay)
            {
                _defaultDelay = defaultDelay;
            }

            public void SetDelay(int sequence, TimeSpan delay)
            {
                _delayBySeq[sequence] = delay;
            }

            public async Task<byte[]> SynthesizeAsync(string text, string referenceId, CancellationToken ct)
            {
                int sequence = int.Parse(text);
                TimeSpan delay = _delayBySeq.TryGetValue(sequence, out TimeSpan configuredDelay) ? configuredDelay : _defaultDelay;

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                return new byte[] { (byte)sequence, 1, 2, 3 };
            }
        }
    }
}
