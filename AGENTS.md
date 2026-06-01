# AGENTS.md — Mate-EnginePlus (Unity)

Unity 6 desktop companion frontend. Streams chat from hermes-agent (localhost:8642), synthesizes speech via a selectable TTS provider — Fish-Speech (localhost:8092, default) or Irodori-TTS (localhost:8091) — and animates a VRM avatar.

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
  OpenaiCompatibleAgent/      # Unified chat-agent module (ns: OpenaiCompatibleAgent)
    Backend/                  # Backend integration (asmdef: OpenaiCompatibleAgent.Backend)
      HermesResponseClient.cs # OpenAI Responses API client
      StreamingOrchestrator.cs# Main orchestrator + TTS provider selection
      SentenceChunker.cs      # Sentence boundary detection
      Preprocessor.cs         # Text cleanup + emotion detection
      ITtsClient.cs           # Provider-neutral TTS seam (text → WAV)
      IrodoriClient.cs        # Irodori TTS client (:8091, multipart) — ITtsClient
      FishSpeechClient.cs     # Fish-Speech TTS client (:8092, JSON) — ITtsClient
      FastBunkaiSidecarClient.cs # Sidecar for /eos
      TtsRequestQueue.cs      # Sequence-preserving TTS queue (over ITtsClient)
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
      TtsProviderHandler.cs      # TTS-provider dropdown (Fish-Speech / Irodori)
      *.prefab                   # ChatHistoryItem, SessionItem, VoiceItem
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

- **`OpenaiCompatibleAgent`**: Unified chat-agent module — backend streaming, TTS, orchestration, UI, session management, expression handlers (was `Hermes` + `DesktopMatePlus`, merged 2026-05-26)
- **`OpenaiCompatibleAgent.Tests`**: EditMode tests for the module
- **`LLMUnitySamples`**: ChatBot sample (local LLM only)

## Assemblies

- **`OpenaiCompatibleAgent.Backend`** (`OpenaiCompatibleAgent/Backend/`): Isolated backend layer — pure HTTP/streaming/orchestration with no Unity UI deps. Self-contained asmdef referencing OpenAI SDK + Utilities.
- **`Assembly-CSharp`** (default): Everything else under `OpenaiCompatibleAgent/` (Api, Chat, Expression, UI, ScreenCapture) — needs `TextMeshPro` + Assembly-CSharp types like `UniversalBlendshapes`, so it cannot live under an isolated asmdef.
- **`Hermes.Editor.Tests`** (test asmdef, filename unchanged for now): References `OpenaiCompatibleAgent.Backend`.

## Log Prefixes

- `[Hermes]` — orchestrator
- `[FishSpeech]` — Fish-Speech TTS client
- `[Irodori]` — Irodori TTS client
- `[SessionPanel]` — session UI
- `[SessionAPI]` — REST client
- `[TTS]` — audio playback
- `[Voice]` — voice catalog handler
- `[TtsProvider]` — TTS-provider dropdown

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
- **Never** add `*.mat` to `.gitignore` — they are project source. The lilToon shader re-quantizes float properties (7-8th decimal drift on `_MainColor`/`_OverlayColor` etc.) every time Unity reimports them. There is no EditorSettings flag that suppresses this; `m_SerializationMode: 2` (Force Text) is already correct. Accept the noise — commit it occasionally as `chore: refresh material serialization`. If a specific `.mat` is truly volatile, convert it to a **Material Variant** of a stable parent so only deltas serialize. Ignoring the files breaks fresh clones (pink shaders).

## Build / Test

```bash
unity-cli editor refresh --compile       # Compile and check for errors
unity-cli console --type error           # Verify no compile errors
unity-cli test                           # Run EditMode tests
unity-cli test --mode PlayMode           # Run PlayMode tests
```

No CI pipeline. Validation is manual via unity-cli.

## Conventions

- **Folder names have spaces**: Always quote paths. Example: `"Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/Backend/StreamingOrchestrator.cs"`
- **Event wiring**: Mixed (some code-based, some Inspector). User prefers Inspector-based wiring — write logic, instruct user which GameObject to attach to.

## Key Components

### Backend (`OpenaiCompatibleAgent/Backend/`)

**HermesResponseClient** (`OpenaiCompatibleAgent/Backend/HermesResponseClient.cs`)
- Wraps OpenAI SDK (`com.openai.unity`)
- Streaming via `CreateModelResponseAsync`
- Tracks `LastResponseId` for multi-turn continuity (public getter + setter for session restoration)
- Main-thread marshaling via `ConcurrentQueue<Action>`

