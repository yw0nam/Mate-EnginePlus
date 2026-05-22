# AGENTS.md — Mate-EnginePlus (Unity)

Unity 6 desktop companion frontend. Streams chat from hermes-agent (localhost:8642), synthesizes speech via Irodori-TTS (localhost:8091), animates VRM avatar.

## Quick Reference

- **Unity version**: 6000.2.6f2
- **Main scene**: `Assets/MATE ENGINE - Scenes/Mate Engine Main.unity`
- **Serialization**: Force Text + Visible Meta Files
- **JSON**: `com.unity.nuget.newtonsoft-json` UPM
- **Text/UI**: TextMeshPro + UGUI
- **OpenAI SDK**: `com.openai.unity` v8.8.9 (OpenUPM)

## unity-cli Commands

```bash
unity-cli status                          # Check connection
unity-cli editor play --wait              # Enter play mode
unity-cli editor stop                     # Stop play mode
unity-cli editor refresh --compile        # Recompile, wait for completion
unity-cli console --type error            # Read compile errors
unity-cli console --stacktrace user       # Errors with stack traces
unity-cli exec "return Application.dataPath;"  # Run C# in Unity
unity-cli reserialize Assets/path.prefab  # Validate YAML after text edits
unity-cli test                            # Run EditMode tests
unity-cli test --mode PlayMode            # Run PlayMode tests
```

**After editing `.prefab`, `.unity`, `.asset`, or `.mat` as text YAML**: Run `unity-cli reserialize <path>`.

**After editing `.cs` files**: Run `unity-cli editor refresh --compile` and check `unity-cli console --type error`.

## Directory Structure

```
Assets/MATE ENGINE - Scripts/
  Hermes/                     # Backend integration (7 files)
    HermesResponseClient.cs   # OpenAI SDK wrapper
    StreamingOrchestrator.cs  # Main orchestrator
    SentenceChunker.cs        # Sentence boundary detection
    Preprocessor.cs           # Text cleanup + emotion detection
    IrodoriClient.cs          # TTS HTTP client
    FastBunkaiSidecarClient.cs # Sidecar for /eos
    TtsRequestQueue.cs        # Sequence-preserving TTS queue
  DesktopMatePlus/            # UI + session management (15 files)
    Api/SessionApiClient.cs   # REST client for hermes sessions
    Chat/DmpChatController.cs # Chat UI controller
    Expression/
      TtsAudioPlayer.cs       # WAV → AudioClip playback
      EmotionCrossfader.cs    # Blendshape animator
      AmplitudeLipSync.cs     # Lip sync from audio
    UI/
      SessionPanelController.cs  # Session list UI
      SessionSlotHandler.cs      # Per-session slot
      SessionPanelToggle.cs      # Show/hide toggle
      DraggablePanel.cs          # Draggable window base
      DmpChatMessageItem.cs      # Chat bubble renderer
      VoiceCatalogHandler.cs     # Voice dropdown (runtime voice selection)
    ScreenCapture/            # Screen capture (text-only, image input unwired)
  Settings/                   # Settings UI
  AvatarHandlers/             # Avatar behavior
  VRMLoader/                  # VRM model loading
  BlendshapeManager/          # Blendshape UI
  APIs/                       # Discord, Steam, Win32
  Tasty Pie Menu/             # Radial pie menu
  ThemeManager/               # UI theming
  Game APIs/                  # Minecraft integration
  Tools/                      # Misc tools
  Lang/                       # Localization
  Tests/                      # EditMode + PlayMode tests
MATE ENGINE - Scenes/         # Unity scenes
MATE ENGINE - Animations/     # Animation assets
MATE ENGINE - Packages/       # Embedded packages (VRM, UniGLTF, Steamworks)
MATE ENGINE - Shaders/        # lilToon, Poiyomi, kage
MATE ENGINE - Resources/      # Asset resources
LLMUnity/                     # Local LLM (legacy, optional)
  Samples/ChatBot/ChatBot.cs  # Chat UI — local-LLM mode only
Editor/                       # Editor tools
```

## Namespaces

- **`Hermes`**: Backend integration (streaming, TTS, orchestration)
- **`DesktopMatePlus`**: UI controllers, session management, expression handlers
- **`LLMUnitySamples`**: ChatBot sample (local LLM only)

## Log Prefixes

- `[Hermes]` — orchestrator
- `[SessionPanel]` — session UI
- `[SessionAPI]` — REST client
- `[TTS]` — audio playback
- `[Voice]` — voice catalog handler

## UI Architecture Principle

**Agent cannot see Unity scene objects.** Therefore:

1. Write C# logic (MonoBehaviour scripts)
2. Tell the user **exactly which GameObject** to attach each script to
3. Tell the user **which Inspector fields** to assign
4. Let the user wire Button.onClick, Toggle.onValueChanged etc. in the Inspector

