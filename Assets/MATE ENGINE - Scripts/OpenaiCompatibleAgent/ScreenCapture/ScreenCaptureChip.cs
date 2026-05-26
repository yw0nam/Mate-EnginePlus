using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// 상태바에 표시되는 "📷 Chrome - YouTube ✕" 칩.
    /// DmpChatController가 Start()에서 OnChipCancelled를 구독한다.
    /// </summary>
    public class ScreenCaptureChip : MonoBehaviour
    {
        [Header("UI")]
        public TMP_Text captureLabel;
        public Button   cancelButton;

        public event Action OnChipCancelled;

        void Awake()
        {
            gameObject.SetActive(false);
            cancelButton?.onClick.AddListener(() => OnChipCancelled?.Invoke());
        }

        public void Show(string sourceName)
        {
            if (captureLabel != null) captureLabel.text = $"📷 {sourceName}";
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
