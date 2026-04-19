using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DesktopMatePlus
{
    /// <summary>
    /// Toggles the left session panel and divider on/off via SetActive.
    /// Attach to DmpChatCanvas root. Wire toggleButton.onClick → TogglePanel() in Inspector.
    /// </summary>
    public class SessionPanelToggle : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject sessionPanel;
        [SerializeField] private GameObject divider;
        [SerializeField] private TMP_Text toggleButtonText;

        private bool _expanded = true;

        private const string ExpandedIcon = "\u2261"; // ≡
        private const string CollapsedIcon = "\u25B8"; // ▸

        void Start()
        {
            UpdateIcon();
        }

        /// <summary>Wire to ToggleButton.onClick in Inspector.</summary>
        public void TogglePanel()
        {
            _expanded = !_expanded;
            if (sessionPanel != null) sessionPanel.SetActive(_expanded);
            if (divider != null) divider.SetActive(_expanded);
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            if (toggleButtonText != null)
                toggleButtonText.text = _expanded ? ExpandedIcon : CollapsedIcon;
        }
    }
}