**Do NOT** try to programmatically find and wire deeply nested scene objects. This causes errors because the agent cannot verify the hierarchy.

## UI Pattern: Template-Based Cloning

Session list uses a template-clone pattern:
- First child named `SessionItem` in ScrollRect content is hidden (template)
- Clones are named `SessionSlot`
- Each slot displays: title (3-tier fallback: title → preview → "Session " + id[..8]), 24-char truncate with U+2026 ellipsis

## Anti-Patterns

- **Never** edit `.unity` scene files directly (too large, merge-hostile). Use `unity-cli exec` or instruct user to make changes manually.
- **Never** hardcode object paths like `transform.Find("deeply/nested/path")` — fragile. Use serialized `[Header]` fields assigned in Inspector.
- **Never** suppress C# warnings with `#pragma warning disable`. Fix the root cause.
- **Never** modify files in `MATE ENGINE - Packages/` or `LLMUnity/` (vendor code) unless absolutely necessary. Document any changes.

## Build / Test

```bash
unity-cli editor refresh --compile       # Compile and check for errors
unity-cli console --type error           # Verify no compile errors
unity-cli test                           # Run EditMode tests
unity-cli test --mode PlayMode           # Run PlayMode tests
```

No CI pipeline. Validation is manual via unity-cli.

## Conventions

- **Folder names have spaces**: Always quote paths. Example: `"Assets/MATE ENGINE - Scripts/Hermes/StreamingOrchestrator.cs"`
- **Event wiring**: Mixed (some code-based, some Inspector). User prefers Inspector-based wiring — write logic, instruct user which GameObject to attach to.

## Key Components

### Hermes Integration

**HermesResponseClient** (`Hermes/HermesResponseClient.cs`)
- Wraps OpenAI SDK (`com.openai.unity`)
- Streaming via `CreateModelResponseAsync`
- Tracks `LastResponseId` for multi-turn continuity (public getter + setter for session restoration)
- Main-thread marshaling via `ConcurrentQueue<Action>`

**StreamingOrchestrator** (`Hermes/StreamingOrchestrator.cs`)
- Orchestrator MonoBehaviour `[ExecuteAlways]`
- Composes: chunker, preprocessor, TTS queue, audio player
- Entry point: `SendAsync(text, onTokenDelta, onTurnComplete, onError, ct)`
- `CurrentVoiceId` property — runtime voice override; falls back to serialized `referenceVoiceId` Inspector default. Set by `VoiceCatalogHandler`.
- Resets TTS player between turns

**SentenceChunker** (`Hermes/SentenceChunker.cs`)
- Buffers tokens, detects sentence boundaries
- Calls `/eos` sidecar (fast-bunkai) when buffer ends with sentence-ender
- Yields complete sentences only if length ≥ `minChunkLength` (default 50)

**Preprocessor** (`Hermes/Preprocessor.cs`)
- Strips `*action*` (regex `\*[^*]*\*`) and `[meta]` (regex `\[[^\]]*\]`)
- Collapses whitespace, trims
- Detects first emoji from known set, extracts emotion

**IrodoriClient** (`Hermes/IrodoriClient.cs`)
- Multipart POST to `http://localhost:8091/synthesize`
- Attaches reference voice MP3 from `D:\codes\waifu\references_voices\<voice_id>\merged_audio.mp3`
- Returns WAV bytes (48 kHz mono 16-bit PCM)
- Inspector fields: `irodoriBaseUrl`, `voicesRootPath`, `defaultVoiceId`
- Public getters: `VoicesRootPath`, `DefaultVoiceId` (consumed by `VoiceCatalogHandler`)

**TtsRequestQueue** (`Hermes/TtsRequestQueue.cs`)
- Sequence-preserving TTS request queue
- `Enqueue(sequence, text, emotion, refId)` creates synthesis task
- `WaitBarrierAsync(timeout=30s)` awaits all pending, cancels stragglers on timeout
- Emits results in sequence order via `OnResult(seq, wav, emotion)` callback

### UI Integration

**DmpChatController** (`DesktopMatePlus/Chat/DmpChatController.cs`)
- Wires to `StreamingOrchestrator.SendAsync`
- Manages chat bubbles, screen capture (text-only, image input unwired)
- Fires `_wasNewSession` event to refresh session list after completion

**SessionPanelController** (`DesktopMatePlus/UI/SessionPanelController.cs`)
- Populates session list from `SessionApiClient.ListSessions`
- Handles select/new-chat/rename
- On session select: restores `previous_response_id` chain from `SessionInfo.last_response_id` (falls back to `Reset()` for legacy/empty sessions)
- On new chat: calls `hermesClient.Reset()` to start a fresh chain