**StreamingOrchestrator** (`OpenaiCompatibleAgent/Backend/StreamingOrchestrator.cs`)
- Orchestrator MonoBehaviour `[ExecuteAlways]`
- Composes: chunker, preprocessor, TTS queue, audio player
- Entry point: `SendAsync(text, onTokenDelta, onTurnComplete, onError, ct)`
- `CurrentVoiceId` property — runtime voice override; falls back to serialized `referenceVoiceId` Inspector default. Set by `VoiceCatalogHandler`.
- `CurrentProvider` property (`TtsProvider` enum) — runtime TTS provider, default Fish-Speech (serialized `defaultProvider`). `ResolveActiveClient` picks `fishSpeechClient` / `irodoriClient`; `EnsureTtsQueue` rebuilds the queue (cancelling the old one) when the provider changes at a turn boundary. Set by `TtsProviderHandler`. Resets to `defaultProvider` on domain reload; re-seeded from saved settings on startup.
- Resets TTS player between turns

**SentenceChunker** (`OpenaiCompatibleAgent/Backend/SentenceChunker.cs`)
- Buffers tokens, detects sentence boundaries
- Calls `/eos` sidecar (fast-bunkai) when buffer ends with sentence-ender
- Yields complete sentences only if length ≥ `minChunkLength` (default 50)

**Preprocessor** (`OpenaiCompatibleAgent/Backend/Preprocessor.cs`)
- Strips `*action*` (regex `\*[^*]*\*`) and `[meta]` (regex `\[[^\]]*\]`)
- Collapses whitespace, trims
- Detects first emoji from known set, extracts emotion

**ITtsClient** (`OpenaiCompatibleAgent/Backend/ITtsClient.cs`)
- Provider-neutral seam: `Task<byte[]> SynthesizeAsync(string text, string referenceId, CancellationToken ct)` → WAV bytes (null on failure)
- Implemented by `IrodoriClient` and `FishSpeechClient`; `TtsRequestQueue` and `StreamingOrchestrator` depend on it (not on a concrete client)

**IrodoriClient** (`OpenaiCompatibleAgent/Backend/IrodoriClient.cs`) — `ITtsClient`
- Multipart POST to `http://localhost:8091/synthesize`
- Attaches reference voice MP3 from `D:\codes\waifu\references_voices\<voice_id>\merged_audio.mp3`
- Returns WAV bytes (48 kHz mono 16-bit PCM)
- Inspector fields: `irodoriBaseUrl`, `voicesRootPath`, `defaultVoiceId`
- Public getters: `VoicesRootPath`, `DefaultVoiceId` (consumed by `VoiceCatalogHandler`); log prefix `[Irodori]`

**FishSpeechClient** (`OpenaiCompatibleAgent/Backend/FishSpeechClient.cs`) — `ITtsClient`
- JSON POST to `http://localhost:8092/v1/audio/speech` (vLLM-omni `fishaudio/s2-pro`)
- Body: `{ model, input, voice, response_format:"wav" }`; `voice` = server-registered preset (same 16 ids as Irodori), so no reference-audio upload
- Returns WAV bytes (44.1 kHz mono); `HealthCheckAsync` → GET `/health`; log prefix `[FishSpeech]`
- Inspector fields: `baseUrl` (default `http://localhost:8092`), `modelId`, `defaultVoiceId`, `responseFormat`, `language`
- Pure static `BuildRequest(...)` maps params → request DTO (unit-tested)

**TtsRequestQueue** (`OpenaiCompatibleAgent/Backend/TtsRequestQueue.cs`)
- Sequence-preserving TTS request queue
- `Enqueue(sequence, text, emotion, refId)` creates synthesis task
- `WaitBarrierAsync(timeout=30s)` awaits all pending, cancels stragglers on timeout
- Emits results in sequence order via `OnResult(seq, wav, emotion)` callback

### UI Integration (`OpenaiCompatibleAgent/{Api,Chat,Expression,UI,ScreenCapture}/`)

**DmpChatController** (`OpenaiCompatibleAgent/Chat/DmpChatController.cs`)
- Wires to `StreamingOrchestrator.SendAsync`
- Manages chat bubbles, screen capture (text-only, image input unwired)
- Fires `_wasNewSession` event to refresh session list after completion

**SessionPanelController** (`OpenaiCompatibleAgent/UI/SessionPanelController.cs`)
- Populates session list from `SessionApiClient.ListSessions`
- Handles select/new-chat/rename
- On session select: restores `previous_response_id` chain from `SessionInfo.last_response_id` (falls back to `Reset()` for legacy/empty sessions)
- On new chat: calls `hermesClient.Reset()` to start a fresh chain

**SessionApiClient** (`OpenaiCompatibleAgent/Api/SessionApiClient.cs`)
- REST client for hermes session API (localhost:8642)
- Bearer auth: `Authorization: Bearer hermes_api_key`
- Methods: `ListSessions`, `GetChatHistory`, `UpdateSessionTitle`
- Parses `data` envelope, handles newest-first message ordering

**TtsAudioPlayer** (`OpenaiCompatibleAgent/Expression/TtsAudioPlayer.cs`)
- WAV bytes → AudioClip playback
- Sequence-preserving via `SortedList<int, byte[]>`
- Fires `OnWavChunkStarted(int sequence, string emotion)` event
- `Reset()` clears state between turns

