# Refactor Plan: `Hermes/` + `DesktopMatePlus/` → `OpenaiCompatibleAgent/`

**Date authored:** 2026-05-26
**Status:** Plan only — not yet executed
**Scope:** Folder + namespace consolidation (no behavior change, no API rename)

---

## 1. Goal

Merge the two co-located chat-agent modules into a single, coherent module.

- **`Assets/MATE ENGINE - Scripts/Hermes/`** (7 files, ns `Hermes`)
- **`Assets/MATE ENGINE - Scripts/DesktopMatePlus/`** (15 files, ns `DesktopMatePlus`)

→ **`Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/`** (22 files, ns `OpenaiCompatibleAgent`)

### Why this is safe

- Dependency is **strictly unidirectional**: `DesktopMatePlus → Hermes`. No cycles.
- Only **4 DMP files** import `Hermes` namespace (will become same-namespace).
- Only **3 test files** + **1 scene** + **1 code comment** reference these from outside.
- Unity scenes/prefabs reference scripts by **GUID**, not path or namespace — `.cs`+`.meta` co-move preserves all scene wiring.

### Why NOT decompose further

The user explicitly asked for "DMP and Hermes should be one." Pulling ScreenCapture / generic Audio into separate modules is a different refactor — out of scope here per Karpathy §3 (surgical changes).

---

## 2. Success Criteria (Karpathy §4)

A reviewer can verify success by running:

1. ✅ `unity-cli editor refresh --compile` → completes with no errors
2. ✅ `unity-cli console --type error` → empty
3. ✅ `unity-cli test` (EditMode) → all green; specifically `PreprocessorTests`, `TtsRequestQueueTests` pass
4. ✅ `unity-cli test --mode PlayMode` → all green; `HermesSmokeRunner` finds `OpenaiCompatibleAgent.TtsAudioPlayer` at runtime
5. ✅ Open `Mate Engine Main.unity` in Editor → no "Missing script" warnings on any GameObject
6. ✅ Play mode smoke: send a chat message, confirm streaming + TTS + emotion crossfade still work
7. ✅ `grep -rn "namespace Hermes\b\|namespace DesktopMatePlus\b" Assets/` → 0 results
8. ✅ `grep -rn "using Hermes;\|using DesktopMatePlus;\|using Hermes\.\|using DesktopMatePlus\." Assets/` → 0 results

---

## 3. Target Folder Layout

```
Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/
├── OpenaiCompatibleAgent.asmdef        # was Hermes.asmdef
├── Backend/                            # was Hermes/
│   ├── HermesResponseClient.cs         # class name kept
│   ├── StreamingOrchestrator.cs
│   ├── SentenceChunker.cs
│   ├── Preprocessor.cs
│   ├── IrodoriClient.cs
│   ├── FastBunkaiSidecarClient.cs
│   └── TtsRequestQueue.cs
├── Api/
│   └── SessionApiClient.cs
├── Chat/
│   └── DmpChatController.cs            # class name kept
├── Expression/
│   ├── TtsAudioPlayer.cs
│   ├── EmotionCrossfader.cs
│   └── AmplitudeLipSync.cs
├── UI/
│   ├── SessionPanelController.cs
│   ├── SessionSlotHandler.cs
│   ├── SessionPanelToggle.cs
│   ├── DraggablePanel.cs
│   ├── DmpChatMessageItem.cs
│   └── VoiceCatalogHandler.cs
└── ScreenCapture/
    ├── ScreenCaptureChip.cs
    ├── ScreenCaptureManager.cs
    ├── ScreenCapturePanelController.cs
    └── ScreenCaptureSource.cs
```

**Class names are not changed.** Keeping `HermesResponseClient`, `DmpChatController`, etc. minimizes the blast radius and keeps `git blame` history readable. Class-rename can be a follow-up if desired.

---

## 4. Execution Phases

Each phase ends with a **verification gate**. Do not start the next phase until the gate is green.

### Phase 0 — Pre-flight (read-only)

- [ ] Take a snapshot: `git status` clean on `Mate-Engine/`
- [ ] Confirm Unity Editor is closed (or in safe state) before bulk file moves — otherwise Unity's importer may race the move
- [ ] Record current test pass-count: `unity-cli test` → save the count for comparison

**Gate:** Clean tree, baseline test count captured.

---

### Phase 1 — Create container folder

- [ ] `mkdir Assets/MATE\ ENGINE\ -\ Scripts/OpenaiCompatibleAgent`
- [ ] Let Unity auto-generate the `.meta` for the new folder (open Editor briefly), OR copy a sibling folder's `.meta` template and assign a fresh GUID

**Gate:** Folder visible in Unity Project pane with valid `.meta`.

---

### Phase 2 — Move files (no edits to content yet)

For each file under `Hermes/` and `DesktopMatePlus/`:

