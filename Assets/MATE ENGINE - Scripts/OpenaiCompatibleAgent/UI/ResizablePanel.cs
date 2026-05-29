using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// 이 스크립트를 리사이즈 그립(보통 창의 우측 하단 코너) GameObject에 붙이세요.
    /// 마우스로 드래그하면 <see cref="window"/> 크기를 조절합니다.
    ///
    /// OpenaiCompatibleCanvas/OuterMenuChat 구조 전제:
    ///  - 너비: OuterMenuChat 은 ContentSizeFitter(horizontal=PreferredSize) 라서
    ///    sizeDelta.x 를 직접 바꿀 수 없다. 자식 패널(LeftPanel/RightPanel)의
    ///    LayoutElement.preferredWidth 합으로 결정되므로 <see cref="widthElements"/> 를
    ///    통해 너비를 조절한다.
    ///  - 높이: CSF vertical=Unconstrained 라서 window.sizeDelta.y 를 직접 설정한다.
    ///
    /// 좌측 상단 코너를 고정한 채(=우측 하단으로 자라남) 리사이즈되도록 pivot/rect
    /// 변화량을 보정한다.
    /// </summary>
    public class ResizablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [Header("크기를 조절할 창 (OuterMenuChat)")]
        public RectTransform window;

        [Header("너비는 이 LayoutElement 들의 preferredWidth 로 조절됨 (예: RightPanel, LeftPanel)")]
        public LayoutElement[] widthElements;

        [Header("조절 축")]
        public bool resizeWidth = true;
        public bool resizeHeight = true;

        [Header("창 크기 제한 (px)")]
        public Vector2 minSize = new Vector2(500f, 320f);
        public Vector2 maxSize = new Vector2(1600f, 1000f);

        [Header("각 패널의 최소 preferredWidth (px)")]
        public float minElementWidth = 120f;

        [Header("크기 저장 (PlayerPrefs)")]
        public bool persist = true;
        public string prefKey = "OAC_ChatWindowSize";

        private RectTransform _parent;
        private Vector2 _startPointer;
        private float _startWidth;
        private float _startHeight;
        private float[] _startPrefW;

        void Awake()
        {
            if (window != null) _parent = window.parent as RectTransform;
        }

        void Start()
        {
            if (persist) LoadSize();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (window == null) return;
            _parent = window.parent as RectTransform;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent, e.position, e.pressEventCamera, out _startPointer);

            _startWidth = window.rect.width;
            _startHeight = window.rect.height;

            if (widthElements != null)
            {
                _startPrefW = new float[widthElements.Length];
                for (int i = 0; i < widthElements.Length; i++)
                    _startPrefW[i] = widthElements[i] != null ? widthElements[i].preferredWidth : 0f;
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (window == null || _parent == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent, e.position, e.pressEventCamera, out Vector2 cur);
            Vector2 delta = cur - _startPointer;

            ApplySize(_startWidth + delta.x, _startHeight - delta.y);
        }

        /// <summary>
        /// 목표 창 너비/높이를 적용하고 좌상단을 고정하도록 위치를 보정한다.
        /// </summary>
        public void ApplySize(float targetWidth, float targetHeight)
        {
            if (window == null) return;

            float oldW = window.rect.width;
            float oldH = window.rect.height;

            if (resizeWidth && widthElements != null && widthElements.Length > 0 && _startPrefW != null)
            {
                float targetTotal = Mathf.Clamp(targetWidth, minSize.x, maxSize.x);
                float applyDelta = targetTotal - _startWidth;
                float per = applyDelta / widthElements.Length;
                for (int i = 0; i < widthElements.Length; i++)
                {
                    if (widthElements[i] == null) continue;
                    widthElements[i].preferredWidth = Mathf.Max(minElementWidth, _startPrefW[i] + per);
                }
            }

            if (resizeHeight)
            {
                float targetH = Mathf.Clamp(targetHeight, minSize.y, maxSize.y);
                window.sizeDelta = new Vector2(window.sizeDelta.x, targetH);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(window);

            // 좌상단 고정 보정: rect 변화량을 pivot 기준으로 위치에 반영한다.
            float dW = window.rect.width - oldW;
            float dH = window.rect.height - oldH;
            Vector2 pivot = window.pivot;
            Vector2 pos = window.anchoredPosition;
            pos.x += dW * pivot.x;
            pos.y -= dH * (1f - pivot.y);
            window.anchoredPosition = pos;

            if (persist) SaveSize();
        }

        private void SaveSize()
        {
            // window 너비는 CSF 가 결정하므로 rect.width 를 저장한다.
            PlayerPrefs.SetFloat(prefKey + "_W", window.rect.width);
            PlayerPrefs.SetFloat(prefKey + "_H", window.rect.height);
        }

        private void LoadSize()
        {
            if (!PlayerPrefs.HasKey(prefKey + "_W")) return;
            float w = PlayerPrefs.GetFloat(prefKey + "_W");
            float h = PlayerPrefs.GetFloat(prefKey + "_H");

            // 저장값 적용을 위해 시작 기준을 현재 값으로 세팅한 뒤 ApplySize 호출.
            _startWidth = window.rect.width;
            _startHeight = window.rect.height;
            if (widthElements != null)
            {
                _startPrefW = new float[widthElements.Length];
                for (int i = 0; i < widthElements.Length; i++)
                    _startPrefW[i] = widthElements[i] != null ? widthElements[i].preferredWidth : 0f;
            }
            ApplySize(w, h);
        }
    }
}
