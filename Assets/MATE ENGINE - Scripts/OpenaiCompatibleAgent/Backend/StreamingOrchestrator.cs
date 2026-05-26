using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Hermes
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

        public async Task SendAsync(
            string userText,
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
                    await hermesClient.SendAsync(userText, HandleTokenDelta, HandleStreamComplete, HandleStreamError, ct);
                    await _turnTcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                _ttsQueue?.Reset();
                onError?.Invoke("Request cancelled.");
            }
            catch (Exception ex)
            {
                _ttsQueue?.Reset();
                onError?.Invoke(ex.Message);
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
                _onTurnComplete?.Invoke();
                _turnTcs?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                HandleStreamError(ex.Message);
            }
        }

        private void HandleStreamError(string message)
        {
            _ttsQueue?.Reset();
            _onError?.Invoke(message);
            _turnTcs?.TrySetException(new InvalidOperationException(message));
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
