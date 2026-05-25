using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Responses;
using UnityEngine;
using Utilities.Async;
using Utilities.WebRequestRest.Interfaces;

namespace Hermes
{
    // SDK compatibility note (Phase A2): com.openai.unity maps Response.OutputText
    // only from the optional top-level "output_text" JSON field. hermes-agent
    // returns OpenAI-compatible nested output[].content[].text instead, so smoke
    // tests got an empty string despite a valid response. We log raw request/response
    // JSON and fall back to walking the nested message content manually.

    /// <summary>
    /// Thin MonoBehaviour wrapper around com.openai.unity's <see cref="OpenAIClient"/>
    /// for the self-hosted hermes-agent server. The client is configured for the
    /// Responses API at <c>http://localhost:8642/v1/responses</c> by default and
    /// uses bearer authorization through the SDK's custom-domain settings.
    /// </summary>
    /// <remarks>
    /// All streaming callbacks from the SDK are marshalled onto Unity's main
    /// thread via <see cref="_mainThreadQueue"/>, matching the pattern used by
    /// <c>DesktopMatePlusClient</c>.
    ///
    /// Streaming events map:
    /// <list type="bullet">
    /// <item><description><c>response.created</c> - stores <see cref="Response.Id"/> as <see cref="LastResponseId"/>.</description></item>
    /// <item><description><c>response.output_text.delta</c> - invokes the token delta callback.</description></item>
    /// <item><description><c>response.completed</c> - invokes the completion callback.</description></item>
    /// <item><description><c>response.failed</c> and <c>error</c> - invoke the error callback.</description></item>
    /// <item><description><c>response.refusal.delta</c> - logs a warning and treats refusal text as a normal delta.</description></item>
    /// </list>
    /// </remarks>
    [ExecuteAlways]
    public class HermesResponseClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string host = "localhost";
        [SerializeField] private int port = 8642;
        [SerializeField] private string apiKey = "hermes_api_key";

        [Header("Model")]
        [SerializeField] private string modelId = "hermes-agent";
        [SerializeField] private bool store = true;

        /// <summary>
        /// The most recent Responses API response id. Subsequent requests send
        /// this value as <c>previous_response_id</c> so the server can continue
        /// the conversation chain. Can also be set externally to restore a chain
        /// after loading a historical session (see SessionPanelController.SelectSession).
        /// </summary>
        public string LastResponseId { get; set; }

        /// <summary>
        /// API key used to initialize <see cref="OpenAIAuthentication"/>.
        /// </summary>
        public string ApiKey => apiKey;

        /// <summary>
        /// Host and port shown in the Inspector-friendly form used by hermes.
        /// </summary>
        public string BaseDomain => $"{host}:{port}";

        private OpenAIClient _client;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        /// <summary>
        /// Initializes the Unity OpenAI SDK client for the local hermes-agent
        /// endpoint. The SDK's custom-domain constructor defaults to HTTPS, so
        /// the domain is passed with an explicit <c>http://</c> scheme for the
        /// local non-TLS server.
        /// </summary>
        private void Awake()
        {
            InitializeClient();
        }

        /// <summary>
        /// Executes all queued SDK callbacks on Unity's main thread.
        /// </summary>
        private void Update()
        {
            PumpMainThreadQueue();
        }