- [ ] `git mv` (or filesystem move) the `.cs` **AND its `.cs.meta`** together to the target subfolder
- [ ] Same for the `Hermes.asmdef` and `Hermes.asmdef.meta` → rename to `OpenaiCompatibleAgent.asmdef` + meta (keep GUID inside meta unchanged)

**Files to move (22 `.cs` + 22 `.meta` + 1 asmdef + 1 asmdef meta):**

| From | To |
|---|---|
| `Hermes/*.cs` (7 files) | `OpenaiCompatibleAgent/Backend/` |
| `Hermes/Hermes.asmdef` | `OpenaiCompatibleAgent/OpenaiCompatibleAgent.asmdef` (rename) |
| `DesktopMatePlus/Api/*` | `OpenaiCompatibleAgent/Api/` |
| `DesktopMatePlus/Chat/*` | `OpenaiCompatibleAgent/Chat/` |
| `DesktopMatePlus/Expression/*` | `OpenaiCompatibleAgent/Expression/` |
| `DesktopMatePlus/UI/*` | `OpenaiCompatibleAgent/UI/` |
| `DesktopMatePlus/ScreenCapture/*` | `OpenaiCompatibleAgent/ScreenCapture/` |

- [ ] Delete the now-empty `Hermes/` and `DesktopMatePlus/` folders (and their `.meta` files)

**Gate:**
- `unity-cli editor refresh --compile` → compile errors expected (namespaces still `Hermes` / `DesktopMatePlus`, but asmdef name changed). This is expected. The point of this gate is to confirm **scene wiring is preserved** — open `Mate Engine Main.unity`, no missing-script warnings.
- If missing-script warnings appear: STOP. The `.meta` files were not moved together. Revert.

---

### Phase 3 — Rename asmdef + update test asmdef reference

Edit `OpenaiCompatibleAgent/OpenaiCompatibleAgent.asmdef`:
```diff
- "name": "Hermes",
- "rootNamespace": "Hermes",
+ "name": "OpenaiCompatibleAgent",
+ "rootNamespace": "OpenaiCompatibleAgent",
```

Edit `Tests/Editor/Hermes.Editor.Tests.asmdef`:
```diff
- "references": [ "Hermes" ]
+ "references": [ "OpenaiCompatibleAgent" ]
```
*(Optional: rename test asmdef too — defer to keep diff small.)*

**Gate:** asmdef files parse as valid JSON. (Compile still broken — namespaces next.)

---

### Phase 4 — Rewrite namespaces

Mechanical find/replace across the 22 moved `.cs` files:

```
namespace Hermes          →  namespace OpenaiCompatibleAgent
namespace DesktopMatePlus →  namespace OpenaiCompatibleAgent
```

And in the 4 DMP files that imported Hermes, remove the now-redundant using:
```
using Hermes;             →  (delete the line)
```

**Files affected:**
- All 22 files: `namespace` line
- 4 files lose `using Hermes;`: `SessionApiClient.cs`, `DmpChatController.cs`, `SessionPanelController.cs`, `VoiceCatalogHandler.cs`

**Gate:**
- `grep -rn "namespace Hermes\b\|namespace DesktopMatePlus\b" Assets/MATE\ ENGINE\ -\ Scripts/OpenaiCompatibleAgent/` → 0
- `unity-cli editor refresh --compile`
- `unity-cli console --type error` — only the **external** callers (tests + smoke runner) should still error.

---

### Phase 5 — Update external callers

#### 5a. Test files (3 files in `Tests/Editor/`)

In each:
```diff
- using Hermes;
+ using OpenaiCompatibleAgent;
```

Files: `TtsRequestQueueTests.cs`, `HermesSmokeRunner.cs`, `PreprocessorTests.cs`.

#### 5b. `HermesSmokeRunner.cs` runtime type lookup ⚠️

Line 179:
```diff
- var ttsAudioPlayer = FindFirstObjectByTypeName("DesktopMatePlus.TtsAudioPlayer");
+ var ttsAudioPlayer = FindFirstObjectByTypeName("OpenaiCompatibleAgent.TtsAudioPlayer");
```

Audit the file for any other namespace-qualified type strings (e.g. `"Hermes.*"` or `"DesktopMatePlus.*"`) and update all of them. `assembly.GetType(typeName)` at line 240 takes whatever string is passed in — every call site is suspect.

#### 5c. `Platform/Win32/Win32ScreenCapture.cs`

Update the comment reference `DesktopMatePlus.ScreenCaptureManager` → `OpenaiCompatibleAgent.ScreenCaptureManager`. Comment only — not load-bearing — but keeps grep clean.

**Gate:**
- `unity-cli editor refresh --compile` → clean
- `unity-cli console --type error` → empty

---

### Phase 6 — Update documentation

