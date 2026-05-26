using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// 이 스크립트를 드래그 핸들(헤더/타이틀바) GameObject에 붙이세요.
    /// targetPanel에 실제로 이동시킬 RectTransform(DmpChatCanvas 등)을 연결하세요.
    /// </summary>
    public class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [Header("드래그로 이동할 패널")]
        public RectTransform targetPanel;

        [Header("화면 밖으로 나가지 못하게 제한")]
        public bool clampToScreen = true;

        private Vector2 _dragOffset;
        private Canvas _canvas;

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (targetPanel == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetPanel.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint
            );
            _dragOffset = targetPanel.anchoredPosition - localPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (targetPanel == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetPanel.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint
            );

            Vector2 newPos = localPoint + _dragOffset;

            if (clampToScreen)
                newPos = ClampToParent(newPos);

            targetPanel.anchoredPosition = newPos;
        }

        private Vector2 ClampToParent(Vector2 pos)
        {
            var parent = targetPanel.parent as RectTransform;
            if (parent == null) return pos;

            Vector2 parentSize = parent.rect.size;
            Vector2 panelSize = targetPanel.rect.size;
            Vector2 pivot = targetPanel.pivot;

            float minX = -parentSize.x * 0.5f + panelSize.x * pivot.x;
            float maxX =  parentSize.x * 0.5f - panelSize.x * (1f - pivot.x);
            float minY = -parentSize.y * 0.5f + panelSize.y * pivot.y;
            float maxY =  parentSize.y * 0.5f - panelSize.y * (1f - pivot.y);

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            return pos;
        }
    }
}
