using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LLMUnitySamples;

namespace DesktopMatePlus
{
    /// <summary>
    /// Session explorer — uses the scene's SessionItem as a template.
    /// Clones the template for each session returned by the API.
    /// </summary>
    public class SessionPanelController : MonoBehaviour
    {
        [Header("References")]
        public SessionApiClient apiClient;
        public DesktopMatePlusClient dmpClient;
        public ChatBot chatBot;
        public GameObject chatMenuPanel;

        [Header("Style")]
        public Color activeSlotColor = new Color32(70, 70, 120, 255);
        public Color inactiveSlotColor = new Color32(32, 44, 58, 255); // matches SessionItem imgColor

        private RectTransform _content;
        private GameObject _template; // first SessionItem in Content — hidden, used as clone source
        private readonly List<SessionInfo> _sessions = new();
        private readonly List<GameObject> _slotObjects = new();
        private string _activeSessionId;

        void OnEnable()
        {
            FindContentAndTemplate();

            if (chatBot != null)
                chatBot.OnNewSessionCreated += OnNewSessionCreated;

            RefreshSessionList();
        }

        void OnDisable()
        {
            if (chatBot != null)
                chatBot.OnNewSessionCreated -= OnNewSessionCreated;
        }

        private void FindContentAndTemplate()
        {
            if (_content != null && _template != null) return;

            var scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (scrollRect == null || scrollRect.content == null) return;
            _content = scrollRect.content;

            // Find first SessionItem child as template
            foreach (Transform child in _content)
            {
                if (child.name.StartsWith("SessionItem"))
                {
                    _template = child.gameObject;
                    break;
                }
            }

            // Hide template & all existing SessionItems
            if (_template != null)
            {
                foreach (Transform child in _content)
                {
                    if (child.name.StartsWith("SessionItem"))
                        child.gameObject.SetActive(false);
                }
            }
        }

        // =================================================================
        // Public
        // =================================================================

        public void RefreshSessionList()
        {
            if (apiClient == null) return;
            apiClient.ListSessions(OnSessionsLoaded, err => Debug.LogWarning($"[SessionPanel] {err}"));
        }

        // =================================================================
        // Data → UI
        // =================================================================

        private void OnSessionsLoaded(List<SessionInfo> sessions)
        {
            _sessions.Clear();
            _sessions.AddRange(sessions);
            _sessions.Sort((a, b) => string.Compare(b.updated_at, a.updated_at, StringComparison.Ordinal));
            RebuildUI();
        }

        private void RebuildUI()
        {
            if (_content == null || _template == null)
            {
                FindContentAndTemplate();
                if (_content == null || _template == null) return;
            }

            // Destroy previously cloned slots
            foreach (var go in _slotObjects)
                if (go != null) Destroy(go);
            _slotObjects.Clear();

            // Auto-select current session
            if (!string.IsNullOrEmpty(dmpClient?.SessionId))
                _activeSessionId = dmpClient.SessionId;
            else if (_sessions.Count > 0)
                _activeSessionId = _sessions[0].session_id;

            // Clone template for each session
            foreach (var session in _sessions)
            {
                var slot = Instantiate(_template, _content);
                slot.SetActive(true);
                slot.name = "SessionSlot";
                PopulateSlot(slot, session);
                _slotObjects.Add(slot);
            }

            HighlightActive();
        }

        private void PopulateSlot(GameObject slot, SessionInfo session)
        {
            string title = !string.IsNullOrEmpty(session.title)
                ? session.title
                : $"Chat {session.session_id[..8]}...";

            // Fill TMP texts by name
            var titleTmp = FindChildTMP(slot.transform, "TitleText");
            if (titleTmp != null) titleTmp.text = title;

            var createdTmp = FindChildTMP(slot.transform, "CreatedText");
            if (createdTmp != null) createdTmp.text = FormatTimestamp(session.created_at);

            var updatedTmp = FindChildTMP(slot.transform, "UpdatedText");
            if (updatedTmp != null) updatedTmp.text = FormatTimestamp(session.updated_at);

            string sid = session.session_id;

            // Slot select button — with hover/press color transition
            var btn = slot.GetComponent<Button>();
            if (btn == null) btn = slot.AddComponent<Button>();
            btn.targetGraphic = slot.GetComponent<Image>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = inactiveSlotColor;
            colors.highlightedColor = new Color32(50, 65, 85, 255);
            colors.pressedColor = new Color32(70, 70, 120, 255);
            colors.selectedColor = inactiveSlotColor;
            colors.fadeDuration = 0.1f;
            btn.colors = colors;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnSelect(sid));

            // Delete button
            var delTransform = slot.transform.Find("SessionDeleteButton");
            if (delTransform != null)
            {
                var delBtn = delTransform.GetComponent<Button>();
                if (delBtn == null) delBtn = delTransform.gameObject.AddComponent<Button>();
                delBtn.targetGraphic = delTransform.GetComponent<Image>();
                delBtn.transition = Selectable.Transition.ColorTint;
                var delColors = delBtn.colors;
                delColors.normalColor = Color.white;
                delColors.highlightedColor = new Color32(255, 100, 100, 255);
                delColors.pressedColor = new Color32(200, 50, 50, 255);
                delColors.fadeDuration = 0.1f;
                delBtn.colors = delColors;
                delBtn.onClick.RemoveAllListeners();
                delBtn.onClick.AddListener(() => OnDelete(sid));
            }
        }

        private static TextMeshProUGUI FindChildTMP(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        // =================================================================
        // Actions
        // =================================================================

        private void OnSelect(string sessionId)
        {
            _activeSessionId = sessionId;
            if (dmpClient != null) dmpClient.SessionId = sessionId;
            HighlightActive();

            // Activate chat panel FIRST (so ChatBot.StartCoroutine works)
            if (chatMenuPanel != null) chatMenuPanel.SetActive(true);
            gameObject.SetActive(false);

            // Then load history
            apiClient?.GetChatHistory(sessionId, 50, messages =>
            {
                chatBot?.LoadHistoryBubbles(messages);
            }, err => Debug.LogWarning($"[SessionPanel] {err}"));
        }

        public void OnNewChatClicked()
        {
            _activeSessionId = null;
            if (dmpClient != null) dmpClient.SessionId = null;

            if (chatMenuPanel != null) chatMenuPanel.SetActive(true);
            gameObject.SetActive(false);

            chatBot?.ClearAllBubbles();
        }

        private void OnDelete(string sessionId)
        {
            apiClient?.DeleteSession(sessionId, () =>
            {
                _sessions.RemoveAll(s => s.session_id == sessionId);
                RebuildUI();
                if (_activeSessionId == sessionId)
                    _activeSessionId = _sessions.Count > 0 ? _sessions[0].session_id : null;
            }, err => Debug.LogWarning($"[SessionPanel] {err}"));
        }

        private void OnNewSessionCreated(string sessionId, string firstMessage)
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
        }

        // =================================================================
        // Highlight
        // =================================================================

        private void HighlightActive()
        {
            for (int i = 0; i < _slotObjects.Count && i < _sessions.Count; i++)
            {
                var img = _slotObjects[i].GetComponent<Image>();
                if (img != null)
                    img.color = _sessions[i].session_id == _activeSessionId ? activeSlotColor : inactiveSlotColor;
            }
        }

        private static string FormatTimestamp(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, out var dt)) return iso;
            var d = DateTime.UtcNow - dt;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
            if (d.TotalDays < 7) return $"{(int)d.TotalDays}d ago";
            return dt.ToString("MM/dd");
        }
    }
}
