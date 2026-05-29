using OpenaiCompatibleAgent;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MateEngine.SettingsEditor
{
    /// <summary>
    /// OpenaiCompatibleCanvas 채팅 창에 (1) 우측 하단 리사이즈 그립과
    /// (2) 폰트 크기 컨트롤(A-/A+/리셋 + 배율 라벨)을 생성/연결한다.
    ///
    /// 실행: Tools → Mate Engine → Setup Chat Window Controls
    ///   또는 unity-cli exec "MateEngine.SettingsEditor.ChatWindowControlsSetup.Run();"
    ///
    /// 멱등(idempotent): 이미 만든 오브젝트는 건너뛰고 참조만 다시 연결한다.
    /// </summary>
    public static class ChatWindowControlsSetup
    {
        const string MenuPath = "Tools/Mate Engine/Setup Chat Window Controls";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var canvas = FindByName("OpenaiCompatibleCanvas");
            if (canvas == null) { Debug.LogError("[ChatCtrls] OpenaiCompatibleCanvas not found."); return; }

            var outer      = FindUnder(canvas.transform, "OuterMenuChat");
            var rightPanel = FindUnder(canvas.transform, "RightPanel");
            var leftPanel  = FindUnder(canvas.transform, "LeftPanel");
            var bottomBar  = FindUnder(canvas.transform, "BottomBar");
            var chatArea   = FindUnder(canvas.transform, "ChatArea");
            var newChatBtn = FindUnder(canvas.transform, "NewChatButton");
            var connStatus = FindUnder(canvas.transform, "ConnectionStatus");

            if (outer == null || rightPanel == null || leftPanel == null)
            { Debug.LogError("[ChatCtrls] OuterMenuChat/RightPanel/LeftPanel not found."); return; }

            // messageListContent = ChatArea/Viewport/Content
            RectTransform content = null;
            if (chatArea != null)
            {
                var vp = chatArea.transform.Find("Viewport");
                if (vp != null) content = vp.Find("Content") as RectTransform;
            }
            // inputField
            var inputFieldGo = FindUnder(canvas.transform, "InputField");
            var inputField = inputFieldGo != null ? inputFieldGo.GetComponent<TMP_InputField>() : null;

            // ---------- 1) Resize grip ----------
            var grip = SetupResizeGrip(outer, rightPanel, leftPanel);

            // ---------- 2) Font controller ----------
            var fontCtrl = canvas.GetComponent<ChatFontSizeController>();
            if (fontCtrl == null) fontCtrl = Undo.AddComponent<ChatFontSizeController>(canvas);
            {
                var so = new SerializedObject(fontCtrl);
                so.FindProperty("messageListContent").objectReferenceValue = content;
                so.FindProperty("inputField").objectReferenceValue = inputField;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---------- 3) Font buttons (A- / 라벨 / A+) ----------
            if (bottomBar != null && newChatBtn != null)
                SetupFontButtons(bottomBar, newChatBtn.gameObject, connStatus, fontCtrl);

            EditorSceneManager.MarkSceneDirty(canvas.scene);
            EditorUtility.SetDirty(canvas);
            Debug.Log("[ChatCtrls] Done. ResizeGrip=" + (grip != null) +
                      " FontController wired (content=" + (content != null) + ", input=" + (inputField != null) + "). Save the scene to persist.");
            Selection.activeGameObject = canvas;
            EditorGUIUtility.PingObject(canvas);
        }

        // ---- Resize grip ----
        static GameObject SetupResizeGrip(GameObject outer, GameObject rightPanel, GameObject leftPanel)
        {
            var outerRt = outer.GetComponent<RectTransform>();
            Transform existing = outer.transform.Find("ResizeGrip");
            GameObject grip = existing != null ? existing.gameObject : null;
            if (grip == null)
            {
                grip = new GameObject("ResizeGrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(ResizablePanel));
                Undo.RegisterCreatedObjectUndo(grip, "Create ResizeGrip");
                grip.transform.SetParent(outer.transform, false);
            }

            var rt = grip.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(28f, 28f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;

            var img = grip.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.28f); // 은은한 코너 표시
            img.raycastTarget = true;

            // HorizontalLayoutGroup(OuterMenuChat) 가 그립을 레이아웃에 끼우지 않도록 무시.
            var le = grip.GetComponent<LayoutElement>();
            le.ignoreLayout = true;

            var resize = grip.GetComponent<ResizablePanel>();
            var rso = new SerializedObject(resize);
            rso.FindProperty("window").objectReferenceValue = outerRt;
            var arr = rso.FindProperty("widthElements");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = rightPanel.GetComponent<LayoutElement>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = leftPanel.GetComponent<LayoutElement>();
            rso.ApplyModifiedPropertiesWithoutUndo();

            return grip;
        }

        // ---- Font buttons ----
        static void SetupFontButtons(GameObject bottomBar, GameObject btnTemplate, GameObject connStatusTemplate, ChatFontSizeController fontCtrl)
        {
            var bar = bottomBar.transform.Find("FontControlBar");
            if (bar == null)
            {
                var barGo = new GameObject("FontControlBar", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                Undo.RegisterCreatedObjectUndo(barGo, "Create FontControlBar");
                barGo.transform.SetParent(bottomBar.transform, false);
                barGo.transform.SetAsFirstSibling();
                bar = barGo.transform;

                var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleRight;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
                hlg.spacing = 6f;
                hlg.padding = new RectOffset(0, 4, 0, 0);

                var ble = barGo.GetComponent<LayoutElement>();
                ble.preferredHeight = 30f;
                ble.flexibleWidth = 1f;
            }

            // BottomBar 가 추가 행을 담을 수 있도록 높이를 늘린다.
            var bbLe = bottomBar.GetComponent<LayoutElement>();
            if (bbLe != null && bbLe.preferredHeight < 150f)
                bbLe.preferredHeight = 150f;

            // 라벨 + 버튼 구성: [A-] [100%] [A+]
            var label = MakeOrGetLabel(bar, connStatusTemplate, fontCtrl);
            var minus = MakeOrGetButton(bar, btnTemplate, "FontMinusButton", "A-");
            var plus  = MakeOrGetButton(bar, btnTemplate, "FontPlusButton", "A+");

            // 순서: A-  100%  A+
            minus.transform.SetSiblingIndex(0);
            if (label != null) label.transform.SetSiblingIndex(1);
            plus.transform.SetSiblingIndex(2);

            // 컨트롤러에 라벨 연결
            if (label != null)
            {
                var so = new SerializedObject(fontCtrl);
                so.FindProperty("scaleLabel").objectReferenceValue = label.GetComponent<TMP_Text>();
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // onClick 연결 (멱등: 기존 persistent 리스너 정리 후 재연결)
            WireClick(minus.GetComponent<Button>(), fontCtrl, "DecreaseFont");
            WireClick(plus.GetComponent<Button>(), fontCtrl, "IncreaseFont");
        }

        static GameObject MakeOrGetButton(Transform parent, GameObject template, string name, string text)
        {
            var existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : Object.Instantiate(template, parent);
            if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.name = name;
            go.SetActive(true);
            go.transform.localScale = Vector3.one;

            // 로컬라이즈 이벤트 제거 (라벨 직접 지정)
            foreach (var ev in go.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
                Object.DestroyImmediate(ev);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(40f, 28f);
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 40f;
            le.preferredHeight = 28f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = text;
                tmp.enableAutoSizing = false;
                tmp.fontSize = 22f;
                tmp.alignment = TextAlignmentOptions.Center;
            }
            return go;
        }

        static GameObject MakeOrGetLabel(Transform parent, GameObject template, ChatFontSizeController fontCtrl)
        {
            var existing = parent.Find("FontScaleLabel");
            if (existing != null) return existing.gameObject;
            if (template == null) return null;

            var go = Object.Instantiate(template, parent);
            Undo.RegisterCreatedObjectUndo(go, "Create FontScaleLabel");
            go.name = "FontScaleLabel";
            go.SetActive(true);
            go.transform.localScale = Vector3.one;

            foreach (var ev in go.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
                Object.DestroyImmediate(ev);

            // ConnectionStatus 의 LayoutElement(가로 568) 가 따라오므로 작게 재설정.
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 56f;
            le.preferredHeight = 28f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = "100%";
                tmp.enableAutoSizing = false;
                tmp.fontSize = 18f;
                tmp.alignment = TextAlignmentOptions.Center;
            }
            return go;
        }

        static void WireClick(Button btn, ChatFontSizeController target, string method)
        {
            if (btn == null) return;
            // 기존 persistent 리스너 모두 제거 후 재등록 (멱등)
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(btn.onClick, i);

            UnityAction action = method == "IncreaseFont" ? target.IncreaseFont
                               : method == "DecreaseFont" ? target.DecreaseFont
                               : (UnityAction)target.ResetFont;
            UnityEventTools.AddPersistentListener(btn.onClick, action);
        }

        // ---- helpers ----
        static GameObject FindByName(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid() && go.name == name) return go;
            return null;
        }

        static GameObject FindUnder(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
