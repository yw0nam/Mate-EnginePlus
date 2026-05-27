using System.Collections.Generic;
using OpenaiCompatibleAgent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MateEngine.SettingsEditor
{
    /// <summary>
    /// Editor utility that adds Hermes / OpenAI-compatible settings rows under the
    /// existing "= AI" section of SettingsMenuCanvas/Main Menu by cloning the row
    /// widgets already present there (legacy InputField, Slider, Toggle).
    ///
    /// Run from the menu (Tools → Mate Engine → Add Hermes Settings Rows) or via
    ///   unity-cli exec "MateEngine.SettingsEditor.AddHermesSettingsRows.Run();"
    ///
    /// Idempotent: skips rows that already exist by name.
    /// </summary>
    public static class AddHermesSettingsRows
    {
        const string MenuPath = "Tools/Mate Engine/Add Hermes Settings Rows";

        struct InputRow { public string name; public string label; }
        struct SliderRow { public string name; public string label; public float min; public float max; public bool whole; }
        struct ToggleRow { public string name; public string label; }

        static readonly InputRow[] InputRows = new InputRow[] {
            new InputRow{ name="HermesHost",     label="Hermes Host" },
            new InputRow{ name="HermesPort",     label="Hermes Port" },
            new InputRow{ name="HermesApiKey",   label="Hermes API Key" },
            new InputRow{ name="HermesModelId",  label="Hermes Model" },
            new InputRow{ name="IrodoriBaseUrl", label="Irodori URL" },
            new InputRow{ name="VoicesRootPath", label="Voices Root" },
            new InputRow{ name="ChatAiName",     label="AI Name" },
            new InputRow{ name="ChatUserName",   label="User Name" },
        };

        static readonly SliderRow[] SliderRows = new SliderRow[] {
            new SliderRow{ name="SentenceMinChunkLength", label="Min Chunk Length", min=10, max=200, whole=true },
            new SliderRow{ name="TtsBarrierTimeoutSeconds", label="TTS Timeout (s)", min=5, max=120, whole=false },
            new SliderRow{ name="ChatMaxMessages", label="Max Messages", min=10, max=500, whole=true },
        };

        static readonly ToggleRow[] ToggleRows = new ToggleRow[] {
            new ToggleRow{ name="ChatAutoScroll", label="Auto Scroll" },
        };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var canvasGo = FindByName("SettingsMenuCanvas");
            if (canvasGo == null) { Debug.LogError("[AddHermesSettingsRows] SettingsMenuCanvas not found."); return; }

            Transform innerMain = null;
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Main Menu" && t.parent != null && t.parent.name == "MenuPanel") { innerMain = t; break; }
            }
            if (innerMain == null) { Debug.LogError("[AddHermesSettingsRows] inner Main Menu not found."); return; }

            // Use the existing "= AI" section so the new rows feel natural.
            Transform aiSection = innerMain.Find("= AI");
            if (aiSection == null) { Debug.LogError("[AddHermesSettingsRows] '= AI' section not found."); return; }

            // Templates already in the AI section.
            Transform inputTemplate = aiSection.Find("AI INPUT/AiSystemPrompt");
            if (inputTemplate == null)
            {
                // Older layouts: AiSystemPrompt may sit directly under = AI
                foreach (Transform t in aiSection.GetComponentsInChildren<Transform>(true))
                    if (t.name == "AiSystemPrompt") { inputTemplate = t; break; }
            }
            if (inputTemplate == null) { Debug.LogError("[AddHermesSettingsRows] InputField template AiSystemPrompt not found."); return; }

            Transform inputParent = inputTemplate.parent;

            // For sliders/toggles, borrow templates from other sections.
            Transform sliderTemplate = FindRowTemplate(innerMain, "FPSLimit");
            Transform toggleTemplate = FindRowTemplate(innerMain, "Discord RPC");
            if (sliderTemplate == null) { Debug.LogError("[AddHermesSettingsRows] Slider template (FPSLimit) not found."); return; }
            if (toggleTemplate == null) { Debug.LogError("[AddHermesSettingsRows] Toggle template (Discord RPC) not found."); return; }

            int created = 0;

            foreach (var row in InputRows)
            {
                if (inputParent.Find(row.name) != null) continue;
                var clone = CloneAs(inputTemplate.gameObject, inputParent, row.name);
                SetFirstTmpText(clone, row.label);
                created++;
            }

            // Sliders/toggles get added directly under = AI so they cluster with related fields.
            foreach (var row in SliderRows)
            {
                if (aiSection.Find(row.name) != null) continue;
                var clone = CloneAs(sliderTemplate.gameObject, aiSection, row.name);
                var slider = clone.GetComponent<Slider>();
                if (slider != null)
                {
                    slider.wholeNumbers = row.whole;
                    slider.minValue = row.min;
                    slider.maxValue = row.max;
                }
                SetFirstTmpText(clone, row.label);
                created++;
            }

            foreach (var row in ToggleRows)
            {
                if (aiSection.Find(row.name) != null) continue;
                var clone = CloneAs(toggleTemplate.gameObject, aiSection, row.name);
                SetFirstTmpText(clone, row.label);
                created++;
            }

            // Reinit button — borrow the "Close App" button as a template since it's a
            // simple button with a TMP_Text child.
            if (aiSection.Find("HermesReinitialize") == null)
            {
                Transform btnTemplate = null;
                foreach (Transform t in innerMain.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Close App") { btnTemplate = t; break; }
                if (btnTemplate != null)
                {
                    var clone = CloneAs(btnTemplate.gameObject, aiSection, "HermesReinitialize");
                    SetFirstTmpText(clone, "Reinit Hermes");
                    created++;
                }
            }

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Debug.Log($"[AddHermesSettingsRows] Created {created} row(s) under '= AI'. Open SettingsMenuCanvas in the Editor to wire them to the new SettingsHandlerHermes component on the Settings GameObject.");
            Selection.activeGameObject = aiSection.gameObject;
            EditorGUIUtility.PingObject(aiSection.gameObject);
        }

        static GameObject CloneAs(GameObject template, Transform parent, string newName)
        {
            var clone = Object.Instantiate(template, parent);
            clone.name = newName;
            clone.SetActive(true);
            Undo.RegisterCreatedObjectUndo(clone, "Add Hermes Settings Row");
            return clone;
        }

        static Transform FindRowTemplate(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static void SetFirstTmpText(GameObject root, string text)
        {
            // Prefer a child named "Text" / "Title" / "Label" so we don't overwrite
            // numeric value labels embedded in sliders (e.g. FPSLimit's "FPS Number").
            var labels = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            TMPro.TMP_Text best = null;
            foreach (var l in labels)
            {
                if (l == null) continue;
                if (l.name == "Text" || l.name == "Title" || l.name == "Label" || l.name == "TitleText")
                { best = l; break; }
            }
            if (best == null && labels.Length > 0) best = labels[0];
            if (best != null) best.text = text;
        }

        static GameObject FindByName(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid() && go.name == name) return go;
            return null;
        }

        // ----- Wiring -----

        const string WireMenuPath = "Tools/Mate Engine/Wire Hermes Settings";

        /// <summary>
        /// Adds (or finds) the SettingsHandlerHermes component on the "Settings"
        /// GameObject and wires its serialized references to the rows created by
        /// <see cref="Run"/>. Idempotent.
        /// </summary>
        [MenuItem(WireMenuPath)]
        public static void WireSettings()
        {
            var settingsGo = FindByName("Settings");
            if (settingsGo == null) { Debug.LogError("[Wire] 'Settings' GameObject not found."); return; }

            var handler = settingsGo.GetComponent<SettingsHandlerHermes>();
            if (handler == null)
            {
                handler = Undo.AddComponent<SettingsHandlerHermes>(settingsGo);
                Debug.Log("[Wire] Added SettingsHandlerHermes to Settings GameObject.");
            }

            var hermesGo = FindByName("Hermes");
            var canvasGo = FindByName("OpenaiCompatibleCanvas");
            if (hermesGo == null) { Debug.LogError("[Wire] 'Hermes' GameObject not found."); return; }
            if (canvasGo == null) { Debug.LogError("[Wire] 'OpenaiCompatibleCanvas' GameObject not found."); return; }

            // Locate the cloned rows under = AI.
            var settingsCanvas = FindByName("SettingsMenuCanvas");
            Transform aiSection = null;
            if (settingsCanvas != null)
            {
                foreach (var t in settingsCanvas.GetComponentsInChildren<Transform>(true))
                    if (t.name == "= AI") { aiSection = t; break; }
            }
            if (aiSection == null) { Debug.LogError("[Wire] '= AI' section not found."); return; }

            T FindInAi<T>(string name) where T : Component
            {
                foreach (var t in aiSection.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.GetComponent<T>();
                return null;
            }

            GameObject FindGoInAi(string name)
            {
                foreach (var t in aiSection.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
                return null;
            }

            // (LocalizeStringEvent on cloned widgets is now destroyed inside
            // MoveToHermesSection's per-row loop; no-op here for legacy callers.)

            // ---- SerializedObject wiring ----
            var so = new SerializedObject(handler);

            void SetRef(string propName, Object target)
            {
                var p = so.FindProperty(propName);
                if (p == null) { Debug.LogWarning($"[Wire] property '{propName}' not found on handler."); return; }
                p.objectReferenceValue = target;
            }

            // Runtime targets
            SetRef("hermesClient",          hermesGo.GetComponent<HermesResponseClient>());
            SetRef("irodoriClient",         hermesGo.GetComponent<IrodoriClient>());
            SetRef("streamingOrchestrator", hermesGo.GetComponent<StreamingOrchestrator>());
            SetRef("chatController",        canvasGo.GetComponent<DmpChatController>());

            // Hermes connection inputs
            SetRef("hermesHostInput",    FindInAi<InputField>("HermesHost"));
            SetRef("hermesPortInput",    FindInAi<InputField>("HermesPort"));
            SetRef("hermesApiKeyInput",  FindInAi<InputField>("HermesApiKey"));
            SetRef("hermesModelIdInput", FindInAi<InputField>("HermesModelId"));
            SetRef("hermesReinitializeButton", FindButtonUnder(FindGoInAi("HermesReinitialize")));

            // Irodori
            SetRef("irodoriBaseUrlInput", FindInAi<InputField>("IrodoriBaseUrl"));
            SetRef("voicesRootPathInput", FindInAi<InputField>("VoicesRootPath"));

            // Streaming sliders + their numeric value labels.
            // FPSLimit template carries a child called "FPS Number" that displays the value;
            // we re-use it as our *Label slot. SetFirstTmpText() previously wrote to the
            // localized "Text" sibling (the title), so this slot stays untouched.
            SetRef("sentenceMinChunkLengthSlider", FindInAi<Slider>("SentenceMinChunkLength"));
            SetRef("sentenceMinChunkLengthLabel", FindValueLabel("SentenceMinChunkLength", aiSection));
            SetRef("ttsBarrierTimeoutSlider", FindInAi<Slider>("TtsBarrierTimeoutSeconds"));
            SetRef("ttsBarrierTimeoutLabel",  FindValueLabel("TtsBarrierTimeoutSeconds", aiSection));

            // Chat
            SetRef("chatMaxMessagesSlider", FindInAi<Slider>("ChatMaxMessages"));
            SetRef("chatMaxMessagesLabel",  FindValueLabel("ChatMaxMessages", aiSection));
            SetRef("chatAutoScrollToggle",  FindInAi<Toggle>("ChatAutoScroll"));
            SetRef("chatAiNameInput",       FindInAi<InputField>("ChatAiName"));
            SetRef("chatUserNameInput",     FindInAi<InputField>("ChatUserName"));

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(handler);
            EditorSceneManager.MarkSceneDirty(settingsGo.scene);

            Debug.Log("[Wire] SettingsHandlerHermes references wired. Save the scene to persist.");
            Selection.activeGameObject = settingsGo;
            EditorGUIUtility.PingObject(settingsGo);
        }

        static Button FindButtonUnder(GameObject go)
        {
            if (go == null) return null;
            return go.GetComponentInChildren<Button>(true);
        }

        // ----- Move to dedicated section -----

        const string MoveMenuPath = "Tools/Mate Engine/Move Hermes Rows To New Section";

        const string HermesSectionName = "= HERMES";

        /// <summary>
        /// Moves the cloned Hermes-settings rows out of the original "= AI" section
        /// (which is dominated by the oversized AiSystemPrompt) into a fresh section
        /// "= HERMES" appended after the last existing section. Extends the
        /// scroll Content's bottom padding so the new rows are reachable.
        /// Idempotent.
        /// </summary>
        [MenuItem(MoveMenuPath)]
        public static void MoveToHermesSection()
        {
            var canvasGo = FindByName("SettingsMenuCanvas");
            if (canvasGo == null) { Debug.LogError("[Move] SettingsMenuCanvas not found."); return; }

            // Resolve the structural chain.
            var outerMain = canvasGo.transform.Find("Main Menu");
            var content   = outerMain != null ? outerMain.Find("Viewport/Content") : null;
            var menuPanel = content != null ? content.Find("MenuPanel") : null;
            var innerMain = menuPanel != null ? menuPanel.Find("Main Menu") : null;
            if (innerMain == null) { Debug.LogError("[Move] inner Main Menu not found."); return; }

            // 1. Resolve or create the = HERMES header.
            Transform hermesSection = innerMain.Find(HermesSectionName);
            if (hermesSection == null)
            {
                // Clone an existing section header. Pick = MINECRAFT — it has fewer
                // children we'd need to strip. (Section headers in this menu have
                // their widget rows attached as children; we want a header-only clone.)
                Transform headerTemplate = innerMain.Find("= MINECRAFT");
                if (headerTemplate == null)
                {
                    foreach (Transform t in innerMain)
                        if (t.name.StartsWith("= ") && t.GetComponent<TMPro.TMP_Text>() != null) { headerTemplate = t; break; }
                }
                if (headerTemplate == null) { Debug.LogError("[Move] no section header template found."); return; }

                var headerClone = Object.Instantiate(headerTemplate.gameObject, innerMain);
                headerClone.name = HermesSectionName;
                Undo.RegisterCreatedObjectUndo(headerClone, "Create = HERMES section");
                hermesSection = headerClone.transform;

                // Strip ALL children in one step — we only want the header TMP_Text,
                // not the template section's widget rows.
                var detachedRoots = new List<Transform>();
                for (int i = hermesSection.childCount - 1; i >= 0; i--)
                    detachedRoots.Add(hermesSection.GetChild(i));
                hermesSection.DetachChildren();
                foreach (var root in detachedRoots)
                    Undo.DestroyObjectImmediate(root.gameObject);

                // Destroy LocalizeStringEvent outright — these new rows are not
                // localized yet and disable can be flipped back by Inspector/scripts.
                foreach (var ev in headerClone.GetComponents<UnityEngine.Localization.Components.LocalizeStringEvent>())
                    Undo.DestroyObjectImmediate(ev);

                var tmp = headerClone.GetComponent<TMPro.TMP_Text>();
                if (tmp != null)
                {
                    Undo.RecordObject(tmp, "Set section text");
                    tmp.text = "= HERMES";
                }

                // Normalize scale (clones from animated panels may inherit weird scale).
                Undo.RecordObject(hermesSection, "Normalize header scale");
                hermesSection.localScale = Vector3.one;
            }

            // 2. Position the header just below the last existing section header.
            //    Find lowest-y header (excluding our new one).
            float lowestHeaderY = float.MaxValue;
            foreach (Transform t in innerMain)
            {
                if (t == hermesSection) continue;
                if (!t.name.StartsWith("= ")) continue;
                var rt = t as RectTransform;
                if (rt == null) continue;
                lowestHeaderY = Mathf.Min(lowestHeaderY, rt.anchoredPosition.y);
            }

            const float HEADER_GAP   = 260f; // matches the looser inter-section gap.
            const float WIDGET_GAP   = 50f;  // first widget below new header.
            const float ROW_STEP     = -36f;

            var hermesRt = (RectTransform)hermesSection;
            Undo.RecordObject(hermesRt, "Position = HERMES header");
            hermesRt.anchorMin = new Vector2(0.5f, 0.5f);
            hermesRt.anchorMax = new Vector2(0.5f, 0.5f);
            hermesRt.pivot     = new Vector2(0.5f, 0.5f);
            hermesRt.anchoredPosition = new Vector2(0, lowestHeaderY - HEADER_GAP);
            hermesRt.sizeDelta = new Vector2(400, 40);

            float headerY = hermesRt.anchoredPosition.y;
            Debug.Log($"[Move] = HERMES header at y={headerY:0}");

            // 3. Reparent each Hermes-settings row into innerMain (the same parent as
            //    section headers). Sections are sibling labels; their "content" widgets
            //    are also direct siblings of inner Main Menu in the existing menu (sections
            //    are not containers, just labels). So our widgets become siblings under
            //    inner Main Menu, positioned just below the = HERMES header.
            float widgetStartY = headerY - WIDGET_GAP;
            int idx = 0;
            foreach (var name in LayoutOrder)
            {
                Transform row = null;
                foreach (var t in innerMain.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) { row = t; break; }
                if (row == null) continue;

                Undo.SetTransformParent(row, innerMain, "Reparent row to innerMain");

                var rt = (RectTransform)row;
                Undo.RecordObject(rt, "Position Hermes row");
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);

                Vector2 size;
                if (row.GetComponent<Slider>() != null)  size = new Vector2(500, 20);
                else if (row.GetComponent<Toggle>() != null) size = new Vector2(160, 20);
                else if (row.GetComponent<InputField>() != null) size = new Vector2(500, 28);
                else size = new Vector2(200, 36); // HermesReinitialize wrapper

                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(0, widgetStartY + idx * ROW_STEP);
                rt.localScale = Vector3.one; // clones from animated templates can inherit zero/4.3x scale

                // Destroy ALL LocalizeStringEvent on the row + its children. These
                // rows have no localization entries yet and disabled events can be
                // re-enabled by inspector / scripts.
                foreach (var ev in row.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
                    Undo.DestroyObjectImmediate(ev);

                // InputField single-line normalization + placeholder hint cleanup.
                var inp = row.GetComponent<InputField>();
                if (inp != null)
                {
                    Undo.RecordObject(inp, "Normalize InputField");
                    inp.lineType = InputField.LineType.SingleLine;
                    inp.contentType = name == "HermesPort" ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;
                    inp.characterLimit = 256;
                    if (inp.placeholder is Text ph)
                    {
                        Undo.RecordObject(ph, "Normalize placeholder");
                        ph.text = InputPlaceholders.TryGetValue(name, out var hint) ? hint : "";
                        ph.alignment = TextAnchor.MiddleLeft;
                        ph.horizontalOverflow = HorizontalWrapMode.Overflow;
                        ph.verticalOverflow = VerticalWrapMode.Truncate;
                    }
                    if (inp.textComponent != null)
                    {
                        Undo.RecordObject(inp.textComponent, "Normalize input text");
                        inp.textComponent.alignment = TextAnchor.MiddleLeft;
                        inp.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                        inp.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
                    }
                }

                EditorUtility.SetDirty(rt);
                idx++;
            }

            float lastY = widgetStartY + (idx - 1) * ROW_STEP;
            Debug.Log($"[Move] Positioned {idx} widget(s). Last widget at y={lastY:0}");

            // 4. Make sure the scroll Content is tall enough for the new section.
            //    Original menu used VLG.padding.bottom=4600 as a "phantom" height
            //    (children had no LayoutElement → VLG.preferred=0 → CSF kept the
            //    manually-set sizeDelta). We switch to the proper Unity pattern:
            //    add a LayoutElement to MenuPanel with preferredHeight = total
            //    section content depth, and reset padding.bottom so VLG/CSF
            //    compute Content.height = padding.top + preferredHeight only.
            var vlg = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null && menuPanel is RectTransform menuPanelRt)
            {
                var le = menuPanel.GetComponent<UnityEngine.UI.LayoutElement>();
                if (le == null) le = Undo.AddComponent<UnityEngine.UI.LayoutElement>(menuPanel.gameObject);

                float neededPreferred = Mathf.Abs(lastY) + 200f; // 200 px safety below last row
                bool changed = false;
                if (le.preferredHeight < neededPreferred)
                {
                    Undo.RecordObject(le, "Grow MenuPanel preferredHeight");
                    le.preferredHeight = neededPreferred;
                    EditorUtility.SetDirty(le);
                    changed = true;
                }
                if (vlg.padding.bottom != 0)
                {
                    Undo.RecordObject(vlg, "Zero VLG padding.bottom");
                    vlg.padding = new RectOffset(vlg.padding.left, vlg.padding.right, vlg.padding.top, 0);
                    EditorUtility.SetDirty(vlg);
                    changed = true;
                }
                if (changed)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
                    Debug.Log($"[Move] MenuPanel.preferredHeight={le.preferredHeight:0}, padding.bottom=0, Content.height now {((RectTransform)content).rect.height:0}");
                }
            }

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = hermesSection.gameObject;
            EditorGUIUtility.PingObject(hermesSection.gameObject);
        }

        // ----- Layout (legacy, kept for reference) -----

        const string LayoutMenuPath = "Tools/Mate Engine/Layout Hermes Settings";

        // Order of widgets (top → bottom) inside the = AI section, directly below the
        // existing AI INPUT container.
        static readonly string[] LayoutOrder = new string[] {
            "HermesHost", "HermesPort", "HermesApiKey", "HermesModelId",
            "IrodoriBaseUrl", "VoicesRootPath",
            "ChatAiName", "ChatUserName",
            "SentenceMinChunkLength", "TtsBarrierTimeoutSeconds", "ChatMaxMessages",
            "ChatAutoScroll",
            "HermesReinitialize",
        };

        // Single-line placeholder hints per InputField row.
        static readonly Dictionary<string, string> InputPlaceholders = new Dictionary<string, string> {
            { "HermesHost",     "localhost" },
            { "HermesPort",     "8642" },
            { "HermesApiKey",   "hermes_api_key" },
            { "HermesModelId",  "hermes-agent" },
            { "IrodoriBaseUrl", "http://localhost:8091" },
            { "VoicesRootPath", "D:\\codes\\waifu\\references_voices" },
            { "ChatAiName",     "AI" },
            { "ChatUserName",   "User" },
        };

        /// <summary>
        /// Cascades the cloned Hermes-settings rows vertically below AI INPUT so they
        /// no longer overlap. Idempotent — safe to re-run. Sizes are clamped to the
        /// row-style of the existing FPSLimit/Discord RPC rows so the menu stays
        /// visually consistent.
        /// </summary>
        [MenuItem(LayoutMenuPath)]
        public static void LayoutWidgets()
        {
            var canvasGo = FindByName("SettingsMenuCanvas");
            if (canvasGo == null) { Debug.LogError("[Layout] SettingsMenuCanvas not found."); return; }

            Transform aiSection = null, aiInput = null;
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "= AI" && aiSection == null) aiSection = t;
                if (t.name == "AI INPUT" && aiInput == null) aiInput = t;
            }
            if (aiSection == null) { Debug.LogError("[Layout] '= AI' section not found."); return; }

            // Step 1: reparent any rows that ended up inside AI INPUT (the cloned
            // InputFields) up to the = AI section so everything is one flat column.
            foreach (var name in LayoutOrder)
            {
                Transform row = aiSection.Find(name);
                if (row != null) continue; // already a direct child
                // search deeper
                foreach (var t in aiSection.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name) { row = t; break; }
                }
                if (row == null) continue;
                Undo.SetTransformParent(row, aiSection, "Reparent settings row");
            }

            // Step 2: cascade positions starting just below the actual rendered
            // bottom of AiSystemPrompt (which is much taller than its AI INPUT
            // parent's sizeDelta). We compute that bottom in world space, convert
            // to = AI's local coordinates, and add a small gap.
            float startY = -400f;
            Transform asp = null;
            foreach (var t in aiSection.GetComponentsInChildren<Transform>(true))
                if (t.name == "AiSystemPrompt") { asp = t; break; }
            if (asp != null)
            {
                var aspCorners = new Vector3[4];
                ((RectTransform)asp).GetWorldCorners(aspCorners);
                // Corners: 0 BL, 1 TL, 2 TR, 3 BR.
                // Convert the bottom-left corner (BL) into = AI's local space so we keep
                // X consistent and Unity handles scale/rotation correctly.
                Vector3 localBL = aiSection.InverseTransformPoint(aspCorners[0]);
                startY = localBL.y - 20f;
                Debug.Log($"[Layout] AiSystemPrompt worldBottomY={aspCorners[0].y:0.000} -> aiSection localY={localBL.y:0.0} startY={startY:0.0}");
            }

            const float STEP = -32f;
            const float CENTER_X = 0f;

            Vector2 inputSize  = new Vector2(500, 28);
            Vector2 sliderSize = new Vector2(500, 20);
            Vector2 toggleSize = new Vector2(160, 20);
            Vector2 buttonSize = new Vector2(200, 36);

            int idx = 0;
            foreach (var name in LayoutOrder)
            {
                var row = aiSection.Find(name);
                if (row == null) continue;
                var rt = (RectTransform)row;

                // Force center-anchor sandbox so anchoredPosition is the visible center.
                Undo.RecordObject(rt, "Layout Hermes Row");
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);

                Vector2 size;
                if (row.GetComponent<Slider>() != null) size = sliderSize;
                else if (row.GetComponent<Toggle>() != null) size = toggleSize;
                else if (row.GetComponent<InputField>() != null) size = inputSize;
                else size = buttonSize; // HermesReinitialize wrapper

                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2(CENTER_X, startY + idx * STEP);
                EditorUtility.SetDirty(rt);

                // Single-line normalize for InputFields cloned from the multi-line
                // AiSystemPrompt template: force SingleLine + clear the inherited
                // 60-line system-prompt placeholder + truncate overflow.
                var inp = row.GetComponent<InputField>();
                if (inp != null)
                {
                    Undo.RecordObject(inp, "Normalize InputField");
                    inp.lineType = InputField.LineType.SingleLine;
                    inp.contentType = name == "HermesPort" ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;
                    inp.characterLimit = 256;
                    var ph = inp.placeholder as Text;
                    if (ph != null)
                    {
                        Undo.RecordObject(ph, "Normalize placeholder");
                        ph.text = InputPlaceholders.TryGetValue(name, out var hint) ? hint : "";
                        ph.alignment = TextAnchor.MiddleLeft;
                        ph.horizontalOverflow = HorizontalWrapMode.Overflow;
                        ph.verticalOverflow = VerticalWrapMode.Truncate;
                        EditorUtility.SetDirty(ph);
                    }
                    var txt = inp.textComponent;
                    if (txt != null)
                    {
                        Undo.RecordObject(txt, "Normalize input text");
                        txt.alignment = TextAnchor.MiddleLeft;
                        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                        txt.verticalOverflow = VerticalWrapMode.Truncate;
                        EditorUtility.SetDirty(txt);
                    }
                    EditorUtility.SetDirty(inp);
                }
                idx++;
            }

            // Step 3: also ensure the section's child order matches LayoutOrder so
            // tab/focus order is predictable.
            int siblingIndex = aiSection.childCount - 1;
            for (int i = LayoutOrder.Length - 1; i >= 0; i--)
            {
                var row = aiSection.Find(LayoutOrder[i]);
                if (row == null) continue;
                row.SetSiblingIndex(siblingIndex--);
            }

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Debug.Log($"[Layout] Repositioned {idx} row(s) under '= AI'. Save the scene to persist.");
            Selection.activeGameObject = aiSection.gameObject;
        }

        // Picks the "FPS Number"-style numeric value label cloned from the FPSLimit template.
        // Falls back to any TMP_Text that is NOT the localized title.
        static TMPro.TMP_Text FindValueLabel(string rowName, Transform aiSection)
        {
            Transform row = null;
            foreach (var t in aiSection.GetComponentsInChildren<Transform>(true))
                if (t.name == rowName) { row = t; break; }
            if (row == null) return null;

            TMPro.TMP_Text fallback = null;
            foreach (var label in row.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (label == null) continue;
                if (label.name == "FPS Number") return label;
                if (label.name != "Text" && label.name != "Title" && label.name != "Label" && label.name != "TitleText")
                    fallback = label;
            }
            return fallback;
        }
    }
}