        /// <summary>
        /// Drains the queued main-thread callbacks. Normally invoked by
        /// <see cref="Update"/>, but Editor-mode smoke tests and Phase C
        /// orchestrators can call this directly when <c>await Task.Delay</c>
        /// would otherwise starve the MonoBehaviour update tick.
        /// </summary>
        public void PumpMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogError($"[Hermes] Main thread callback error: {e}"); }
            }
        }

        /// <summary>
        /// Sends a user message to hermes-agent and streams token deltas through
        /// main-thread callbacks.
        /// </summary>
        /// <param name="userText">The user's message.</param>
        /// <param name="onTokenDelta">Called once per text delta on Unity's main thread.</param>
        /// <param name="onComplete">Called once when the streamed response completes.</param>
        /// <param name="onError">Called once when the SDK or server reports an error.</param>
        /// <param name="ct">Optional cancellation token.</param>
        public async Task SendAsync(
            string userText,
            Action<string> onTokenDelta,
            Action onComplete,
            Action<string> onError,
            CancellationToken ct = default)
        {
            EnsureClient();

            var errorQueued = false;
            var tokenDeltaQueued = false;
            var completionQueued = false;
            void QueueError(string message)
            {
                if (errorQueued)
                {
                    return;
                }

                errorQueued = true;
                EnqueueMainThread(() => onError?.Invoke(message));
            }

            try
            {
                var request = CreateRequest(userText);
                LogWireTrace("Streaming", "before-await", request);
                var streamState = new StreamingState();
                Func<string, IServerSentEvent, Task> handler = (eventType, sseEvent) =>
                {
                    HandleStreamEvent(
                        eventType,
                        sseEvent,
                        streamState,
                        delta =>
                        {
                            tokenDeltaQueued = true;
                            onTokenDelta?.Invoke(delta);
                        },
                        () =>
                        {
                            completionQueued = true;
                            onComplete?.Invoke();
                        },
                        QueueError);
                    return Task.CompletedTask;
                };

                var response = await _client.ResponsesEndpoint
                    .CreateModelResponseAsync(request, handler, ct)
                    .ConfigureAwait(false);

                LogWireTrace("Streaming", "after-await", null);
                Debug.Log($"[Hermes] Response RAW: {Newtonsoft.Json.JsonConvert.SerializeObject(response)}");
                Debug.Log($"[Hermes] Response.Id={response?.Id}, Status={response?.Status}, Output.Count={response?.Output?.Count}");

                if (!string.IsNullOrEmpty(response?.Id))
                {
                    LastResponseId = response.Id;
                }

                var fallbackText = ExtractOutputText(response);
                if (!tokenDeltaQueued && !string.IsNullOrEmpty(fallbackText))
                {
                    EnqueueMainThread(() => onTokenDelta?.Invoke(fallbackText));
                }

                if (!completionQueued)
                {
                    EnqueueMainThread(() => onComplete?.Invoke());
                }
            }
            catch (OperationCanceledException)
            {
                QueueError("Request cancelled.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Hermes] SendAsync failed: {e}");
                QueueError(e.Message);
            }
        }

        /// <summary>
        /// Sends a user message without streaming and returns the assembled
        /// response text. This is intended for smoke tests and debugging.
        /// </summary>
        /// <param name="userText">The user's message.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>The SDK response's assembled <see cref="Response.OutputText"/> value, or an empty string on failure.</returns>
        public async Task<string> SendNonStreamingAsync(string userText, CancellationToken ct = default)
        {
            EnsureClient();

            try
            {
                var request = CreateRequest(userText);
                LogWireTrace("NonStreaming", "before-await", request);

                var response = await _client.ResponsesEndpoint
                    .CreateModelResponseAsync(request, (Func<string, IServerSentEvent, Task>)null, ct)
                    .ConfigureAwait(false);

                LogWireTrace("NonStreaming", "after-await", null);
                Debug.Log($"[Hermes] Response RAW: {Newtonsoft.Json.JsonConvert.SerializeObject(response)}");
                Debug.Log($"[Hermes] Response.Id={response?.Id}, Status={response?.Status}, Output.Count={response?.Output?.Count}");

                if (!string.IsNullOrEmpty(response?.Id))
                {
                    LastResponseId = response.Id;
                }

                return ExtractOutputText(response);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[Hermes] Non-streaming request cancelled.");
                return string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Hermes] SendNonStreamingAsync failed: {e}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Clears <see cref="LastResponseId"/> so the next request starts a new
        /// Responses API chain.
        /// </summary>
        public void Reset()
        {
            LastResponseId = null;
        }

        private static JsonSerializerSettings _sdkSerializerSettings;

        private static JsonSerializerSettings GetSdkSerializerSettings()
        {
            if (_sdkSerializerSettings != null) return _sdkSerializerSettings;
            try
            {
                var prop = typeof(OpenAIClient).GetProperty(
                    "JsonSerializationOptions",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                _sdkSerializerSettings = prop?.GetValue(null) as JsonSerializerSettings;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Hermes-Trace] reflection for JsonSerializationOptions failed: {e.Message}");
            }
            return _sdkSerializerSettings;
        }

        private void LogWireTrace(string flow, string phase, object request)
        {
            try
            {
                var tid = Thread.CurrentThread.ManagedThreadId;
                var isMain = SyncContextUtility.IsMainThread;
                var unityTid = SyncContextUtility.UnityThreadId;
                var hasSyncCtx = SynchronizationContext.Current != null;
                var ctxType = SynchronizationContext.Current?.GetType().Name ?? "<null>";
                var isPlaying = isMain ? Application.isPlaying.ToString() : "<off-main>";
                Debug.Log(
                    $"[Hermes-Trace] flow={flow} phase={phase} tid={tid} unityTid={unityTid} " +
                    $"isMain={isMain} syncCtx={ctxType} hasSyncCtx={hasSyncCtx} isPlaying={isPlaying}");

                if (request != null)
                {
                    var sdkSettings = GetSdkSerializerSettings();
                    var realPayload = sdkSettings != null
                        ? JsonConvert.SerializeObject(request, sdkSettings)
                        : JsonConvert.SerializeObject(request);
                    Debug.Log($"[Hermes-Trace] {flow} real-wire JSON: {realPayload}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Hermes-Trace] LogWireTrace failed: {e}");
            }
        }

        private void InitializeClient()
        {
            var auth = new OpenAIAuthentication(apiKey);
            var settings = new OpenAISettings(domain: BuildSdkDomain());
            _client = new OpenAIClient(auth, settings);
            Debug.Log($"[Hermes] OpenAI client initialized for {settings.BaseRequestUrlFormat}");
        }

        private void EnsureClient()
        {
            if (_client == null)
            {
                InitializeClient();
            }
        }

        private CreateResponseRequest CreateRequest(string userText)
        {
            return new CreateResponseRequest(
                textInput: userText ?? string.Empty,
                model: modelId,
                previousResponseId: string.IsNullOrEmpty(LastResponseId) ? null : LastResponseId,
                store: store);
        }

        // Per-request state. The SDK's TextContent.Delta and RefusalContent.Delta
        // setters accumulate (`delta += value`) so reading them on each SSE event
        // yields the cumulative string. We track the previous cumulative value and
        // emit only the increment so downstream consumers (SentenceChunker, UI)
        // receive raw incremental tokens.
        private sealed class StreamingState
        {
            public string PreviousTextDelta = string.Empty;
            public string PreviousRefusalDelta = string.Empty;
        }

        private void HandleStreamEvent(
            string eventType,
            IServerSentEvent sseEvent,
            StreamingState state,
            Action<string> onTokenDelta,
            Action onComplete,
            Action<string> reportError)
        {
            Debug.Log("[H-evt] type=" + eventType + " sseType=" + (sseEvent == null ? "null" : sseEvent.GetType().FullName));
            switch (eventType)
            {
                case "response.created":
                    if (sseEvent is Response createdResponse && !string.IsNullOrEmpty(createdResponse.Id))
                    {
                        EnqueueMainThread(() => LastResponseId = createdResponse.Id);
                    }
                    break;

                case "response.output_text.delta":
                    if (sseEvent is OpenAI.Responses.TextContent textContent)
                    {
                        var cumulative = textContent.Delta ?? string.Empty;
                        var increment = cumulative.StartsWith(state.PreviousTextDelta, StringComparison.Ordinal)
                            ? cumulative.Substring(state.PreviousTextDelta.Length)
                            : cumulative;
                        state.PreviousTextDelta = cumulative;
                        if (increment.Length > 0)
                        {
                            EnqueueMainThread(() => onTokenDelta?.Invoke(increment));
                        }
                    }
                    break;

                case "response.completed":
                    if (sseEvent is Response completedResponse && !string.IsNullOrEmpty(completedResponse.Id))
                    {
                        EnqueueMainThread(() => LastResponseId = completedResponse.Id);
                    }
                    EnqueueMainThread(() => onComplete?.Invoke());
                    break;

                case "response.failed":
                    reportError(GetResponseErrorMessage(sseEvent));
                    break;

                case "response.refusal.delta":
                    if (sseEvent is RefusalContent refusalContent)
                    {
                        var cumulative = refusalContent.Delta ?? string.Empty;
                        var increment = cumulative.StartsWith(state.PreviousRefusalDelta, StringComparison.Ordinal)
                            ? cumulative.Substring(state.PreviousRefusalDelta.Length)
                            : cumulative;
                        state.PreviousRefusalDelta = cumulative;
                        if (increment.Length > 0)
                        {
                            EnqueueMainThread(() =>
                            {
                                Debug.LogWarning($"[Hermes] Response refusal delta: {increment}");
                                onTokenDelta?.Invoke(increment);
                            });
                        }
                    }
                    break;

                case "error":
                    var message = sseEvent is Error error ? error.Message : GetResponseErrorMessage(sseEvent);
                    reportError(message);
                    break;
            }
        }

        private static string GetResponseErrorMessage(IServerSentEvent sseEvent)
        {
            if (sseEvent is Response response && response.Error != null)
            {
                return response.Error.Message;
            }

            if (sseEvent is Error error)
            {
                return error.Message;
            }

            return sseEvent == null ? "Unknown Hermes response error." : sseEvent.ToString();
        }

        private static string ExtractOutputText(Response response)
        {
            if (!string.IsNullOrEmpty(response?.OutputText))
            {
                return response.OutputText;
            }

            if (response?.Output == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var item in response.Output)
            {
                if (item is not Message message || message.Content == null)
                {
                    continue;
                }

                foreach (var content in message.Content)
                {
                    if (content is OpenAI.Responses.TextContent textContent)
                    {
                        builder.Append(textContent.Text ?? textContent.Delta);
                    }
                }
            }

            return builder.ToString();
        }

        private void EnqueueMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        private string BuildSdkDomain()
        {
            var trimmedHost = (host ?? string.Empty).Trim();

            if (trimmedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var schemeLength = trimmedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7 : 8;
                var withoutScheme = trimmedHost.Substring(schemeLength);
                return withoutScheme.Contains(":") ? trimmedHost : $"{trimmedHost}:{port}";
            }

            return $"http://{trimmedHost}:{port}";
        }
    }
}
