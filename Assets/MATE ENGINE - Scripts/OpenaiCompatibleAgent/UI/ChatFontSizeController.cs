using UnityEngine;
using TMPro;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// 채팅 폰트 크기(배율)를 조절한다. OpenaiCompatibleCanvas 에 붙이세요.
    ///
    /// - 동적으로 생성되는 채팅 버블은 <see cref="DmpChatMessageItem.CurrentFontScale"/>
    ///   정적 값을 통해 자동으로 현재 배율을 적용받는다.
    /// - 이미 생성된 버블은 <see cref="messageListContent"/> 아래를 순회하며 갱신한다.
    /// - 입력창 텍스트도 함께 스케일한다(선택).
    ///
    /// 조작:
    ///  - A+ / A- / Reset 버튼의 OnClick 에 IncreaseFont / DecreaseFont / ResetFont 연결
    ///  - 키보드: Ctrl + '='(또는 '+') 확대, Ctrl + '-' 축소, Ctrl + '0' 리셋
    /// </summary>
    public class ChatFontSizeController : MonoBehaviour
    {
        [Header("적용 대상")]
        [Tooltip("채팅 버블이 쌓이는 ScrollRect Content (ChatArea/Viewport/Content)")]
        public RectTransform messageListContent;
        [Tooltip("입력창 (선택). 비워두면 입력창 폰트는 건드리지 않음.")]
        public TMP_InputField inputField;
        [Tooltip("현재 배율을 표시할 라벨 (선택). 예: 100%")]
        public TMP_Text scaleLabel;

        [Header("배율 설정")]
        public float minScale = 0.6f;
        public float maxScale = 2.0f;
        public float step = 0.1f;

        [Header("키보드 단축키 (Ctrl + +/-/0)")]
        public bool enableKeyboardShortcuts = true;

        [Header("저장")]
        public bool persist = true;
        public string prefKey = "OAC_ChatFontScale";

        private float _scale = 1f;
        private float _baseInputSize;
        private bool _inputCaptured;

        void Awake()
        {
            _scale = persist ? PlayerPrefs.GetFloat(prefKey, 1f) : 1f;
            _scale = Mathf.Clamp(_scale, minScale, maxScale);
            // 정적 값은 즉시 세팅 → Start 이전에 생성되는 버블도 올바른 배율로.
            DmpChatMessageItem.CurrentFontScale = _scale;
        }

        void Start()
        {
            Apply();
        }

        void Update()
        {
            if (!enableKeyboardShortcuts) return;
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Plus))
                IncreaseFont();
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                DecreaseFont();
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
                ResetFont();
        }

        // ==== Public API (버튼/슬라이더에서 호출) ====

        public void IncreaseFont() => SetScale(_scale + step);
        public void DecreaseFont() => SetScale(_scale - step);
        public void ResetFont() => SetScale(1f);

        /// <summary>슬라이더 onValueChanged 에 직접 연결 가능.</summary>
        public void SetScale(float scale)
        {
            _scale = Mathf.Clamp(scale, minScale, maxScale);
            if (persist)
            {
                PlayerPrefs.SetFloat(prefKey, _scale);
                PlayerPrefs.Save();
            }
            Apply();
        }

        public float CurrentScale => _scale;

        private void Apply()
        {
            DmpChatMessageItem.CurrentFontScale = _scale;

            if (messageListContent != null)
            {
                var items = messageListContent.GetComponentsInChildren<DmpChatMessageItem>(true);
                foreach (var item in items)
                    if (item != null) item.ApplyFontScale(_scale);
            }

            if (inputField != null && inputField.textComponent != null)
            {
                if (!_inputCaptured)
                {
                    _baseInputSize = inputField.textComponent.fontSize;
                    _inputCaptured = true;
                }
                inputField.textComponent.enableAutoSizing = false;
                inputField.textComponent.fontSize = _baseInputSize * _scale;
                inputField.pointSize = _baseInputSize * _scale;
            }

            if (scaleLabel != null)
                scaleLabel.text = Mathf.RoundToInt(_scale * 100f) + "%";
        }
    }
}
