using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DesktopMatePlus
{
    public class DmpChatMessageItem : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text chatText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private GameObject speakerButtonObj;

        // Called by DmpChatController after Instantiate
        public void Initialize(string content, bool isAI, Sprite avatar, string senderName, string timestamp)
        {
            if (avatarImage != null) avatarImage.sprite = avatar;
            if (nameText != null) nameText.text = senderName;
            if (chatText != null) chatText.text = content;
            if (timeText != null) timeText.text = timestamp;
            if (speakerButtonObj != null) speakerButtonObj.SetActive(isAI);
        }

        // For streaming AI responses — update text incrementally
        public void SetChatText(string partial)
        {
            if (chatText != null) chatText.text = partial;
        }

        public string GetChatText()
        {
            return chatText != null ? chatText.text : "";
        }
    }
}
