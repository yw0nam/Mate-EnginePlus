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
        bool _awaitingResponse;
        float _awaitingSince;
        bool _transcribing;

        void Awake()
        {
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
            _segmenter.OnSpeechEnd += OnUtterance;

            _mic = new MicrophoneCapture(micDeviceName);
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
            _mic.Start();
        }

        void StopMic() => _mic.Stop();

        void Update()
        {
            if (!_listening) return;

            if (mode == VoiceInputMode.PushToTalk)
            {
                if (Input.GetKeyDown(pushToTalkKey)) { _vad.ResetState(); _segmenter.Reset(); _mic.Start(); }
                if (Input.GetKeyUp(pushToTalkKey) && _mic.IsRecording) { FlushPushToTalk(); }
            }

            bool ttsPlaying = ttsAudioPlayer != null && ttsAudioPlayer.IsPlaying;
            // Once TTS starts, IsPlaying governs the gate. Otherwise wait only briefly for a
            // response that may produce no TTS (text-only turn / TTS down) rather than blocking long.
            if (ttsPlaying) _awaitingResponse = false;
            else if (_awaitingResponse && Time.time - _awaitingSince > 8f) _awaitingResponse = false;

            bool gated = ttsPlaying || _awaitingResponse;

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
            byte[] wav = WavEncoder.Encode(pcm, MicrophoneCapture.SampleRate);
            _transcribing = true; UpdateColor();
            string text;
            using (var cts = new CancellationTokenSource(60000))
                text = await _asr.TranscribeAsync(wav, cts.Token);
            _transcribing = false;

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
            _awaitingResponse = true;
            _awaitingSince = Time.time;
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
