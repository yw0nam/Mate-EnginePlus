using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DesktopMatePlus
{
    [Serializable]
    public class SessionInfo
    {
        public string session_id;
        public string user_id;
        public string agent_id;
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
    /// REST client for DesktopMatePlus STM (Short-Term Memory) session APIs.
    /// </summary>
    public class SessionApiClient : MonoBehaviour
    {
        public DesktopMatePlusClient dmpClient;

        private string BaseUrl => $"http://{dmpClient.host}:{dmpClient.port}";
        private string UserId => dmpClient.userId;
        private string AgentId => dmpClient.agentId;

        // =================================================================
        // Public API
        // =================================================================

        public void ListSessions(Action<List<SessionInfo>> onSuccess, Action<string> onError = null)
        {
            string url = $"{BaseUrl}/v1/stm/sessions?user_id={Uri.EscapeDataString(UserId)}&agent_id={Uri.EscapeDataString(AgentId)}";
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
                                session_id = item["session_id"]?.ToString(),
                                user_id = item["user_id"]?.ToString(),
                                agent_id = item["agent_id"]?.ToString(),
                                created_at = item["created_at"]?.ToString(),
                                updated_at = item["updated_at"]?.ToString(),
                                title = item["metadata"]?["title"]?.ToString()
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
            string url = $"{BaseUrl}/v1/stm/get-chat-history?session_id={Uri.EscapeDataString(sessionId)}&user_id={Uri.EscapeDataString(UserId)}&agent_id={Uri.EscapeDataString(AgentId)}&limit={limit}";
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
                            // Skip system and tool messages for UI display
                            if (role == "system" || role == "tool") continue;
                            messages.Add(new ChatMessageData
                            {
                                role = role,
                                content = item["content"]?.ToString()
                            });
                        }
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
            string url = $"{BaseUrl}/v1/stm/sessions/{Uri.EscapeDataString(sessionId)}?user_id={Uri.EscapeDataString(UserId)}&agent_id={Uri.EscapeDataString(AgentId)}";
            StartCoroutine(DeleteRequest(url, _ => onSuccess?.Invoke(), onError));
        }

        public void UpdateSessionTitle(string sessionId, string title, Action onSuccess = null, Action<string> onError = null)
        {
            string url = $"{BaseUrl}/v1/stm/sessions/{Uri.EscapeDataString(sessionId)}/metadata";
            var body = new
            {
                session_id = sessionId,
                metadata = new { title }
            };
            string jsonBody = JsonConvert.SerializeObject(body);
            StartCoroutine(PatchRequest(url, jsonBody, _ => onSuccess?.Invoke(), onError));
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

        private IEnumerator DeleteRequest(string url, Action<string> onSuccess, Action<string> onError)
        {
            using var request = UnityWebRequest.Delete(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SessionAPI] DELETE failed: {request.error}");
                onError?.Invoke(request.error);
            }
            else
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }

        private IEnumerator PatchRequest(string url, string jsonBody, Action<string> onSuccess, Action<string> onError)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            using var request = new UnityWebRequest(url, "PATCH");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SessionAPI] PATCH failed: {request.error}");
                onError?.Invoke(request.error);
            }
            else
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }
    }
}
