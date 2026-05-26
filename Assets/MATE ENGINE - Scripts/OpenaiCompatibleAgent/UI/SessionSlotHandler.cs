using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Per-session-slot UI handler. Attach to the SessionItem template.
    /// Child references set in Inspector are preserved when Instantiate clones the template.
    /// Button.onClick events wired in Inspector on the template also survive cloning.
    /// </summary>
    public class SessionSlotHandler : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text createdText;
        [SerializeField] private TMP_Text updatedText;
        [SerializeField, Range(8, 64)] private int maxTitleChars = 24;

        [Header("Active Highlight Colors")]
        public Color activeColor = new Color32(70, 70, 120, 255);
        public Color inactiveColor = new Color32(32, 44, 58, 255);

        private SessionPanelController _controller;
        private string _sessionId;
        private Button _slotButton;

        public string SessionId => _sessionId;

        /// <summary>
        /// Called by SessionPanelController after cloning the template.
        /// Sets up display data and stores controller reference.
        /// </summary>
        public void Initialize(SessionInfo session, SessionPanelController controller)
        {
            _sessionId = session.session_id;
            _controller = controller;

            string fullTitle = !string.IsNullOrEmpty(session.title)
                ? session.title
                : !string.IsNullOrEmpty(session.preview)
                    ? session.preview
                    : $"Session {_sessionId[..Math.Min(8, _sessionId.Length)]}";

            if (titleText != null) titleText.text = TruncateTitle(fullTitle);
            if (createdText != null) createdText.text = FormatTimestamp(session.created_at);
            if (updatedText != null) updatedText.text = FormatTimestamp(session.updated_at);

            _slotButton = GetComponent<Button>();
        }

        /// <summary>
        /// Wire to the SessionItem root Button.onClick in Inspector.
        /// Selects this session and opens chat history.
        /// </summary>
        public void OnSlotClicked()
        {
            Debug.Log($"[SessionSlot] OnSlotClicked: sessionId={_sessionId} controller={(_controller != null ? "OK" : "NULL")}");
            _controller?.SelectSession(_sessionId);
        }

        public void SetHighlight(bool isActive)
        {
            if (_slotButton == null) _slotButton = GetComponent<Button>();
            if (_slotButton == null) return;

            var colors = _slotButton.colors;
            colors.normalColor = isActive ? activeColor : inactiveColor;
            colors.selectedColor = isActive ? activeColor : inactiveColor;
            _slotButton.colors = colors;
        }

        private string TruncateTitle(string title)
        {
            if (title.Length <= maxTitleChars)
                return title;
            return title[..maxTitleChars] + "…";
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
