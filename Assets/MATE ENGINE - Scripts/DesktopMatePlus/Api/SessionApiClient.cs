using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DesktopMatePlus
{
    [Serializable]
    public class SessionInfo
    {
        // Full nanobot session key ("desktop_mate:<chat_id>"). Treat as opaque
        // on the UI side; strip the "desktop_mate:" prefix only when handing
        // a chat_id to the WebSocket layer.
        public string session_id;
        public string created_at;
        public string updated_at;
        public string title;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string role;
        public string content;
    }

    /// <summary>
    /// REST client for the DesktopMate channel's session routes (list / messages / delete).
    /// Endpoints mirror nanobot's WebSocketChannel surface and share the static
    /// ?token= used for the WebSocket handshake. The FE's title-edit flow was
    /// removed — nanobot doesn't expose a PATCH metadata endpoint and the UX was
    /// deferred.
    /// </summary>
    public class SessionApiClient : MonoBehaviour
    {
        public DesktopMatePlusClient dmpClient;

        private string BaseUrl => $"http://{dmpClient.host}:{dmpClient.port}";
        private string Token => dmpClient.token;

        private string WithToken(string pathWithQuery)
        {
            string sep = pathWithQuery.Contains("?") ? "&" : "?";
            return $"{pathWithQuery}{sep}token={Uri.EscapeDataString(Token ?? string.Empty)}";
        }

        // =================================================================
        // Public API
        // =================================================================

        public void ListSessions(Action<List<SessionInfo>> onSuccess, Action<string> onError = null)
        {
            string url = WithToken($"{BaseUrl}/api/sessions");
            StartCoroutine(GetRequest(url, json =>
            {
                try
                {
                    var obj = JObject.Parse(json);
                    var sessions = new List<SessionInfo>();
                    var arr = obj["sessions"] as JArray;
                    if (arr != null)
                    {
                        foreach (var item in arr)
                        {
                            sessions.Add(new SessionInfo
                            {
                                session_id = item["key"]?.ToString(),
                                created_at = item["created_at"]?.ToString(),
                                updated_at = item["updated_at"]?.ToString(),
                                // Server doesn't persist titles; FE infers a display
                                // label from the first user message instead.
                                title = null,
                            });
                        }
                    }
                    onSuccess?.Invoke(sessions);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Parse error: {e.Message}");
                }
            }, onError));
        }

        public void GetChatHistory(string sessionId, int limit, Action<List<ChatMessageData>> onSuccess, Action<string> onError = null)
        {
            string url = WithToken($"{BaseUrl}/api/sessions/{Uri.EscapeDataString(sessionId)}/messages");
            StartCoroutine(GetRequest(url, json =>
            {
                try
                {
                    var obj = JObject.Parse(json);
                    var messages = new List<ChatMessageData>();
                    var arr = obj["messages"] as JArray;
                    if (arr != null)
                    {
                        foreach (var item in arr)
                        {
                            string role = item["role"]?.ToString();
                            if (role == "system" || role == "tool") continue;
                            messages.Add(new ChatMessageData
                            {
                                role = role,
                                content = item["content"]?.ToString()
                            });
                        }
                    }
                    // Nanobot's /messages returns the full session. Clip to the
                    // caller's requested tail locally so the sidebar/UI stays
                    // bounded on long sessions.
                    if (limit > 0 && messages.Count > limit)
                    {
                        messages = messages.GetRange(messages.Count - limit, limit);
                    }
                    onSuccess?.Invoke(messages);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Parse error: {e.Message}");
                }
            }, onError));
        }

        public void DeleteSession(string sessionId, Action onSuccess, Action<string> onError = null)
        {
            // Note: nanobot's WebSocketChannel embeds delete as a GET because the
            // websockets library's HTTP parser only accepts GET. We mirror that.
            string url = WithToken($"{BaseUrl}/api/sessions/{Uri.EscapeDataString(sessionId)}/delete");
            StartCoroutine(GetRequest(url, _ => onSuccess?.Invoke(), onError));
        }

        // =================================================================
        // HTTP Helpers
        // =================================================================

        private IEnumerator GetRequest(string url, Action<string> onSuccess, Action<string> onError)
        {
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SessionAPI] GET failed: {request.error} url={url}");
                onError?.Invoke(request.error);
            }
            else
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }
    }
}
