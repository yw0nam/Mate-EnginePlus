using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopMatePlus
{
    /// <summary>
    /// Session explorer — manages session list, delegates per-slot UI to SessionSlotHandler.
    /// </summary>
    public class SessionPanelController : MonoBehaviour
    {
        [Header("References")]
        public SessionApiClient apiClient;
        public DesktopMatePlusClient dmpClient;

        [Header("Prefabs")]
        [SerializeField] private GameObject sessionItemPrefab;

        // Events for DmpChatController integration
        public event Action<List<ChatMessageData>> OnHistoryLoaded;
        public event Action OnChatCleared;

        private RectTransform _content;
        private readonly List<SessionInfo> _sessions = new();
        private readonly List<GameObject> _slotObjects = new();
        private string _activeSessionId;

        void OnEnable()
        {
            FindContent();
            RefreshSessionList();
        }

        void OnDisable()
        {
        }

        private void FindContent()
        {
            if (_content != null) return;

            var scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (scrollRect == null || scrollRect.content == null) return;
            _content = scrollRect.content;
        }

        public void RefreshSessionList()
        {
            if (apiClient == null) return;
            apiClient.ListSessions(OnSessionsLoaded, err => Debug.LogWarning($"[SessionPanel] {err}"));
        }

        /// <summary>Called by SessionSlotHandler when a slot is selected.</summary>
        public void SelectSession(string sessionId)
        {
            _activeSessionId = sessionId;
            if (dmpClient != null) dmpClient.SessionId = sessionId;
            HighlightActive();

            apiClient?.GetChatHistory(sessionId, 50, messages =>
            {
                OnHistoryLoaded?.Invoke(messages);
            }, err => Debug.LogWarning($"[SessionPanel] History load error: {err}"));
        }

        /// <summary>Called by SessionSlotHandler when delete is clicked.</summary>
        public void DeleteSession(string sessionId)
        {
            apiClient?.DeleteSession(sessionId, () =>
            {
                _sessions.RemoveAll(s => s.session_id == sessionId);
                RebuildUI();
                if (_activeSessionId == sessionId)
                    _activeSessionId = _sessions.Count > 0 ? _sessions[0].session_id : null;
            }, err => Debug.LogWarning($"[SessionPanel] Delete error: {err}"));
        }

        /// <summary>Called by SessionSlotHandler when title is edited.</summary>
        public void UpdateSessionTitle(string sessionId, string newTitle)
        {
            apiClient?.UpdateSessionTitle(sessionId, newTitle);
            var session = _sessions.Find(s => s.session_id == sessionId);
            if (session != null) session.title = newTitle;
        }

        /// <summary>Wire Footer "New Chat" button to this in Inspector.</summary>
        public void OnNewChatClicked()
        {
            _activeSessionId = null;
            if (dmpClient != null) dmpClient.SessionId = null;
            OnChatCleared?.Invoke();
        }

        /// <summary>Public alias for DmpChatController to trigger refresh.</summary>
        public void RefreshList() => RefreshSessionList();

        private void OnSessionsLoaded(List<SessionInfo> sessions)
        {
            _sessions.Clear();
            _sessions.AddRange(sessions);
            _sessions.Sort((a, b) => string.Compare(b.updated_at, a.updated_at, StringComparison.Ordinal));
            RebuildUI();
        }

        private void RebuildUI()
        {
            if (_content == null || sessionItemPrefab == null)
            {
                FindContent();
                if (_content == null || sessionItemPrefab == null) return;
            }

            foreach (var go in _slotObjects)
                if (go != null) Destroy(go);
            _slotObjects.Clear();

            if (!string.IsNullOrEmpty(dmpClient?.SessionId))
                _activeSessionId = dmpClient.SessionId;
            else if (_sessions.Count > 0)
                _activeSessionId = _sessions[0].session_id;

            foreach (var session in _sessions)
            {
                var slot = Instantiate(sessionItemPrefab, _content);
                slot.SetActive(true);
                slot.name = "SessionSlot";

                var handler = slot.GetComponent<SessionSlotHandler>();
                if (handler != null)
                {
                    handler.Initialize(session, this);
                }
                else
                {
                    Debug.LogWarning("[SessionPanel] SessionSlotHandler missing on prefab. Attach it to SessionItem prefab.");
                }

                _slotObjects.Add(slot);
            }

            HighlightActive();
        }

        private void HighlightActive()
        {
            for (int i = 0; i < _slotObjects.Count && i < _sessions.Count; i++)
            {
                var handler = _slotObjects[i].GetComponent<SessionSlotHandler>();
                if (handler != null)
                    handler.SetHighlight(_sessions[i].session_id == _activeSessionId);
            }
        }

        /// <summary>Called by DmpChatController when a new session is created.</summary>
        public void AddNewSession(string sessionId, string firstMessage)
        {
            _sessions.Insert(0, new SessionInfo
            {
                session_id = sessionId,
                user_id = dmpClient?.userId,
                agent_id = dmpClient?.agentId,
                created_at = DateTime.UtcNow.ToString("o"),
                updated_at = DateTime.UtcNow.ToString("o"),
                title = firstMessage?.Length > 20 ? firstMessage[..20] + "..." : firstMessage
            });
            _activeSessionId = sessionId;
            apiClient?.UpdateSessionTitle(sessionId, _sessions[0].title);
            RebuildUI();
        }
    }
}
