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
    /// WebSocket client for the DesktopMatePlus backend.
    /// Manages connection lifecycle, authentication, heartbeat, and message routing.
    /// </summary>
    public class DesktopMatePlusClient : MonoBehaviour
    {
        [Header("Connection")]
        public string host = "127.0.0.1";
        public int port = 5600;
        public string token = "unity-client";

        [Header("Identity")]
        public string agentId = "yuri-assistant";
        public string userId = "unity-user";
        public string personaId = "yuri";

        [Header("TTS")]
        public bool ttsEnabled = true;
        public string referenceId = "七海";

        [Header("Settings")]
        public float reconnectDelay = 3f;
        public bool autoReconnect = true;
        public int stmLimit = 10;

        // State
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public bool IsAuthenticated { get; private set; }
        public string ConnectionId { get; private set; }
        public string SessionId { get; set; }

        // Events (main-thread safe via _mainThreadQueue)
        public event Action<StreamTokenData> OnStreamToken;
        public event Action<TtsChunkData> OnTtsChunk;
        public event Action<StreamStartData> OnStreamStart;
        public event Action<StreamEndData> OnStreamEnd;
        public event Action<ErrorData> OnError;
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

        private string WsUrl => $"ws://{host}:{port}/v1/chat/stream";

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
        // Public API
        // =================================================================

        /// <summary>
        /// Connect to the DesktopMatePlus backend.
        /// </summary>
        public void Connect()
        {
            if (IsConnected && IsAuthenticated) return;
            // Reset stale state (e.g. after domain reload)
            IsAuthenticated = false;
            ConnectionId = null;
            _ = ConnectAsync();
        }

        /// <summary>
        /// Disconnect from the backend.
        /// </summary>
        public void Disconnect()
        {
            _intentionalClose = true;
            CloseWebSocket();
        }

        /// <summary>
        /// Send a chat message with streaming callbacks.
        /// This is the primary API for ChatBot.cs integration.
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

            var msg = new OutgoingChatMessage
            {
                content = message,
                agent_id = agentId,
                user_id = userId,
                persona_id = personaId,
                session_id = SessionId,
                tts_enabled = ttsEnabled,
                reference_id = referenceId,
                limit = stmLimit
            };
            if (images != null && images.Length > 0)
            {
                msg.images = new ImageContent[images.Length];
                for (int i = 0; i < images.Length; i++)
                    msg.images[i] = new ImageContent
                    {
                        image_url = new ImageUrl { url = $"data:image/png;base64,{images[i]}" }
                    };
            }
            _ = SendAsync(MessageParser.Serialize(msg));
        }

        /// <summary>
        /// Interrupt the current stream.
        /// </summary>
        public void InterruptStream()
        {
            if (!IsConnected) return;
            var msg = new InterruptStreamMessage();
            _ = SendAsync(MessageParser.Serialize(msg));
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
                await _ws.ConnectAsync(new Uri(WsUrl), _cts.Token);
                Debug.Log($"[DMP] Connected to {WsUrl}");
                _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());

                // Authenticate
                var auth = new AuthorizeMessage { token = token };
                await SendAsync(MessageParser.Serialize(auth));

                // Start receive loop
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
                string msgType = obj["type"]?.ToString();

                switch (msgType)
                {
                    case "authorize_success":
                        var authData = MessageParser.ParseAuthorizeSuccess(obj);
                        ConnectionId = authData.connection_id;
                        IsAuthenticated = true;
                        Debug.Log($"[DMP] Authenticated: {ConnectionId}");
                        break;

                    case "authorize_error":
                        var authErr = MessageParser.ParseAuthorizeError(obj);
                        Debug.LogError($"[DMP] Auth failed: {authErr.error}");
                        _intentionalClose = true;
                        break;

                    case "ping":
                        _ = SendAsync(MessageParser.Serialize(new PongMessage()));
                        break;

                    case "stream_start":
                        var startData = MessageParser.ParseStreamStart(obj);
                        _fullResponseText = "";
                        _mainThreadQueue.Enqueue(() => OnStreamStart?.Invoke(startData));
                        break;

                    case "stream_token":
                        var tokenData = MessageParser.ParseStreamToken(obj);
                        _fullResponseText += tokenData.chunk;
                        var currentText = _fullResponseText;
                        _mainThreadQueue.Enqueue(() =>
                        {
                            _activePartialCallback?.Invoke(currentText);
                            OnStreamToken?.Invoke(tokenData);
                        });
                        break;

                    case "tts_chunk":
                        var ttsData = MessageParser.ParseTtsChunk(obj);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            _activeTtsCallback?.Invoke(ttsData);
                            OnTtsChunk?.Invoke(ttsData);
                        });
                        break;

                    case "stream_end":
                        var endData = MessageParser.ParseStreamEnd(obj);
                        if (!string.IsNullOrEmpty(endData.session_id))
                            SessionId = endData.session_id;
                        _mainThreadQueue.Enqueue(() =>
                        {
                            OnStreamEnd?.Invoke(endData);
                            _activeCompletionCallback?.Invoke();
                            _activePartialCallback = null;
                            _activeTtsCallback = null;
                            _activeCompletionCallback = null;
                        });
                        break;

                    case "error":
                        var errData = MessageParser.ParseError(obj);
                        Debug.LogWarning($"[DMP] Server error ({errData.code}): {errData.error}");
                        _mainThreadQueue.Enqueue(() => OnError?.Invoke(errData));
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
