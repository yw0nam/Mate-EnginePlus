using System.Threading;
using OpenaiCompatibleAgent;            // DmpChatController, TtsAudioPlayer (Assembly-CSharp)
using OpenaiCompatibleAgent.Voice;      // Voice assembly
using UnityEngine;
using UnityEngine.UI;

namespace OpenaiCompatibleAgent.VoiceRuntime
{
    /// <summary>
    /// Drives mic -> Silero VAD -> ASR -> chat injection. Attach to an empty GameObject and
    /// wire the Inspector references. MicButton.onClick should call ToggleListening().
    /// </summary>
    public class VoiceInputController : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] VoiceInputMode mode = VoiceInputMode.AlwaysOn;
        [SerializeField] KeyCode pushToTalkKey = KeyCode.V;

        [Header("Audio / ASR")]
        [SerializeField] string micDeviceName = "";
        [SerializeField] string asrBaseUrl = "http://localhost:5517";
        [SerializeField] string model = "Qwen/Qwen3-ASR-1.7B";

        [Header("VAD")]
        [SerializeField] float threshold = 0.5f;
        [SerializeField] int minSpeechMs = 250;
        [SerializeField] int minSilenceMs = 700;
        [SerializeField] int speechPadMs = 150;
        [SerializeField] int maxSpeechMs = 20000;

        [Header("Scene References")]
        [SerializeField] DmpChatController dmpChatController;
        [SerializeField] TtsAudioPlayer ttsAudioPlayer;
        [SerializeField] Button micButton;
        [SerializeField] Image micButtonImage;

        [Header("State Colors")]
        [SerializeField] Color colorOff = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField] Color colorIdle = new Color(0.3f, 0.8f, 0.4f);
        [SerializeField] Color colorCapturing = new Color(0.4f, 1f, 0.5f);
        [SerializeField] Color colorTranscribing = new Color(0.95f, 0.85f, 0.2f);

        MicrophoneCapture _mic;
        SileroVad _vad;
        VadSegmenter _segmenter;
        AsrClient _asr;
        bool _listening;
        bool _transcribing;
        bool _wasGated;

        void Awake()
        {
            // Keep the voice pipeline alive regardless of the chat/settings panel's visibility.
            // VoiceInput lives under the chat canvas, so toggling that panel off deactivates this
            // GameObject — Update() stops and OnDisable() cuts the mic. This is a logic-only object
            // (no UI of its own; its serialized scene references stay valid after reparenting), so
            // detach it to the scene root at startup so it never deactivates with the panel.
            if (transform.parent != null)
                transform.SetParent(null, false);

            _asr = new AsrClient(asrBaseUrl, model);
            try { _vad = SileroVad.FromStreamingAssets(); }
            catch (System.Exception e) { Debug.LogWarning($"[Voice] Silero load failed: {e.Message}"); }

            _segmenter = new VadSegmenter(new VadSegmenter.Config
            {
                sampleRate = MicrophoneCapture.SampleRate,
                frameSamples = MicrophoneCapture.FrameSamples,
                threshold = threshold,
                minSpeechMs = minSpeechMs,
                minSilenceMs = minSilenceMs,
                speechPadMs = speechPadMs,
                maxSpeechMs = maxSpeechMs
            });
            _segmenter.OnSpeechStart += () => Debug.Log("[Voice][Seg] speech START detected");
            _segmenter.OnSpeechEnd += OnUtterance;

            _mic = new MicrophoneCapture(micDeviceName);
            Debug.Log($"[Voice] Awake done. vad={(_vad != null ? "OK" : "NULL")} mode={mode} asr={asrBaseUrl} micDevice='{(string.IsNullOrEmpty(micDeviceName) ? "(default)" : micDeviceName)}'");
            UpdateColor();
        }

        /// <summary>Wired to MicButton.onClick. Toggles the master voice-input enable.</summary>
        public void ToggleListening()
        {
            _listening = !_listening;
            if (mode == VoiceInputMode.AlwaysOn)
            {
                if (_listening) StartMic(); else StopMic();
            }
            UpdateColor();
            Debug.Log($"[Voice] enabled={_listening} mode={mode}");
        }

        void StartMic()
        {
            if (_vad == null) { Debug.LogWarning("[Voice] VAD unavailable; cannot listen."); _listening = false; return; }
            _vad.ResetState();
            _segmenter.Reset();
            bool ok = _mic.Start();
            Debug.Log($"[Voice] StartMic -> mic.Start()={ok} isRecording={_mic.IsRecording}");
        }

        void StopMic() => _mic?.Stop();

        void Update()
        {
            if (!_listening) return;

            if (mode == VoiceInputMode.PushToTalk)
            {
                if (Input.GetKeyDown(pushToTalkKey)) { _vad.ResetState(); _segmenter.Reset(); _mic.Start(); }
                if (Input.GetKeyUp(pushToTalkKey) && _mic.IsRecording) { FlushPushToTalk(); }
            }

            bool ttsPlaying = ttsAudioPlayer != null && ttsAudioPlayer.IsPlaying;
            bool aiStreaming = dmpChatController != null && dmpChatController.IsStreaming;
            // Pause the mic whenever a turn is in flight — transcribing the user's utterance, the
            // AI generating its reply, or the AI speaking it back (TTS) — and resume only once all
            // three are idle. This is driven by the real turn state (response.completed clears
            // IsStreaming), not a timeout.
            bool gated = _transcribing || aiStreaming || ttsPlaying;

            // Turn just finished (gate released): start the next utterance from a clean VAD state
            // rather than the frozen state left from before the turn.
            if (_wasGated && !gated) { _vad?.ResetState(); _segmenter.Reset(); }
            _wasGated = gated;

            if (mode == VoiceInputMode.AlwaysOn && _mic.IsRecording)
            {
                // While gated, discard buffered mic audio so it can't flood the VAD as a false
                // utterance when the gate lifts (e.g. the companion's own TTS bleed).
                if (gated) _mic.Drain();
                else _mic.Poll(FeedFrame);
            }
            else if (mode == VoiceInputMode.PushToTalk && _mic.IsRecording)
                _mic.Poll(FeedFrame);

            UpdateColor();
        }

        void FeedFrame(float[] frame)
        {
            float p = _vad != null ? _vad.Process(frame) : 0f;
            _segmenter.Process(frame, p);
        }

        void FlushPushToTalk()
        {
            _mic.Poll(FeedFrame);
            _mic.Stop();
            int silenceFrames = Mathf.CeilToInt(minSilenceMs / (1000f * MicrophoneCapture.FrameSamples / MicrophoneCapture.SampleRate)) + 2;
            for (int i = 0; i < silenceFrames; i++) _segmenter.Process(new float[MicrophoneCapture.FrameSamples], 0f);
        }

        async void OnUtterance(float[] pcm)
        {
            float durSec = pcm.Length / (float)MicrophoneCapture.SampleRate;
            byte[] wav = WavEncoder.Encode(pcm, MicrophoneCapture.SampleRate);
            Debug.Log($"[Voice][ASR] speech END -> pcm={pcm.Length} samples ({durSec:F2}s) wav={wav.Length} bytes; sending to {asrBaseUrl}");
            _transcribing = true; UpdateColor();
            string text;
            try
            {
                using (var cts = new CancellationTokenSource(60000))
                    text = await _asr.TranscribeAsync(wav, cts.Token);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Voice][ASR] TranscribeAsync threw: {e.GetType().Name}: {e.Message}");
                _transcribing = false; UpdateColor();
                return;
            }
            _transcribing = false;

            Debug.Log($"[Voice][ASR] result='{text}' (len={(text == null ? -1 : text.Length)})");
            if (string.IsNullOrEmpty(text)) { Debug.Log("[Voice] empty transcription, skipping."); UpdateColor(); return; }

            InjectAndSend(text);
        }

        void InjectAndSend(string text)
        {
            if (dmpChatController == null) { Debug.LogWarning("[Voice] dmpChatController not assigned."); return; }

            // inputField is public TMP_InputField; OnSendClicked reads it, clears it, and sends.
            if (dmpChatController.inputField != null)
                dmpChatController.inputField.text = text;

            dmpChatController.OnSendClicked();
            UpdateColor();
            Debug.Log($"[Voice] submitted: {text}");
        }

        void UpdateColor()
        {
            if (micButtonImage == null) return;
            Color c;
            if (!_listening) c = colorOff;
            else if (_transcribing) c = colorTranscribing;
            else if (_segmenter != null && _segmenter.IsInSpeech) c = colorCapturing;
            else c = colorIdle;
            micButtonImage.color = c;
        }

        void OnDisable() { StopMic(); }
        void OnDestroy() { _vad?.Dispose(); }
    }
}
