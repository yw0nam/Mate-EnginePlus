using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Utilities.Async;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Coordinates Hermes streaming text, sentence chunking, emotion mapping, Irodori TTS, and audio playback.
    /// </summary>
    /// <remarks>
    /// <see cref="HermesResponseClient"/> already marshals streaming callbacks onto Unity's main thread,
    /// so this orchestrator does not need its own ConcurrentQueue pump.
    /// </remarks>
    [ExecuteAlways]
    public class StreamingOrchestrator : MonoBehaviour
    {
        [Header("Clients")]
        [SerializeField] private HermesResponseClient hermesClient;
        [SerializeField] private IrodoriClient irodoriClient;
        [SerializeField] private FastBunkaiSidecarClient sidecarClient;

        [Header("Processing")]
        [SerializeField] private Component ttsAudioPlayer;
        [SerializeField] private string referenceVoiceId = "七海";
        [SerializeField] private int sentenceMinChunkLength = 50;
        [SerializeField] private float ttsBarrierTimeoutSeconds = 30f;

        /// <summary>
        /// Runtime voice id. When set by VoiceCatalogHandler (or any UI), this
        /// overrides the Inspector default for all subsequent TTS requests.
        /// Falls back to <see cref="referenceVoiceId"/> when null or empty.
        /// </summary>
        public string CurrentVoiceId { get; set; }

        /// <summary>
        /// Sentence chunker min length. Applied on the next turn (chunker is
        /// recreated each send).
        /// </summary>
        public int SentenceMinChunkLength
        {
            get => sentenceMinChunkLength;
            set => sentenceMinChunkLength = Mathf.Max(1, value);
        }

        /// <summary>
        /// TTS barrier timeout in seconds, read on each barrier wait. Takes
        /// effect on the next turn.
        /// </summary>
        public float TtsBarrierTimeoutSeconds
        {
            get => ttsBarrierTimeoutSeconds;
            set => ttsBarrierTimeoutSeconds = Mathf.Max(0.1f, value);
        }

        private SentenceChunker _chunker;
        private TtsRequestQueue _ttsQueue;
        private int _nextSequence;
        private TaskCompletionSource<bool> _turnTcs;
        private CancellationToken _turnCancellationToken;
        private Action<string> _onTokenDelta;
        private Action _onTurnComplete;
        private Action<string> _onError;

        /// <summary>
        /// Pass-through to the wired <see cref="HermesResponseClient"/>'s last response id.
        /// Useful for E2E tests / UI code that need the canonical id from the same instance
        /// the orchestrator actually streamed through.
        /// </summary>
        public string LastResponseId => hermesClient != null ? hermesClient.LastResponseId : null;

        private void Awake()
        {
            EnsureComposed();
            if (string.IsNullOrEmpty(CurrentVoiceId))
                CurrentVoiceId = referenceVoiceId;
        }

        public Task SendAsync(
            string userText,
            Action<string> onTokenDelta,
            Action onTurnComplete,
            Action<string> onError,
            CancellationToken ct = default)
        {
            return SendAsync(userText, null, onTokenDelta, onTurnComplete, onError, ct);
        }

        /// <summary>
        /// Multimodal overload: forwards <paramref name="imageDataUrls"/> as
        /// <c>input_image</c> content items to <see cref="HermesResponseClient.SendAsync"/>.
        /// </summary>
        public async Task SendAsync(
            string userText,
            IReadOnlyList<string> imageDataUrls,
            Action<string> onTokenDelta,
            Action onTurnComplete,
            Action<string> onError,
            CancellationToken ct = default)
        {
            _onTokenDelta = onTokenDelta;
            _onTurnComplete = onTurnComplete;
            _onError = onError;
            _turnCancellationToken = ct;
            _turnTcs = new TaskCompletionSource<bool>();

            try
            {
                EnsureComposed();
                ResetTurnState();

                using (ct.Register(() => _turnTcs.TrySetCanceled()))
                {
                    await hermesClient.SendAsync(userText, imageDataUrls, HandleTokenDelta, HandleStreamComplete, HandleStreamError, ct);
                    await _turnTcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                _ttsQueue?.Reset();
                var cb = onError;
                SyncContextUtility.RunOnUnityThread(() => cb?.Invoke("Request cancelled."));
            }
            catch (Exception ex)
            {
                _ttsQueue?.Reset();
                var cb = onError;
                var msg = ex.Message;
                SyncContextUtility.RunOnUnityThread(() => cb?.Invoke(msg));
            }
        }

        private void EnsureComposed()
        {
            if (sidecarClient == null)
            {
                sidecarClient = new FastBunkaiSidecarClient();
            }

            if (_chunker == null)
            {
                _chunker = CreateChunker();
            }

            if (_ttsQueue == null && irodoriClient != null)
            {
                _ttsQueue = new TtsRequestQueue(irodoriClient);
                _ttsQueue.OnResult = HandleTtsResult;
            }
        }

        private void ResetTurnState()
        {
            if (hermesClient == null)
            {
                throw new InvalidOperationException("HermesResponseClient is not assigned.");
            }

            if (irodoriClient == null)
            {
                throw new InvalidOperationException("IrodoriClient is not assigned.");
            }

            if (_ttsQueue == null)
            {
                _ttsQueue = new TtsRequestQueue(irodoriClient);
                _ttsQueue.OnResult = HandleTtsResult;
            }

            _chunker = CreateChunker();
            _ttsQueue.Reset();
            _nextSequence = 0;
            ResetTtsAudioPlayer();
        }

        // The orchestrator's per-turn _nextSequence resets to 0 every turn, but the
        // TtsAudioPlayer's playback cursor (_nextSequence inside the player) does not -
        // so after Turn 1 plays seq=0, the player advances to expect seq=1 next, while
        // Turn 2 enqueues a fresh seq=0 that never gets played. Resetting the player
        // at turn start keeps both cursors aligned.
        private void ResetTtsAudioPlayer()
        {
            if (ttsAudioPlayer == null)
            {
                return;
            }

            MethodInfo reset = ttsAudioPlayer.GetType().GetMethod("Reset", Type.EmptyTypes);
            reset?.Invoke(ttsAudioPlayer, null);
        }

        private SentenceChunker CreateChunker()
        {
            return new SentenceChunker(sidecarClient, sentenceMinChunkLength, "<think>", "</think>");
        }

        private async void HandleTokenDelta(string delta)
        {
            try
            {
                _onTokenDelta?.Invoke(delta);
                List<string> sentences = await _chunker.FeedAsync(delta, _turnCancellationToken);
                for (int i = 0; i < sentences.Count; i++)
                {
                    EnqueueSentence(sentences[i]);
                }
            }
            catch (Exception ex)
            {
                HandleStreamError(ex.Message);
            }
        }

        private async void HandleStreamComplete()
        {
            try
            {
                string remainder = _chunker.Flush();
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    EnqueueSentence(remainder);
                }

                await _ttsQueue.WaitBarrierAsync(TimeSpan.FromSeconds(ttsBarrierTimeoutSeconds));

                // After the TTS barrier the await typically resumes on a worker
                // thread (HttpClient + Task.Delay continuations). Unity API calls
                // inside _onTurnComplete (SetTalking, ShowThinking, SetInputInteractable)
                // would either silently fail or throw off-main-thread, leaving
                // DmpChatController._isStreaming stuck at true forever. Marshal
                // back to Unity's main thread before invoking the callback.
                Debug.Log(string.Format(
                    "[Orchestrator] phase=after-barrier tid={0} unityTid={1} isMain={2} hasSyncCtx={3}",
                    Thread.CurrentThread.ManagedThreadId,
                    SyncContextUtility.UnityThreadId,
                    SyncContextUtility.IsMainThread,
                    SynchronizationContext.Current != null));

                var completeCallback = _onTurnComplete;
                var tcs = _turnTcs;
                SyncContextUtility.RunOnUnityThread(() =>
                {
                    try
                    {
                        completeCallback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Orchestrator] onTurnComplete threw: {ex}");
                    }
                    finally
                    {
                        tcs?.TrySetResult(true);
                    }
                });
            }
            catch (Exception ex)
            {
                HandleStreamError(ex.Message);
            }
        }

        private void HandleStreamError(string message)
        {
            _ttsQueue?.Reset();

            // Same main-thread marshaling rationale as HandleStreamComplete:
            // when invoked from an awaited continuation it may run off-main.
            var errorCallback = _onError;
            var tcs = _turnTcs;
            SyncContextUtility.RunOnUnityThread(() =>
            {
                try
                {
                    errorCallback?.Invoke(message);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Orchestrator] onError threw: {ex}");
                }
                finally
                {
                    tcs?.TrySetException(new InvalidOperationException(message));
                }
            });
        }

        private void EnqueueSentence(string sentence)
        {
            var processed = Preprocessor.Process(sentence);
            if (string.IsNullOrWhiteSpace(processed.clean))
            {
                return;
            }

            _ttsQueue.Enqueue(_nextSequence++, processed.clean, processed.emotion, !string.IsNullOrEmpty(CurrentVoiceId) ? CurrentVoiceId : referenceVoiceId);
        }

        private void HandleTtsResult(int seq, byte[] wav, string emotion)
        {
            if (wav != null && wav.Length > 0)
            {
                EnqueueWavBytes(seq, wav, emotion);
                return;
            }

            Debug.LogWarning($"[Orchestrator] TTS synthesis returned null/empty for seq={seq}");
        }

        private void EnqueueWavBytes(int seq, byte[] wav, string emotion)
        {
            if (ttsAudioPlayer == null)
            {
                return;
            }

            MethodInfo method = ttsAudioPlayer.GetType().GetMethod(
                "EnqueueWavBytes",
                new[] { typeof(int), typeof(byte[]), typeof(string) });
            if (method == null)
            {
                Debug.LogWarning("[Orchestrator] Assigned TTS audio player does not expose EnqueueWavBytes(int, byte[], string).");
                return;
            }

            method.Invoke(ttsAudioPlayer, new object[] { seq, wav, emotion });
        }
    }
}
