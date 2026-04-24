using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// WebSocket client for the DesktopMate channel on the nanobot_runtime
    /// backend. Speaks the protocol defined in
    /// ``nanobot_runtime/src/nanobot_runtime/channels/desktop_mate_protocol.py``.
    ///
    /// Handshake is URL-based (``?token=&client_id=``) — no post-connect
    /// ``authorize`` frame. The server signals readiness with an ``event: "ready"``
    /// frame. Every subsequent inbound event carries ``chat_id``; outbound
    /// traffic is either a ``new_chat`` (no chat_id, server mints one) or a
    /// ``message`` (with the bare chat_id, prefix stripped). ``SessionId``
    /// stores the full ``desktop_mate:&lt;chat_id&gt;`` key so it matches the
    /// REST surface's session keys — callers never assemble the prefix by
    /// hand, they use :meth:`ToSessionKey`/:meth:`ToChatId`.
    /// </summary>
    public class DesktopMatePlusClient : MonoBehaviour
    {
        public const string ChannelPrefix = "desktop_mate:";

        [Header("Connection")]
        public string host = "127.0.0.1";
        public int port = 8765;
        public string path = "/ws";
        public string token = "unity-client";
        public string clientId = "unity-client";

        [Header("TTS")]
        public bool ttsEnabled = true;
        public string referenceId = "七海";

        [Header("Settings")]
        public float reconnectDelay = 3f;
        public bool autoReconnect = true;
        // WebSocket protocol-level keepalive interval. Native ping/pong —
        // nothing application-level to wire up.
        public float keepAliveSeconds = 20f;

        // State
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public bool IsAuthenticated { get; private set; }
        public string ConnectionId { get; private set; }

        /// <summary>
        /// Full nanobot session key ("desktop_mate:&lt;chat_id&gt;") or null for
        /// a fresh conversation. Shared with the REST surface so the sidebar
        /// and chat panel agree on identity.
        /// </summary>
        public string SessionId { get; set; }

        // Events (main-thread safe via _mainThreadQueue)
        public event Action<ReadyData> OnReady;
        public event Action<DeltaData> OnDelta;
        public event Action<TtsChunkData> OnTtsChunk;
        public event Action<StreamStartData> OnStreamStart;
        public event Action<StreamEndData> OnStreamEnd;
        public event Action OnConnected;
        public event Action<string> OnDisconnected;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private bool _intentionalClose;
        private string _fullResponseText;

        // Active chat callbacks (for ChatBot.cs integration)
        private Action<string> _activePartialCallback;
        private Action<TtsChunkData> _activeTtsCallback;
        private Action _activeCompletionCallback;

        private string WsUrl
        {
            get
            {
                string normalisedPath = string.IsNullOrEmpty(path) ? "/ws" : path;
                if (!normalisedPath.StartsWith("/")) normalisedPath = "/" + normalisedPath;
                string qs = $"?token={Uri.EscapeDataString(token ?? string.Empty)}" +
                            $"&client_id={Uri.EscapeDataString(clientId ?? string.Empty)}";
                // Only emit ``tts=0`` for the disabled case. Server default is enabled,
                // so the positive case stays implicit and matches the inbound-message
                // ``tts_enabled`` field exactly.
                if (!ttsEnabled) qs += "&tts=0";
                return $"ws://{host}:{port}{normalisedPath}{qs}";
            }
        }

        void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogError($"[DMP] Main thread callback error: {e}"); }
            }
        }

        void OnDestroy()
        {
            _intentionalClose = true;
            _cts?.Cancel();
            CloseWebSocket();
        }

        // =================================================================
        // Prefix helpers
        // =================================================================

        /// <summary>Prefix a bare chat_id with ``desktop_mate:`` if not already present.</summary>
        public static string ToSessionKey(string chatId)
        {
            if (string.IsNullOrEmpty(chatId)) return chatId;
            return chatId.StartsWith(ChannelPrefix) ? chatId : ChannelPrefix + chatId;
        }

        /// <summary>Strip the ``desktop_mate:`` prefix from a session key to get the bare chat_id.</summary>
        public static string ToChatId(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey)) return sessionKey;
            return sessionKey.StartsWith(ChannelPrefix) ? sessionKey[ChannelPrefix.Length..] : sessionKey;
        }

        // =================================================================
        // Public API
        // =================================================================

        public void Connect()
        {
            if (IsConnected && IsAuthenticated) return;
            IsAuthenticated = false;
            ConnectionId = null;
            _ = ConnectAsync();
        }

        public void Disconnect()
        {
            _intentionalClose = true;
            CloseWebSocket();
        }

        /// <summary>
        /// Send a chat message with streaming callbacks. If :prop:`SessionId`
        /// is null/empty, a ``new_chat`` frame is sent and the server will
        /// reveal the new chat_id via the first ``stream_start``; otherwise a
        /// ``message`` frame is sent against the current session.
        ///
        /// <paramref name="images"/> is an optional list of ``data:image/png;base64,...``
        /// data URLs; when null it is omitted from the wire frame via
        /// <c>NullValueHandling.Ignore</c>.
        /// </summary>
        public void SendChat(string message, Action<string> onPartialToken, Action<TtsChunkData> onTtsChunk, Action onComplete, string[] images = null)
        {
            if (!IsAuthenticated)
            {
                Debug.LogWarning("[DMP] Not authenticated. Call Connect() first.");
                onComplete?.Invoke();
                return;
            }

            _fullResponseText = "";
            _activePartialCallback = onPartialToken;
            _activeTtsCallback = onTtsChunk;
            _activeCompletionCallback = onComplete;

            object frame;
            if (string.IsNullOrEmpty(SessionId))
            {
                frame = new NewChatMessage
                {
                    content = message,
                    tts_enabled = ttsEnabled,
                    reference_id = referenceId,
                    images = images,
                };
            }
            else
            {
                frame = new ChatMessage
                {
                    chat_id = ToChatId(SessionId),
                    content = message,
                    tts_enabled = ttsEnabled,
                    reference_id = referenceId,
                    images = images,
                };
            }
            _ = SendAsync(MessageParser.Serialize(frame));
        }

        /// <summary>
        /// Best-effort request to stop the in-flight stream. The new protocol
        /// has no server-side interrupt frame, so this only clears local
        /// active-turn callbacks; the remaining deltas/tts_chunks from the
        /// server are silently dropped until the next ``stream_end``.
        /// </summary>
        public void InterruptStream()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                _activePartialCallback = null;
                _activeTtsCallback = null;
                _activeCompletionCallback?.Invoke();
                _activeCompletionCallback = null;
            });
        }

        // =================================================================
        // Connection
        // =================================================================

        private async Task ConnectAsync()
        {
            _intentionalClose = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                _ws = new ClientWebSocket();
                if (keepAliveSeconds > 0)
                {
                    _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds);
                }
                await _ws.ConnectAsync(new Uri(WsUrl), _cts.Token);
                Debug.Log($"[DMP] Connected to {WsUrl}");
                _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());

                // Start receive loop — IsAuthenticated will be set on the
                // first ``ready`` frame.
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP] Connection failed: {e.Message}");
                ScheduleReconnect();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            HandleClose(result.CloseStatusDescription);
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    ProcessMessage(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException e)
            {
                Debug.LogWarning($"[DMP] WebSocket error: {e.Message}");
            }
            finally
            {
                HandleClose("Connection lost");
            }
        }

        // =================================================================
        // Message Routing
        // =================================================================

        private void ProcessMessage(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                string evt = obj["event"]?.ToString();

                switch (evt)
                {
                    case "ready":
                        var ready = MessageParser.ParseReady(obj);
                        ConnectionId = ready.connection_id;
                        IsAuthenticated = true;
                        Debug.Log($"[DMP] Ready: connection_id={ConnectionId}");
                        _mainThreadQueue.Enqueue(() => OnReady?.Invoke(ready));
                        break;

                    case "stream_start":
                        var startData = MessageParser.ParseStreamStart(obj);
                        // The first turn on a fresh session reveals the
                        // server-minted chat_id here. Rewrite SessionId so
                        // subsequent ``message`` frames target the same session.
                        if (string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(startData.chat_id))
                        {
                            SessionId = ToSessionKey(startData.chat_id);
                        }
                        _fullResponseText = "";
                        _mainThreadQueue.Enqueue(() => OnStreamStart?.Invoke(startData));
                        break;

                    case "delta":
                        var deltaData = MessageParser.ParseDelta(obj);
                        _fullResponseText += deltaData.text;
                        var currentText = _fullResponseText;
                        _mainThreadQueue.Enqueue(() =>
                        {
                            _activePartialCallback?.Invoke(currentText);
                            OnDelta?.Invoke(deltaData);
                        });
                        break;

                    case "tts_chunk":
                        // ``tts_chunk`` is permitted to arrive *after* ``stream_end``
                        // — the server's TTS barrier is best-effort and async
                        // synthesis may land out of order at the wire level. The
                        // FE must keep the socket open and dispatch the chunk
                        // normally.
                        var ttsData = MessageParser.ParseTtsChunk(obj);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            _activeTtsCallback?.Invoke(ttsData);
                            OnTtsChunk?.Invoke(ttsData);
                        });
                        break;

                    case "stream_end":
                        var endData = MessageParser.ParseStreamEnd(obj);
                        if (!string.IsNullOrEmpty(endData.chat_id))
                            SessionId = ToSessionKey(endData.chat_id);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            OnStreamEnd?.Invoke(endData);
                            _activeCompletionCallback?.Invoke();
                            _activePartialCallback = null;
                            _activeTtsCallback = null;
                            _activeCompletionCallback = null;
                        });
                        break;

                    default:
                        if (!string.IsNullOrEmpty(evt))
                            Debug.LogWarning($"[DMP] Unknown event '{evt}': {json}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP] Parse error: {e.Message}");
            }
        }

        // =================================================================
        // Helpers
        // =================================================================

        private async Task SendAsync(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP] Send error: {e.Message}");
            }
        }

        private void HandleClose(string reason)
        {
            IsAuthenticated = false;
            ConnectionId = null;
            _mainThreadQueue.Enqueue(() =>
            {
                _activeCompletionCallback?.Invoke();
                _activePartialCallback = null;
                _activeTtsCallback = null;
                _activeCompletionCallback = null;
                OnDisconnected?.Invoke(reason);
            });

            if (!_intentionalClose && autoReconnect)
                ScheduleReconnect();
        }

        private void CloseWebSocket()
        {
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
            IsAuthenticated = false;
        }

        private async void ScheduleReconnect()
        {
            if (_intentionalClose || !autoReconnect) return;
            Debug.Log($"[DMP] Reconnecting in {reconnectDelay}s...");
            await Task.Delay((int)(reconnectDelay * 1000));
            if (!_intentionalClose && !IsConnected)
                _ = ConnectAsync();
        }
    }
}
