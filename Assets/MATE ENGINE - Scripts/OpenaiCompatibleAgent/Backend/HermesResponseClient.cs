using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Self-contained streaming client for an OpenAI Responses API endpoint (the self-hosted
    /// hermes-agent server at <c>http://localhost:8642/v1/responses</c> by default).
    /// </summary>
    /// <remarks>
    /// This deliberately uses NO third-party OpenAI SDK. It issues the HTTP POST itself and parses
    /// the Server-Sent Events stream with a forward-only reader, extracting only the events a chat
    /// front-end needs:
    /// <list type="bullet">
    /// <item><description><c>response.created</c> / <c>response.in_progress</c> — capture the response id for the <c>previous_response_id</c> chain.</description></item>
    /// <item><description><c>response.output_text.delta</c> — incremental assistant text.</description></item>
    /// <item><description><c>response.refusal.delta</c> — refusal text, surfaced as a normal delta.</description></item>
    /// <item><description><c>response.completed</c> — turn finished.</description></item>
    /// <item><description><c>response.failed</c> / <c>error</c> — failure.</description></item>
    /// </list>
    /// Every other event type (<c>response.function_call_arguments.*</c>, <c>file_search.*</c>,
    /// <c>code_interpreter.*</c>, <c>response.output_item.*</c>, <c>response.content_part.*</c>, …)
    /// is ignored. Unknown fields are never read, so tool-augmented responses — including Hermes
    /// returning <c>function_call_output.output</c> as an array — can no longer break parsing. That
    /// array case is exactly what the previous com.openai.unity SDK choked on.
    ///
    /// All callbacks are marshalled onto Unity's main thread via <see cref="_mainThreadQueue"/>,
    /// drained by <see cref="PumpMainThreadQueue"/> from <see cref="Update"/>.
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
        [SerializeField] private string reasoningEffort = "low";
        [SerializeField] private string hermesModel = "";

        /// <summary>
        /// The most recent Responses API response id. Subsequent requests send this value as
        /// <c>previous_response_id</c> so the server continues the conversation chain. Can also be
        /// set externally to restore a chain after loading a historical session
        /// (see SessionPanelController.SelectSession).
        /// </summary>
        public string LastResponseId { get; set; }

        /// <summary>API key sent as the bearer token.</summary>
        public string ApiKey => apiKey;

        /// <summary>Host and port in the Inspector-friendly <c>host:port</c> form.</summary>
        public string BaseDomain => $"{host}:{port}";

        public string Host { get => host; set => host = value; }
        public int Port { get => port; set => port = value; }
        public string ModelId { get => modelId; set => modelId = value; }

        /// <summary>
        /// Reasoning effort sent as <c>reasoning.effort</c> in the request body
        /// (none/minimal/low/medium/high/xhigh). The value <c>"none"</c> (or empty)
        /// omits the <c>reasoning</c> object entirely, deferring to the server default.
        /// </summary>
        public string ReasoningEffort { get => reasoningEffort; set => reasoningEffort = value; }

        /// <summary>
        /// Optional Hermes provider-routing model sent as <c>hermes_model</c> in the request body.
        /// Empty means no override — the server uses its configured default for the API alias.
        /// </summary>
        public string HermesModel { get => hermesModel; set => hermesModel = value; }

        public void SetApiKey(string value) => apiKey = value;

        /// <summary>Rebuilds the cached base URL after mutating connection fields at runtime.</summary>
        public void Reinitialize() => _baseUrl = BuildBaseUrl();

        // Shared client. Infinite timeout because SSE streams stay open for the whole turn;
        // cancellation is per-request via the CancellationToken.
        private static readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private string _baseUrl;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        private void Awake() => _baseUrl = BuildBaseUrl();

        private void Update() => PumpMainThreadQueue();

        /// <summary>
        /// Drains queued callbacks on Unity's main thread. Normally invoked by <see cref="Update"/>,
        /// but Editor-mode smoke tests can call it directly when <c>await Task.Delay</c> would
        /// otherwise starve the MonoBehaviour update tick.
        /// </summary>
        public void PumpMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogError($"[Hermes] Main thread callback error: {e}"); }
            }
        }

        private void Enqueue(Action action) => _mainThreadQueue.Enqueue(action);

        private string BuildBaseUrl()
        {
            var h = (host ?? string.Empty).Trim();
            if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var schemeLen = h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7 : 8;
                var rest = h.Substring(schemeLen);
                return rest.Contains(":") ? h.TrimEnd('/') : $"{h.TrimEnd('/')}:{port}";
            }
            return $"http://{h}:{port}";
        }

        private string EndpointUrl()
        {
            if (string.IsNullOrEmpty(_baseUrl)) _baseUrl = BuildBaseUrl();
            return _baseUrl.TrimEnd('/') + "/v1/responses";
        }

        // ===================== Public send API =====================

        /// <summary>Streams a user message; token deltas arrive through main-thread callbacks.</summary>
        public Task SendAsync(
            string userText,
            Action<string> onTokenDelta,
            Action onComplete,
            Action<string> onError,
            CancellationToken ct = default)
            => SendAsync(userText, null, onTokenDelta, onComplete, onError, ct);

        /// <summary>
        /// Multimodal overload: send a user message together with one or more images encoded as
        /// base64 data URLs. Each URL becomes an <c>input_image</c> content item.
        /// </summary>
        public async Task SendAsync(
            string userText,
            IReadOnlyList<string> imageDataUrls,
            Action<string> onTokenDelta,
            Action onComplete,
            Action<string> onError,
            CancellationToken ct = default)
        {
            var errorQueued = false;
            var completionQueued = false;

            void QueueError(string message)
            {
                if (errorQueued || completionQueued) return;
                errorQueued = true;
                Enqueue(() => onError?.Invoke(message));
            }

            void QueueComplete()
            {
                if (completionQueued || errorQueued) return;
                completionQueued = true;
                Enqueue(() => onComplete?.Invoke());
            }

            try
            {
                string body = BuildRequestBody(userText, imageDataUrls, stream: true);
                Debug.Log($"[Hermes] POST {EndpointUrl()} ({body.Length} chars){(body.Length > 300 ? " body[0:300]=" + body.Substring(0, 300) + "…" : " body=" + body)}");

                using (var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl()))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Headers.Accept.ParseAdd("text/event-stream");
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using (var response = await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            QueueError($"HTTP {(int)response.StatusCode}: {errBody}");
                            return;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            await ReadSseAsync(reader, onTokenDelta, QueueComplete, QueueError, ct).ConfigureAwait(false);
                        }
                    }
                }

                // Stream ended without an explicit completed/failed/error event → still finish the turn.
                QueueComplete();
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
        /// Non-streaming send used by smoke tests/debugging. Returns the assembled assistant text.
        /// </summary>
        public async Task<string> SendNonStreamingAsync(string userText, CancellationToken ct = default)
        {
            try
            {
                string body = BuildRequestBody(userText, null, stream: false);
                using (var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl()))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                        .ConfigureAwait(false))
                    {
                        string respBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.LogError($"[Hermes] Non-streaming HTTP {(int)response.StatusCode}: {respBody}");
                            return string.Empty;
                        }

                        var obj = JObject.Parse(respBody);
                        var id = obj.Value<string>("id");
                        if (!string.IsNullOrEmpty(id)) LastResponseId = id;
                        return ExtractOutputText(obj);
                    }
                }
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

        /// <summary>Clears <see cref="LastResponseId"/> so the next request starts a new chain.</summary>
        public void Reset() => LastResponseId = null;

        // ===================== SSE parsing =====================

        private async Task ReadSseAsync(
            StreamReader reader,
            Action<string> onTokenDelta,
            Action queueComplete,
            Action<string> queueError,
            CancellationToken ct)
        {
            var data = new StringBuilder();
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

                if (line.Length == 0)
                {
                    // Blank line terminates one event.
                    if (data.Length > 0)
                    {
                        bool terminal = DispatchEvent(data.ToString(), onTokenDelta, queueComplete, queueError);
                        data.Clear();
                        if (terminal) return;
                    }
                    continue;
                }

                // Accumulate "data:" payload lines; ignore "event:", "id:", and ":" comments.
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var payload = line.Substring(5);
                    if (payload.Length > 0 && payload[0] == ' ') payload = payload.Substring(1);
                    if (data.Length > 0) data.Append('\n');
                    data.Append(payload);
                }
            }

            // Flush a trailing event with no terminating blank line.
            if (data.Length > 0)
                DispatchEvent(data.ToString(), onTokenDelta, queueComplete, queueError);
        }

        /// <summary>Parses one SSE data payload and fires callbacks. Returns true if terminal.</summary>
        private bool DispatchEvent(
            string json,
            Action<string> onTokenDelta,
            Action queueComplete,
            Action<string> queueError)
        {
            if (json == "[DONE]")
            {
                queueComplete();
                return true;
            }

            JObject obj;
            try { obj = JObject.Parse(json); }
            catch { return false; } // ignore frames we can't parse — never fatal

            var type = obj.Value<string>("type");
            if (string.IsNullOrEmpty(type)) return false;

            switch (type)
            {
                case "response.created":
                case "response.in_progress":
                {
                    var id = obj["response"]?.Value<string>("id");
                    if (!string.IsNullOrEmpty(id)) Enqueue(() => LastResponseId = id);
                    return false;
                }

                case "response.output_text.delta":
                {
                    var delta = obj.Value<string>("delta");
                    if (!string.IsNullOrEmpty(delta)) Enqueue(() => onTokenDelta?.Invoke(delta));
                    return false;
                }

                case "response.refusal.delta":
                {
                    var delta = obj.Value<string>("delta");
                    if (!string.IsNullOrEmpty(delta))
                        Enqueue(() =>
                        {
                            Debug.LogWarning($"[Hermes] Response refusal delta: {delta}");
                            onTokenDelta?.Invoke(delta);
                        });
                    return false;
                }

                case "response.completed":
                {
                    var id = obj["response"]?.Value<string>("id");
                    if (!string.IsNullOrEmpty(id)) Enqueue(() => LastResponseId = id);
                    queueComplete();
                    return true;
                }

                case "response.incomplete":
                {
                    // HITL interrupt: the agent paused for human approval (metadata.interrupt
                    // carries the request). We keep the response id for chaining and end the turn
                    // cleanly. The approve/reject/edit resume flow is a separate FE feature.
                    var id = obj["response"]?.Value<string>("id");
                    if (!string.IsNullOrEmpty(id)) Enqueue(() => LastResponseId = id);
                    var interrupt = obj["response"]?["metadata"]?["interrupt"];
                    Debug.LogWarning($"[Hermes] response.incomplete (HITL interrupt){(interrupt != null ? ": " + interrupt.ToString(Formatting.None) : "")}");
                    queueComplete();
                    return true;
                }

                case "response.failed":
                {
                    var msg = obj["response"]?["error"]?.Value<string>("message") ?? "Hermes response failed.";
                    queueError(msg);
                    return true;
                }

                case "error":
                {
                    var msg = obj.Value<string>("message")
                              ?? obj["error"]?.Value<string>("message")
                              ?? "Hermes stream error.";
                    var code = obj.Value<string>("code");
                    queueError(string.IsNullOrEmpty(code) ? msg : $"{code}: {msg}");
                    return true;
                }

                default:
                    return false; // function_call_*, file_search_*, code_interpreter_*, item/part events, etc.
            }
        }

        // ===================== Request / response bodies =====================

        private string BuildRequestBody(string userText, IReadOnlyList<string> imageDataUrls, bool stream)
        {
            var root = new JObject
            {
                ["model"] = modelId,
                ["store"] = store,
                ["stream"] = stream,
                ["tool_choice"] = "none",
                ["truncation"] = "auto",
            };

            // reasoning.effort — "none"/empty omits the object so the server default applies.
            if (!string.IsNullOrEmpty(reasoningEffort) &&
                !string.Equals(reasoningEffort, "none", StringComparison.OrdinalIgnoreCase))
                root["reasoning"] = new JObject { ["effort"] = reasoningEffort };

            // hermes_model — empty means no override (server default for the API alias);
            // provider is inferred server-side from the model name.
            if (!string.IsNullOrEmpty(hermesModel))
                root["hermes_model"] = hermesModel;

            if (!string.IsNullOrEmpty(LastResponseId))
                root["previous_response_id"] = LastResponseId;

            string text = userText ?? string.Empty;

            if (imageDataUrls == null || imageDataUrls.Count == 0)
            {
                // Text-only fast path: input is a plain string.
                root["input"] = text;
            }
            else
            {
                // Multimodal: a single user message with a text part + one input_image per data URL.
                var content = new JArray
                {
                    new JObject { ["type"] = "input_text", ["text"] = text }
                };
                foreach (var url in imageDataUrls)
                {
                    if (string.IsNullOrEmpty(url)) continue;
                    content.Add(new JObject { ["type"] = "input_image", ["image_url"] = url });
                }

                var message = new JObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = content
                };
                root["input"] = new JArray { message };
            }

            return root.ToString(Formatting.None);
        }

        /// <summary>
        /// Walks a non-streaming Response JSON for assistant text. Prefers the convenience
        /// <c>output_text</c> field, otherwise concatenates <c>output[].content[].text</c>.
        /// </summary>
        private static string ExtractOutputText(JObject response)
        {
            var top = response.Value<string>("output_text");
            if (!string.IsNullOrEmpty(top)) return top;

            var output = response["output"] as JArray;
            if (output == null) return string.Empty;

            var builder = new StringBuilder();
            foreach (var item in output)
            {
                if (item["content"] is not JArray content) continue;
                foreach (var part in content)
                {
                    var partType = part.Value<string>("type");
                    if (partType == "output_text" || partType == "text")
                    {
                        var t = part.Value<string>("text");
                        if (!string.IsNullOrEmpty(t)) builder.Append(t);
                    }
                }
            }
            return builder.ToString();
        }
    }
}
