using System;
using System.Collections;
using System.Collections.Generic;
using Hermes;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DesktopMatePlus
{
    [Serializable]
    public class SessionInfo
    {
        // Hermes exposes id/started_at/last_active; keep the legacy UI-facing
        // names so SessionPanelController can remain a small rewire.
        public string session_id;
        public string model;
        public string created_at;
        public string updated_at;
        public string title;
        public int message_count;
        public string preview;
        public string last_response_id;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string role;
        public string content;
    }

    /// <summary>
    /// REST client for hermes-agent session routes (list / messages / title patch).
    /// Requests use bearer auth from <see cref="HermesResponseClient.ApiKey"/>.
    /// DELETE is intentionally absent: hermes-agent does not register that route
    /// and Phase D removes the Unity frontend delete behavior.
    /// </summary>
    public class SessionApiClient : MonoBehaviour
    {
        public HermesResponseClient hermesClient;

        private string BaseUrl => $"http://{hermesClient.BaseDomain}";

        private void ApplyBearerAuth(UnityWebRequest req)
        {
            if (hermesClient != null && !string.IsNullOrEmpty(hermesClient.ApiKey))
                req.SetRequestHeader("Authorization", $"Bearer {hermesClient.ApiKey}");
        }

        // =================================================================
        // Public API
        // =================================================================

        public void ListSessions(Action<List<SessionInfo>> onSuccess, Action<string> onError = null)
        {
            string url = $"{BaseUrl}/api/sessions";
            StartCoroutine(GetRequest(url, json =>
            {
                try
                {
                    var obj = JObject.Parse(json);
                    var sessions = new List<SessionInfo>();
                    var arr = obj["data"] as JArray ?? obj["sessions"] as JArray;
                    if (arr != null)
                    {
                        foreach (var item in arr)
                        {
                            sessions.Add(new SessionInfo
                            {
                                session_id = item["id"]?.ToString(),
                                model = item["model"]?.ToString(),
                                created_at = ToDisplayTimestamp(item["started_at"]),
                                updated_at = ToDisplayTimestamp(item["last_active"]),
                                title = item["title"]?.ToString(),
                                message_count = item["message_count"]?.Value<int>() ?? 0,
                                preview = item["preview"]?.ToString(),
                                last_response_id = item["last_response_id"]?.ToString(),
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
            string url = $"{BaseUrl}/api/sessions/{Uri.EscapeDataString(sessionId)}/messages";
            StartCoroutine(GetRequest(url, json =>
            {
                try
                {
                    var obj = JObject.Parse(json);
                    var messages = new List<ChatMessageData>();
                    var arr = obj["data"] as JArray ?? obj["messages"] as JArray;
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
                    // Hermes returns messages newest-first and ignores pagination
                    // query params. Keep the most recent tail locally, then reverse
                    // for the existing bottom-up chat rendering path.
                    if (limit > 0 && messages.Count > limit)
                    {
                        messages = messages.GetRange(0, limit);
                    }
                    messages.Reverse();
                    onSuccess?.Invoke(messages);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Parse error: {e.Message}");
                }
            }, onError));
        }

        public void UpdateSessionTitle(string sessionId, string title, Action onSuccess, Action<string> onError = null)
        {
            if (title == null)
            {
                onError?.Invoke("Title must be a string (non-null).");
                return;
            }

            string url = $"{BaseUrl}/api/sessions/{Uri.EscapeDataString(sessionId)}";
            var body = new JObject { ["title"] = title }.ToString(Newtonsoft.Json.Formatting.None);
            StartCoroutine(PatchRequest(url, body, _ => onSuccess?.Invoke(), onError));
        }

        // =================================================================
        // HTTP Helpers
        // =================================================================

        private IEnumerator GetRequest(string url, Action<string> onSuccess, Action<string> onError)
        {
            using var request = UnityWebRequest.Get(url);
            ApplyBearerAuth(request);
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

        private IEnumerator PatchRequest(string url, string jsonBody, Action<string> onSuccess, Action<string> onError)
        {
            using var request = new UnityWebRequest(url, "PATCH");
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(payload) { contentType = "application/json" };
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ApplyBearerAuth(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SessionAPI] PATCH failed: {request.error} url={url}");
                onError?.Invoke(request.error);
            }
            else
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }

        private static string ToDisplayTimestamp(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (double.TryParse(token.ToString(), out double unixSeconds))
                return DateTimeOffset.FromUnixTimeMilliseconds((long)(unixSeconds * 1000)).UtcDateTime.ToString("o");
            return token.ToString();
        }
    }
}
