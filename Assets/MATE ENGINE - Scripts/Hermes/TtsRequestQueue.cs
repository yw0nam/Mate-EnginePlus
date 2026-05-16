using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Hermes
{
    /// <summary>
    /// Starts Irodori synthesis requests concurrently, then emits completed WAVs in sequence order.
    /// </summary>
    /// <remarks>
    /// The emit cursor starts at sequence 0 for each turn. Callers that use a different first sequence
    /// must enqueue that lower sequence first or reset and start at 0.
    /// </remarks>
    public class TtsRequestQueue
    {
        private readonly IIrodoriClient _irodori;
        private readonly object _lock = new object();
        private readonly List<PendingRequest> _pending = new List<PendingRequest>();
        private readonly SortedDictionary<int, TtsResult> _completed = new SortedDictionary<int, TtsResult>();

        private int _nextSeqToEmit;
        private int _generation;

        public TtsRequestQueue(IIrodoriClient irodori)
        {
            _irodori = irodori ?? throw new ArgumentNullException(nameof(irodori));
        }

        public Action<int, byte[], string, List<Keyframe>> OnResult { get; set; }

        public void Enqueue(int sequence, string text, string emotion, List<Keyframe> keyframes, string referenceId)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            PendingRequest pending = new PendingRequest { Cts = cts };

            int generation;
            lock (_lock)
            {
                generation = _generation;
            }

            pending.Task = SynthesizeOneAsync(sequence, text, emotion, keyframes, referenceId, cts, pending, generation);

            lock (_lock)
            {
                if (generation == _generation)
                {
                    _pending.Add(pending);
                }
            }
        }

        public async Task WaitBarrierAsync(TimeSpan? timeout = null)
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            DateTime deadline = DateTime.UtcNow + effectiveTimeout;

            while (true)
            {
                List<PendingRequest> snapshot;
                lock (_lock)
                {
                    _pending.RemoveAll(item => item.Task == null || item.Task.IsCompleted);
                    if (_pending.Count == 0)
                    {
                        return;
                    }

                    snapshot = new List<PendingRequest>(_pending);
                }

                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    CancelStragglers(snapshot);
                    await WaitBrieflyForCancellationAsync(snapshot);
                    return;
                }

                Task allPending = Task.WhenAll(snapshot.ConvertAll(item => item.Task));
                Task finished = await Task.WhenAny(allPending, Task.Delay(remaining));
                if (finished != allPending)
                {
                    CancelStragglers(snapshot);
                    await WaitBrieflyForCancellationAsync(snapshot);
                    return;
                }

                try
                {
                    await allPending;
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Individual requests translate cancellation to a null WAV result before completing.
                }
            }
        }

        public void Reset()
        {
            List<PendingRequest> toCancel;
            lock (_lock)
            {
                _generation++;
                toCancel = new List<PendingRequest>(_pending);
                _pending.Clear();
                _completed.Clear();
                _nextSeqToEmit = 0;
            }

            CancelStragglers(toCancel);
        }

        private async Task SynthesizeOneAsync(
            int sequence,
            string text,
            string emotion,
            List<Keyframe> keyframes,
            string referenceId,
            CancellationTokenSource cts,
            PendingRequest pending,
            int generation)
        {
            byte[] wav = null;
            try
            {
                wav = await _irodori.SynthesizeAsync(text, referenceId, null, null, null, null, cts.Token);
            }
            catch (OperationCanceledException)
            {
                wav = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TtsRequestQueue] TTS synthesis failed for seq={sequence}: {ex.Message}");
                wav = null;
            }
            finally
            {
                lock (_lock)
                {
                    _pending.Remove(pending);
                }

                cts.Dispose();
            }

            StoreAndDrain(sequence, wav, emotion, keyframes, generation);
        }

        private void StoreAndDrain(int sequence, byte[] wav, string emotion, List<Keyframe> keyframes, int generation)
        {
            List<Emission> emissions = new List<Emission>();
            lock (_lock)
            {
                if (generation != _generation)
                {
                    return;
                }

                _completed[sequence] = new TtsResult
                {
                    Wav = wav,
                    Emotion = emotion,
                    Keyframes = keyframes,
                };

                while (_completed.TryGetValue(_nextSeqToEmit, out TtsResult result))
                {
                    _completed.Remove(_nextSeqToEmit);
                    emissions.Add(new Emission
                    {
                        Sequence = _nextSeqToEmit,
                        Wav = result.Wav,
                        Emotion = result.Emotion,
                        Keyframes = result.Keyframes,
                    });
                    _nextSeqToEmit++;
                }
            }

            for (int i = 0; i < emissions.Count; i++)
            {
                Emission emission = emissions[i];
                OnResult?.Invoke(emission.Sequence, emission.Wav, emission.Emotion, emission.Keyframes);
            }
        }

        private static void CancelStragglers(List<PendingRequest> requests)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                PendingRequest request = requests[i];
                if (request.Task != null && !request.Task.IsCompleted)
                {
                    request.Cts.Cancel();
                }
            }

            if (requests.Count > 0)
            {
                Debug.LogWarning($"[TtsRequestQueue] TTS barrier timed out; cancelled {requests.Count} pending request(s).");
            }
        }

        private static async Task WaitBrieflyForCancellationAsync(List<PendingRequest> requests)
        {
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].Task != null)
                {
                    tasks.Add(requests[i].Task);
                }
            }

            if (tasks.Count == 0)
            {
                return;
            }

            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(TimeSpan.FromMilliseconds(500)));
        }

        private sealed class PendingRequest
        {
            public CancellationTokenSource Cts;
            public Task Task;
        }

        private struct TtsResult
        {
            public byte[] Wav;
            public string Emotion;
            public List<Keyframe> Keyframes;
        }

        private struct Emission
        {
            public int Sequence;
            public byte[] Wav;
            public string Emotion;
            public List<Keyframe> Keyframes;
        }
    }
}
