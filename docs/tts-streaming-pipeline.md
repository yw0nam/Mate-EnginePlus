# TTS Streaming Pipeline (Mate Engine)

hermes-agent에서 스트리밍으로 들어오는 텍스트를 문장 단위로 잘라 비동기 TTS로 음성을 만들고, 순서를 보장하며 재생하는 파이프라인 문서.

핵심 흐름 (한 줄 요약):

```
hermes SSE 토큰 → 텍스트 버퍼 누적 → sentence boundary 감지 → 절단 →
비동기 TTS 합성(동시) → 시퀀스 순서 재정렬 → 순서대로 재생
```

> 모든 경로 코드는 `Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/` 아래, 네임스페이스 `OpenaiCompatibleAgent`.

---

## 단계별 흐름

### 1. hermes 스트리밍 수신 (`Backend/HermesResponseClient.cs`)

- 채팅 경로는 **항상 스트리밍**: `BuildRequestBody(userText, imageDataUrls, stream: true)` (`:162`) → JSON body `"stream": true` (`:391`). `Accept: text/event-stream` 헤더(`:168`).
  - 비스트리밍 `SendNonStreamingAsync`(`stream: false`, `:211`)는 smoke test/디버깅 전용이며 채팅에는 사용되지 않음.
- `POST {host:port}/v1/responses` → SSE 응답을 `ReadSseAsync`(`:251`)가 라인 단위로 읽고, `DispatchEvent`(`:292`)가 이벤트별로 분기.
  - `response.created` / `response.in_progress` → `LastResponseId` 갱신 (대화 체인용 `previous_response_id`)
  - `response.output_text.delta` → `onTokenDelta(delta)` 호출 (`:321-326`) — **증분 토큰**
  - `response.completed` / `response.incomplete` → 턴 종료
  - `response.failed` / `error` → 에러
  - 그 외(function_call/file_search/item/part 등)는 전부 무시 → 툴 응답이 파싱을 깨지 않음
- 모든 콜백은 `_mainThreadQueue`에 적재되어 `Update()`의 `PumpMainThreadQueue()`(`:90-99`)에서 **Unity 메인 스레드**로 마샬링됨.

### 2. 텍스트 버퍼 누적 + 필터링 (`Backend/SentenceChunker.cs`)

- `StreamingOrchestrator.HandleTokenDelta(delta)` (`StreamingOrchestrator.cs:241-256`)가 토큰을 받아 `_chunker.FeedAsync(delta, ct)` 호출.
- `FeedAsync` (`:51-102`):
  - `FilterReasoningStream` — `<think>...</think>` reasoning 블록 제거 (스트림 경계에 걸친 부분 태그도 `_partialTag`로 보류 처리)
  - `ApplyToolCallFilter` — `{'type':'tool_call' ...}` JSON 제거
  - 정제된 텍스트를 `_buffer`(StringBuilder)에 누적

### 3. Sentence boundary 감지

- **로컬 pre-gate** (`:66`): 버퍼에 종결 문자(`。！？.!?\n`)가 하나라도 있으면 진행. (불필요한 sidecar 호출 회피)
- **fast-bunkai sidecar** (`:73`): `_sidecar.FindEosAsync(buffer, ct)` → `POST /eos` (`FastBunkaiSidecarClient.cs`) 가 문장 경계 문자 오프셋 배열 반환.
- **경계 검증** `FilterRealPositions` (`:191-215`): 각 경계 위치의 prefix 마지막 글자가 실제 종결 문자인 위치만 채택 → **완성 문장만** 통과.

### 4. 절단 (cut)

- `:84-88` 루프: 채택된 경계마다 `_buffer.ToString(0, pos)`로 prefix 추출, `_buffer.Remove(0, pos)`로 버퍼에서 잘라냄.
- **최소 길이 게이트** (`:85`): segment 길이가 `minChunkLength`(기본 **50** UTF-16자) 미만이면 단독 방출하지 않고 다음 문장과 병합되도록 버퍼에 유지.
- 버퍼에 완성 문장 경계가 더 남아있으면 루프를 돌며 연속 방출, 미완성 tail만 남으면 종료.

> **동작 노트 (2026-06-01 변경):** 이전에는 "버퍼의 마지막 비공백 글자가 종결 문자일 때만" 청킹을 시도했다(`LastNonWhitespaceIsSentenceEnder` 게이트). 그래서 한 delta 안에 `문장1。 + 문장2 시작`이 함께 들어오면 완성된 `문장1。`의 방출이 다음 delta까지 지연됐다. 이 게이트를 제거하여 **버퍼 중간의 완성 문장도 즉시 방출**한다. 정확성은 `FilterRealPositions`가 보장하므로 영향 없음.
>
> **Tradeoff:** 버퍼에 종결 문자가 하나라도 있으면 매 delta마다 fast-bunkai `/eos`를 호출한다(이전엔 버퍼 끝이 종결일 때만). sidecar는 localhost라 비용은 작다.

### 5. 비동기 TTS 합성 (동시) (`Backend/TtsRequestQueue.cs`)