**EmotionCrossfader** (`OpenaiCompatibleAgent/Expression/EmotionCrossfader.cs`)
- Listens to `TtsAudioPlayer.OnWavChunkStarted`
- Animates blendshapes per emotion via `UniversalBlendshapes` (Joy/Angry/Sorrow/Fun/Neutral)
- Crossfades between emotions smoothly (0.25s linear lerp by default)

**VoiceCatalogHandler** (`OpenaiCompatibleAgent/UI/VoiceCatalogHandler.cs`)
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

- **Fish-Speech-S2-Pro TTS provider (selectable, default)** [2026-05-31]: New `FishSpeechClient` (`:8092` `/v1/audio/speech`, JSON, 44.1 kHz WAV) behind a provider-neutral `ITtsClient` seam that `IrodoriClient` also implements (its unused per-call tuning params were dropped from the public method). `StreamingOrchestrator` gained a `TtsProvider` enum + `CurrentProvider` (default Fish-Speech) + `EnsureTtsQueue` (rebuilds/cancels the queue on a provider switch at the turn boundary). Settings dropdown `TtsProviderHandler` persists `ttsProvider`; `SettingsHandlerHermes` holds `fishSpeechClient` + an optional base-URL input (`fishSpeechBaseUrl`). Voice ids are identical across providers (1:1). Tests: `FishSpeechClientTests`, `StreamingOrchestratorProviderTests`, plus the `FishSpeechSmokeRunner` editor menu (live `:8092`). Design/plan: `docs/superpowers/specs|plans/2026-05-30-fish-speech-tts-provider*`. Verification: 0 new compile errors; new unit tests green; live smoke returned a valid 44.1 kHz WAV.
- **`Hermes/` + `DesktopMatePlus/` → `OpenaiCompatibleAgent/` merge** [2026-05-26]: Folder + namespace consolidation. All 22 .cs files now under `Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/` (Backend/, Api/, Chat/, Expression/, UI/, ScreenCapture/), and all share namespace `OpenaiCompatibleAgent`. Backend keeps its own asmdef (`OpenaiCompatibleAgent.Backend.asmdef`) at `Backend/` scope only — placing it at the module root would have pulled UI/Chat code under it and broken `using TMPro;` + `UniversalBlendshapes` from Assembly-CSharp. Class names unchanged (`HermesResponseClient`, `DmpChatController`, etc.) to minimize blast radius — rename is a separate optional follow-up. Test asmdef filename unchanged (`Hermes.Editor.Tests.asmdef`); its `references` array now points at `OpenaiCompatibleAgent.Backend`. See `refactor_openai_compatible_agent.md` for the full plan. Verification: 0 compile errors, baseline 215/248 test pass rate preserved exactly (the 33 failures are all pre-existing VRM/UniGLTF model-file-missing issues unrelated to this refactor).
- **Voice catalog UI (Option B)** [2026-05-16]: Chat-side TMP_Dropdown for runtime voice selection. `StreamingOrchestrator.CurrentVoiceId` overrides serialized `referenceVoiceId`; selection persisted via `SaveLoadHandler.selectedVoiceId`. Inspector wiring guide: `.sisyphus/plans/voice-catalog-ui-wiring.md`. Affects next turn only (not mid-turn switching).
- **Keyframe pipeline retired** [2026-05-16]: Deleted `Keyframe.cs`, `EmotionMapper.cs`, `emotion_motion_map.yaml`, `EmotionMapperTests.cs`. Removed `List<Keyframe>` parameter from `TtsRequestQueue.Enqueue/OnResult`, `TtsAudioPlayer.OnWavChunkStarted/EnqueueWavBytes`, and `EmotionCrossfader.HandleChunkStarted`. Removed `emotionMapper` field from `StreamingOrchestrator`. Rationale: keyframes were never consumed; emotion-only crossfade is sufficient.
- **`previous_response_id` chain restoration on session load**: Backend now exposes `last_response_id` per session (`GET /api/sessions[/{id}]`). `SessionInfo.last_response_id` field added; `HermesResponseClient.LastResponseId` setter is now public so `SessionPanelController.SelectSession` can seed the chain from the loaded session (falls back to `Reset()` when the field is empty — legacy / pre-migration / non-Responses-API sessions).
- **Session title display**: 3-tier fallback (title → preview → "Session " + id[..8]), 24-char truncate with U+2026 ellipsis
- **Phase E cleanup**: Deleted WebSocket client (`DesktopMatePlusClient.cs`, `DesktopMatePlusMessages.cs`). Removed DMP branch from `ChatBot.cs`. Cleaned up legacy TTS base64 queue. Rewired `EmotionCrossfader` to `OnWavChunkStarted` event.

## Open Follow-Ups

_(none. Future candidates: mid-turn voice switching (Option C); rename `HermesResponseClient`/`DmpChatController`/etc. class names to drop the legacy `Hermes`/`Dmp` prefixes now that the namespace is unified; rename `Hermes.Editor.Tests.asmdef` filename. All deferred.)_
