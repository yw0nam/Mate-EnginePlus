using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenaiCompatibleAgent
{
    public class DmpChatMessageItem : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text chatText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private GameObject speakerButtonObj;

        [Header("Bubble background (optional — wired in Inspector)")]
        [SerializeField] private Image bubbleBackground;
        [SerializeField] private Color aiBubbleColor = new Color(0.110f, 0.161f, 0.224f, 1f);
        [SerializeField] private Color userBubbleColor = new Color(0.086f, 0.122f, 0.188f, 1f);

        [Header("Typewriter")]
        [Tooltip("Chars revealed per second while streaming. Set <= 0 to disable and render instantly.")]
        [SerializeField] private float charsPerSecond = 40f;
        [Tooltip("If backlog exceeds this many chars, reveal speed scales up linearly. Set <= 0 to disable.")]
        [SerializeField] private int catchupThreshold = 80;

        // Fires whenever revealed text grows. Controller hooks this to keep scroll pinned.
        public event Action OnTextRevealed;

        private string _targetText = "";
        private float _revealedChars;
        private bool _typewriterStarted;
        private Coroutine _typeCo;

        // Called by DmpChatController after Instantiate — renders instantly (history, user msgs, placeholder).
        public void Initialize(string content, bool isAI, Sprite avatar, string senderName, string timestamp)
        {
            if (avatarImage != null) avatarImage.sprite = avatar;
            if (nameText != null) nameText.text = senderName;
            if (timeText != null) timeText.text = timestamp;
            if (speakerButtonObj != null) speakerButtonObj.SetActive(isAI);
            if (bubbleBackground != null) bubbleBackground.color = isAI ? aiBubbleColor : userBubbleColor;

            StopTypewriter();
            _targetText = content ?? "";
            _revealedChars = _targetText.Length;
            _typewriterStarted = false;
            if (chatText != null) chatText.text = _targetText;
        }

        // Streaming partial from backend. Updates target text; typewriter catches up over time.
        public void SetChatText(string partial)
        {
            if (chatText == null) return;
            _targetText = partial ?? "";

            // First streaming call after Initialize — discard placeholder and reveal from 0.
            if (!_typewriterStarted)
            {
                _typewriterStarted = true;
                _revealedChars = 0f;
                chatText.text = "";
            }

            if (charsPerSecond <= 0f)
            {
                StopTypewriter();
                _revealedChars = _targetText.Length;
                chatText.text = _targetText;
                OnTextRevealed?.Invoke();
                return;
            }

            if (_typeCo == null && _revealedChars < _targetText.Length)
                _typeCo = StartCoroutine(TypewriterLoop());
        }

        private IEnumerator TypewriterLoop()
        {
            while (_revealedChars < _targetText.Length)
            {
                int backlog = _targetText.Length - Mathf.FloorToInt(_revealedChars);
                float speedMult = (catchupThreshold > 0 && backlog > catchupThreshold)
                    ? 1f + (backlog - catchupThreshold) / (float)catchupThreshold
                    : 1f;

                _revealedChars += charsPerSecond * speedMult * Time.unscaledDeltaTime;
                int visible = Mathf.Min(Mathf.FloorToInt(_revealedChars), _targetText.Length);

                if (chatText.text.Length != visible)
                {
                    chatText.text = _targetText.Substring(0, visible);
                    OnTextRevealed?.Invoke();
                }
                yield return null;
            }
            _typeCo = null;
        }

        private void StopTypewriter()
        {
            if (_typeCo != null)
            {
                StopCoroutine(_typeCo);
                _typeCo = null;
            }
        }

        public string GetChatText()
        {
            return chatText != null ? chatText.text : "";
        }

        public void SetAvatar(Sprite sprite)
        {
            if (avatarImage != null) avatarImage.sprite = sprite;
        }
    }
}
