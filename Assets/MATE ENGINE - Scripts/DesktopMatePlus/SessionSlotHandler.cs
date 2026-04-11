using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DesktopMatePlus
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

        [Header("Title Editing")]
        [Tooltip("Add a TMP_InputField as sibling/overlay of TitleText. Hidden by default, shown on modify click.")]
        [SerializeField] private TMP_InputField titleInputField;

        [Header("Active Highlight Colors")]
        public Color activeColor = new Color32(70, 70, 120, 255);
        public Color inactiveColor = new Color32(32, 44, 58, 255);

        private SessionPanelController _controller;
        private string _sessionId;
        private string _originalTitle;
        private Button _slotButton;
        private bool _isEditing;

        public string SessionId => _sessionId;

        /// <summary>
        /// Called by SessionPanelController after cloning the template.
        /// Sets up display data and stores controller reference.
        /// </summary>
        public void Initialize(SessionInfo session, SessionPanelController controller)
        {
            _sessionId = session.session_id;
            _controller = controller;

            string title = !string.IsNullOrEmpty(session.title)
                ? session.title
                : $"Chat {_sessionId[..Math.Min(8, _sessionId.Length)]}...";

            if (titleText != null) titleText.text = title;
            if (createdText != null) createdText.text = FormatTimestamp(session.created_at);
            if (updatedText != null) updatedText.text = FormatTimestamp(session.updated_at);

            if (titleInputField != null)
                titleInputField.gameObject.SetActive(false);
            if (titleText != null)
                titleText.gameObject.SetActive(true);

            _slotButton = GetComponent<Button>();
            _isEditing = false;
        }

        /// <summary>
        /// Wire to the SessionItem root Button.onClick in Inspector.
        /// Selects this session and opens chat history.
        /// </summary>
        public void OnSlotClicked()
        {
            Debug.Log($"[SessionSlot] OnSlotClicked: sessionId={_sessionId} controller={(_controller != null ? "OK" : "NULL")} isEditing={_isEditing}");
            if (_isEditing) return;
            _controller?.SelectSession(_sessionId);
        }

        /// <summary>
        /// Wire to SessionDeleteButton.onClick in Inspector.
        /// Deletes this session via API and removes from list.
        /// </summary>
        public void OnDeleteClicked()
        {
            Debug.Log($"[SessionSlot] OnDeleteClicked: sessionId={_sessionId}");
            _controller?.DeleteSession(_sessionId);
        }

        /// <summary>
        /// Wire to SessionTitleModifyButton.onClick in Inspector.
        /// Enters inline title editing mode.
        /// </summary>
        public void OnModifyTitleClicked()
        {
            if (titleInputField == null)
            {
                Debug.LogWarning("[SessionSlot] TitleInputField not assigned — cannot edit title.");
                return;
            }
            EnterEditMode();
        }

        /// <summary>
        /// Wire to TitleInputField.onEndEdit in Inspector.
        /// Saves the edited title (or cancels if unchanged).
        /// </summary>
        public void OnTitleEditFinished(string value)
        {
            if (!_isEditing) return;
            ExitEditMode(value);
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

        private void EnterEditMode()
        {
            _isEditing = true;
            _originalTitle = titleText != null ? titleText.text : "";

            titleText.gameObject.SetActive(false);
            titleInputField.gameObject.SetActive(true);
            // Bring InputField to front so nothing overlaps it
            titleInputField.transform.SetAsLastSibling();
            titleInputField.text = _originalTitle;

            // Disable the root Button while editing so it doesn't intercept clicks/focus
            if (_slotButton != null) _slotButton.interactable = false;

            StartCoroutine(ActivateInputNextFrame());
        }

        private System.Collections.IEnumerator ActivateInputNextFrame()
        {
            yield return null;
            titleInputField.Select();
            titleInputField.ActivateInputField();
        }

        private void ExitEditMode(string newTitle)
        {
            _isEditing = false;

            // Re-enable slot button
            if (_slotButton != null) _slotButton.interactable = true;

            if (titleInputField != null)
                titleInputField.gameObject.SetActive(false);
            if (titleText != null)
                titleText.gameObject.SetActive(true);

            string trimmed = newTitle?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && trimmed != _originalTitle)
            {
                titleText.text = trimmed;
                _controller?.UpdateSessionTitle(_sessionId, trimmed);
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