**SessionApiClient** (`DesktopMatePlus/Api/SessionApiClient.cs`)
- REST client for hermes session API (localhost:8642)
- Bearer auth: `Authorization: Bearer hermes_api_key`
- Methods: `ListSessions`, `GetChatHistory`, `UpdateSessionTitle`
- Parses `data` envelope, handles newest-first message ordering

**TtsAudioPlayer** (`DesktopMatePlus/Expression/TtsAudioPlayer.cs`)
- WAV bytes → AudioClip playback
- Sequence-preserving via `SortedList<int, byte[]>`
- Fires `OnWavChunkStarted(int sequence, string emotion)` event
- `Reset()` clears state between turns

**EmotionCrossfader** (`DesktopMatePlus/Expression/EmotionCrossfader.cs`)
- Listens to `TtsAudioPlayer.OnWavChunkStarted`
- Animates blendshapes per emotion via `UniversalBlendshapes` (Joy/Angry/Sorrow/Fun/Neutral)
- Crossfades between emotions smoothly (0.25s linear lerp by default)

**VoiceCatalogHandler** (`DesktopMatePlus/UI/VoiceCatalogHandler.cs`)
- Scans `IrodoriClient.VoicesRootPath` at runtime, filters by `merged_audio.mp3` presence
- Populates TMP_Dropdown with voice folder names (16 voices: 七海, ナツメ, あやせ, ムラサメ, レナ, 千咲, 小春, 希, 愛衣, 栞那, 涼音, 羽月, 芦花, 芳乃, 茉優, 茉子)
- Restores selection from `SaveLoadHandler.data.selectedVoiceId` (fallback: `IrodoriClient.DefaultVoiceId`, then index 0)
- Persists changes via `SaveLoadHandler.SaveToDisk()`
- Seeds `StreamingOrchestrator.CurrentVoiceId` on startup
- Voice change takes effect on next turn (not mid-turn)
- See `.sisyphus/plans/voice-catalog-ui-wiring.md` for Inspector setup

## Debugging

**Compile errors**:
```bash
unity-cli editor refresh --compile
unity-cli console --type error
```

**Runtime errors**:
```bash
unity-cli console --stacktrace user
```

**Play mode**:
```bash
unity-cli editor play --wait
# ... run tests or manual steps ...
unity-cli editor stop
```

**Check hermes backend**:
```powershell
Invoke-WebRequest http://localhost:8642/health -UseBasicParsing
```

**Check Irodori-TTS** (use httpx; `Invoke-WebRequest` to :8091 has known timeout quirks even when the tunnel is healthy):
```powershell
uv run --with httpx python -c "import httpx; r=httpx.get('http://127.0.0.1:8091/health', timeout=15); print(r.status_code, r.text)"
```

## Recent Changes

- **Voice catalog UI (Option B)** [2026-05-16]: Chat-side TMP_Dropdown for runtime voice selection. `StreamingOrchestrator.CurrentVoiceId` overrides serialized `referenceVoiceId`; selection persisted via `SaveLoadHandler.selectedVoiceId`. Inspector wiring guide: `.sisyphus/plans/voice-catalog-ui-wiring.md`. Affects next turn only (not mid-turn switching).
- **Keyframe pipeline retired** [2026-05-16]: Deleted `Keyframe.cs`, `EmotionMapper.cs`, `emotion_motion_map.yaml`, `EmotionMapperTests.cs`. Removed `List<Keyframe>` parameter from `TtsRequestQueue.Enqueue/OnResult`, `TtsAudioPlayer.OnWavChunkStarted/EnqueueWavBytes`, and `EmotionCrossfader.HandleChunkStarted`. Removed `emotionMapper` field from `StreamingOrchestrator`. Rationale: keyframes were never consumed; emotion-only crossfade is sufficient.
- **`previous_response_id` chain restoration on session load**: Backend now exposes `last_response_id` per session (`GET /api/sessions[/{id}]`). `SessionInfo.last_response_id` field added; `HermesResponseClient.LastResponseId` setter is now public so `SessionPanelController.SelectSession` can seed the chain from the loaded session (falls back to `Reset()` when the field is empty — legacy / pre-migration / non-Responses-API sessions).
- **Session title display**: 3-tier fallback (title → preview → "Session " + id[..8]), 24-char truncate with U+2026 ellipsis
- **Phase E cleanup**: Deleted WebSocket client (`DesktopMatePlusClient.cs`, `DesktopMatePlusMessages.cs`). Removed DMP branch from `ChatBot.cs`. Cleaned up legacy TTS base64 queue. Rewired `EmotionCrossfader` to `OnWavChunkStarted` event.

## Open Follow-Ups

_(none — all known follow-ups resolved 2026-05-16. Future candidates: mid-turn voice switching (Option C), DesktopMatePlus/+Hermes/ → OpenaiCompatibleAgent/ folder merge — both deferred.)_
