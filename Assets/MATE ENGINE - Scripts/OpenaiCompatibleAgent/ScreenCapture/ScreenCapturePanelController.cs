using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// 📷 팝업 패널. 모니터/창 탭 전환 + 항목 선택 + 확인/취소.
    /// screenshotButton.onClick → Open() 을 Inspector에서 연결한다.
    /// </summary>
    public class ScreenCapturePanelController : MonoBehaviour
    {
        [Header("Tabs")]
        public Button     tabMonitorBtn;
        public Button     tabWindowBtn;
        public GameObject monitorListPanel;
        public GameObject windowListPanel;

        [Header("Lists")]
        public Transform  monitorListContent;
        public Transform  windowListContent;
        public GameObject monitorItemTemplate;  // 번호 + 해상도 표시용
        public GameObject windowItemTemplate;   // 창 제목 표시용

        [Header("Buttons")]
        public Button confirmBtn;
        public Button cancelBtn;

        /// <summary>확인 버튼 클릭 시 선택된 ScreenCaptureSource 전달.</summary>
        public event Action<ScreenCaptureSource> OnSourceSelected;

        ScreenCaptureSource _selected;

        void Awake()
        {
            gameObject.SetActive(false);
            tabMonitorBtn?.onClick.AddListener(ShowMonitorTab);
            tabWindowBtn?.onClick.AddListener(ShowWindowTab);
            confirmBtn?.onClick.AddListener(OnConfirm);
            cancelBtn?.onClick.AddListener(Close);

            monitorItemTemplate?.SetActive(false);
            windowItemTemplate?.SetActive(false);
        }

        public void Open()
        {
            _selected = null;
            BuildMonitorList();
            BuildWindowList();
            ShowMonitorTab();
            gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        // ── 탭 전환 ───────────────────────────────────────────────────

        void ShowMonitorTab()
        {
            monitorListPanel?.SetActive(true);
            windowListPanel?.SetActive(false);
        }

        void ShowWindowTab()
        {
            monitorListPanel?.SetActive(false);
            windowListPanel?.SetActive(true);
        }

        // ── 목록 빌드 ─────────────────────────────────────────────────

        void BuildMonitorList()
        {
            ClearList(monitorListContent, monitorItemTemplate);
            var monitors = ScreenCaptureManager.EnumerateMonitors();
            foreach (var src in monitors)
                AddItem(monitorListContent, monitorItemTemplate, src);
        }

        void BuildWindowList()
        {
            ClearList(windowListContent, windowItemTemplate);
            var windows = ScreenCaptureManager.EnumerateWindows();
            foreach (var src in windows)
                AddItem(windowListContent, windowItemTemplate, src);
        }

        void ClearList(Transform content, GameObject template)
        {
            if (content == null) return;
            foreach (Transform child in content)
                if (child.gameObject != template)
                    Destroy(child.gameObject);
        }

        void AddItem(Transform content, GameObject template, ScreenCaptureSource src)
        {
            if (template == null || content == null) return;
            var go = Instantiate(template, content);
            go.SetActive(true);

            // 라벨 텍스트 설정
            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = src.DisplayName;

            // 클릭 시 선택 처리
            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => SelectItem(go, src, content));
        }

        void SelectItem(GameObject go, ScreenCaptureSource src, Transform content)
        {
            _selected = src;

            // 같은 목록 내 모든 항목 하이라이트 해제 후 선택 항목만 강조
            // 색상은 DESIGN.md 의 interactive.active / surface.input 토큰을 따른다.
            foreach (Transform child in content)
            {
                var img = child.GetComponent<Image>();
                if (img != null)
                    img.color = child.gameObject == go
                        ? new Color(0.351f, 0.393f, 0.519f, 1f)  // interactive.active
                        : new Color(0.088f, 0.123f, 0.189f, 1f); // surface.input
            }
        }

        void OnConfirm()
        {
            if (_selected == null) return;
            OnSourceSelected?.Invoke(_selected);
            Close();
        }
    }
}