- `StreamingOrchestrator.EnqueueSentence` (`:332-341`): `Preprocessor.Process`로 `*action*`/`[meta]` 태그 제거 및 **emotion 추출** → `_ttsQueue.Enqueue(seq, clean, emotion, voiceId)`.
- `TtsRequestQueue.Enqueue` (`:33-53`): 기다리지 않고 **즉시** `SynthesizeOneAsync` Task 시작 → 여러 문장이 **동시 병렬**로 합성됨.
- `SynthesizeOneAsync` (`:117-151`): `_tts.SynthesizeAsync(text, referenceId, ct)` 호출. `ITtsClient` 구현체는 활성 provider에 따라 결정:
  - `FishSpeechClient` (`:8092` `/v1/audio/speech`, JSON, 44.1kHz WAV) — **기본**
  - `IrodoriClient` (`:8091` `/synthesize`, multipart, 48kHz WAV)
  - provider 선택: `StreamingOrchestrator.ResolveActiveClient` / `CurrentProvider`, 턴 경계에서 큐 재생성(`EnsureTtsQueue` `:184-203`).

### 6. 시퀀스 순서 재정렬 (`Backend/TtsRequestQueue.cs`)

- `StoreAndDrain` (`:153-187`): 합성이 순서 뒤섞여 끝나도 결과를 `SortedDictionary<int, TtsResult>`에 저장하고, `_nextSeqToEmit`부터 **연속된 시퀀스만** 순서대로 방출 → 동시 합성에도 재생 순서 보장.
- `OnResult` → `StreamingOrchestrator.HandleTtsResult` (`:343-352`) → `EnqueueWavBytes` (reflection, `:354-371`).

### 7. 순서대로 재생 (`Expression/TtsAudioPlayer.cs`)

- `EnqueueWavBytes(seq, wav, emotion)` (`:47-59`): seq를 키로 `SortedList`에 적재.
- `PlayNext()` (`:72-90`): `_nextSequence`에 해당하는 chunk를 WAV 디코드(헤더에서 sample rate 읽음 → 48k/44.1k 모두 처리) 후 `AudioSource.Play()`. `OnWavChunkStarted(seq, emotion)` 이벤트 발행 → `EmotionCrossfader`가 blendshape 구동.
- `Update()` (`:32-39`): 현재 클립 재생이 끝나면 다음 chunk 재생(폴링 방식).

### 8. 턴 종료 flush + 배리어 (`Backend/StreamingOrchestrator.cs`)

- `HandleStreamComplete` (`:258-305`): 스트림 종료 시 `_chunker.Flush()`로 남은 버퍼를 길이와 무관하게 강제 방출 → enqueue.
- `_ttsQueue.WaitBarrierAsync(timeout)` (`:268`)로 모든 미완료 합성 완료를 대기(기본 30s, 타임아웃 시 잔여 요청 취소) 후, **메인 스레드로 마샬링**하여 `onTurnComplete` 콜백 호출.

---

## 자료구조: 3개의 큐/버퍼

| # | 위치 | 자료구조 | 역할 |
|---|------|----------|------|
| 1 | `SentenceChunker._buffer` | `StringBuilder` | 스트리밍 토큰 누적 → 문장 경계까지 절단 |
| 2 | `TtsRequestQueue` (`_pending` / `_completed`) | `List` + `SortedDictionary` | 동시 합성 + 시퀀스 순서 재정렬 |
| 3 | `TtsAudioPlayer._queueBytes` | `SortedList<int, …>` | 디코드/재생 대기 큐(순서 보장) |

---

## 설정값

`sentenceMinChunkLength` — 문장 최소 길이(기본 **50** UTF-16자).

- 코드 기본값: `StreamingOrchestrator.cs:32`, `SaveLoadHandler.cs:198`
- Settings UI 슬라이더로 런타임 조정 (범위 10–200, `AddHermesSettingsRows.cs:40`)
- `SaveLoadHandler.data.sentenceMinChunkLength`에 영속
- 시작 시 `SettingsHandlerHermes.ApplySettings` (`:209`)가 저장값을 `streamingOrchestrator.SentenceMinChunkLength`에 주입 → 씬 직렬화값/코드 기본값을 덮어씀

기타: `ttsBarrierTimeoutSeconds`(기본 30s), `CurrentProvider`(기본 FishSpeech), `CurrentVoiceId`.

---

## 관련 파일

- `Backend/HermesResponseClient.cs` — SSE 스트리밍 클라이언트 (SDK 미사용, 자체 HTTP+SSE 파서)
- `Backend/StreamingOrchestrator.cs` — 파이프라인 오케스트레이터 (진입점 `SendAsync`)
- `Backend/SentenceChunker.cs` — 문장 경계 감지/절단
- `Backend/Preprocessor.cs` — 텍스트 정제 + emotion 추출
- `Backend/TtsRequestQueue.cs` — 동시 합성 + 순서 재정렬 큐
- `Backend/ITtsClient.cs`, `FishSpeechClient.cs`, `IrodoriClient.cs` — TTS provider seam
- `Backend/FastBunkaiSidecarClient.cs` — `/eos` 문장 경계 sidecar 클라이언트
- `Expression/TtsAudioPlayer.cs` — WAV 디코드 + 순서 재생
- `Expression/EmotionCrossfader.cs` — `OnWavChunkStarted` 구독, emotion blendshape