Update `AGENTS.md`:
- Directory Structure section: replace `Hermes/` and `DesktopMatePlus/` blocks with the new `OpenaiCompatibleAgent/` tree
- Namespaces section: replace the two entries with `OpenaiCompatibleAgent`
- "Open Follow-Ups" — remove the deferred merge note
- "Recent Changes" — add a 2026-05-26 entry describing this consolidation
- Log Prefixes — unchanged (they're string literals, not namespaces)

Optional: update `KnowledgeBase/CLAUDE.md` if it cross-references either name (audit needed).

**Gate:** Diff of `AGENTS.md` reviewed by user.

---

### Phase 7 — Validation

- [ ] `unity-cli editor refresh --compile` → clean
- [ ] `unity-cli console --type error` → empty
- [ ] `unity-cli test` → test count matches Phase 0 baseline; all green
- [ ] `unity-cli test --mode PlayMode` → all green
- [ ] Open `Mate Engine Main.unity` → no missing-script warnings
- [ ] Manual smoke: enter play mode, send a chat message, verify:
  - Streaming text appears in chat bubble
  - TTS audio plays
  - Emotion crossfade animates blendshapes
  - Session list populates
  - Voice dropdown lists 16 voices, selection persists
- [ ] Final grep check (success criteria §8 above) → both greps return 0

---

## 5. Known Gotchas

| # | Risk | Mitigation |
|---|---|---|
| G1 | Moving `.cs` without its `.cs.meta` → Unity assigns new GUID → all scene refs break | **Always move `.cs` + `.cs.meta` as a pair.** Use `git mv` to make this atomic. |
| G2 | Unity Editor open during bulk moves → importer race, asset DB corruption | Close Editor before Phase 2; or use AssetDatabase.MoveAsset from a script |
| G3 | `HermesSmokeRunner.cs:179` string `"DesktopMatePlus.TtsAudioPlayer"` — runtime lookup, won't show as compile error if missed | Phase 5b explicitly covers it; grep `Assets/` for `"DesktopMatePlus\.\|"Hermes\.` as a final audit |
| G4 | Test asmdef references `"Hermes"` — compile breaks until updated in Phase 3 | Phase 3 covers it |
| G5 | `Hermes.Editor.Tests.asmdef` itself is *named* `Hermes.Editor.Tests` — fine to keep that filename for now; only its `references` array points at `Hermes` which we change | No-op; renaming the test asmdef is optional follow-up |
| G6 | `rootNamespace` in asmdef affects new-file template only, not existing files — harmless if stale | Updating anyway per Phase 3 for cleanliness |
| G7 | Log prefixes `[Hermes]`, `[SessionPanel]`, `[TTS]`, `[Voice]`, `[SessionAPI]` are string literals — keeping them avoids log-grep churn | **Do not change log prefixes.** They are a separate identity from the namespace. |
| G8 | Class names like `HermesResponseClient`, `DmpChatController` reference the old module names — kept on purpose this round | Rename pass is a separate, optional follow-up refactor |
| G9 | `handoff_hermes_sdk_hang.md` references `HermesResponseClient` — class name unchanged so no edit needed | Verify after Phase 4 |

---

## 6. Rollback Strategy

This refactor is git-trackable as a single branch.

- **During Phase 2–4:** If anything looks wrong, `git checkout -- .` reverts file moves and edits cleanly.
- **After Phase 7:** If a regression is found post-merge, `git revert <merge-commit>` is the recovery path.

**Do not squash the phase commits in the feature branch** — each phase's gate is captured in a separate commit so bisecting a regression is straightforward.

---

## 7. Estimated Effort & Risk

| Phase | Manual effort | Risk |
|---|---|---|
| 0 — Pre-flight | 5 min | None (read-only) |
| 1 — Create folder | 2 min | None |
| 2 — Move files | 15 min (scripted) | **Medium** — `.meta` co-move is the critical step |
| 3 — Rename asmdef | 3 min | Low |
| 4 — Rewrite namespaces | 10 min (find/replace) | Low — compile catches mistakes |
| 5 — External callers | 10 min | **Medium** — G3 string lookup is the only thing compile won't catch |
| 6 — Docs | 15 min | None |
| 7 — Validation | 20 min | — |
| **Total** | **~80 min** | **Low overall** given clean dependency graph |

---

## 8. Out of Scope (Explicit Non-Goals)

- **Class renames** (`HermesResponseClient` → `ResponseClient`, etc.) — separate refactor
- **Class-name strings in log prefixes** — keep `[Hermes]`, `[TTS]` etc.
- **Decomposing ScreenCapture or Expression into separate modules** — user chose the flat-merge option; revisit later if needed
- **Updating `ChatBot.cs`** (LLMUnity sample) — already DMP-free per AGENTS.md "Phase E cleanup"
- **Test asmdef rename** (`Hermes.Editor.Tests.asmdef` → `OpenaiCompatibleAgent.Editor.Tests.asmdef`) — defer; only its `references` array matters for compile
- **`KnowledgeBase/` workspace** — different subproject, untouched
- **`handoff_hermes_sdk_hang.md`** — discussion document, references class names which are unchanged
