using OpenaiCompatibleAgent;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Existing SettingsMenuCanvas / = AI section uses the legacy UnityEngine.UI.InputField
// (see AiSystemPrompt), so this handler matches that for clone-friendliness.

/// <summary>
/// Wires the "= AI CHAT" section of SettingsMenuCanvas to the runtime
/// Hermes / Irodori / chat-controller fields and persists changes through
/// <see cref="SaveLoadHandler"/>.
///
/// Attach to the same "Settings" GameObject as the other SettingsHandler*
/// scripts and assign the references in the Inspector.
/// </summary>
public class SettingsHandlerHermes : MonoBehaviour
{
    [Header("Runtime targets")]
    public HermesResponseClient hermesClient;
    public IrodoriClient irodoriClient;
    public StreamingOrchestrator streamingOrchestrator;
    public DmpChatController chatController;

    [Header("Hermes connection")]
    public InputField hermesHostInput;
    public InputField hermesPortInput;
    public InputField hermesApiKeyInput;
    public InputField hermesModelIdInput;
    public Button hermesReinitializeButton;

    [Header("Irodori TTS")]
    public InputField irodoriBaseUrlInput;
    public InputField voicesRootPathInput;

    [Header("Streaming")]
    public Slider sentenceMinChunkLengthSlider;
    public TMP_Text sentenceMinChunkLengthLabel;
    public Slider ttsBarrierTimeoutSlider;
    public TMP_Text ttsBarrierTimeoutLabel;

    [Header("Chat UI")]
    public Slider chatMaxMessagesSlider;
    public TMP_Text chatMaxMessagesLabel;
    public Toggle chatAutoScrollToggle;
    public InputField chatAiNameInput;
    public InputField chatUserNameInput;

    private void Start()
    {
        WireListeners();
        LoadSettings();
        ApplySettings();
    }

    private void WireListeners()
    {
        hermesHostInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.hermesHost = v;
            Save();
        });
        hermesPortInput?.onEndEdit.AddListener(v =>
        {
            if (int.TryParse(v, out var port) && port > 0 && port <= 65535)
            {
                SaveLoadHandler.Instance.data.hermesPort = port;
            }
            else
            {
                // restore previous valid value
                hermesPortInput.SetTextWithoutNotify(SaveLoadHandler.Instance.data.hermesPort.ToString());
            }
            Save();
        });
        hermesApiKeyInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.hermesApiKey = v;
            Save();
        });
        hermesModelIdInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.hermesModelId = v;
            Save();
        });
        hermesReinitializeButton?.onClick.AddListener(ApplyHermesAndReinitialize);

        irodoriBaseUrlInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.irodoriBaseUrl = v;
            if (irodoriClient != null) irodoriClient.BaseUrl = v;
            Save();
        });
        voicesRootPathInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.voicesRootPath = v;
            if (irodoriClient != null) irodoriClient.SetVoicesRootPath(v);
            Save();
        });

        sentenceMinChunkLengthSlider?.onValueChanged.AddListener(v =>
        {
            int iv = Mathf.RoundToInt(v);
            SaveLoadHandler.Instance.data.sentenceMinChunkLength = iv;
            if (streamingOrchestrator != null) streamingOrchestrator.SentenceMinChunkLength = iv;
            if (sentenceMinChunkLengthLabel != null) sentenceMinChunkLengthLabel.text = iv.ToString();
            Save();
        });
        ttsBarrierTimeoutSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.ttsBarrierTimeoutSeconds = v;
            if (streamingOrchestrator != null) streamingOrchestrator.TtsBarrierTimeoutSeconds = v;
            if (ttsBarrierTimeoutLabel != null) ttsBarrierTimeoutLabel.text = v.ToString("0.0") + "s";
            Save();
        });

        chatMaxMessagesSlider?.onValueChanged.AddListener(v =>
        {
            int iv = Mathf.RoundToInt(v);
            SaveLoadHandler.Instance.data.chatMaxMessages = iv;
            if (chatController != null) chatController.maxMessages = iv;
            if (chatMaxMessagesLabel != null) chatMaxMessagesLabel.text = iv.ToString();
            Save();
        });
        chatAutoScrollToggle?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.chatAutoScroll = v;
            if (chatController != null) chatController.autoScroll = v;
            Save();
        });
        chatAiNameInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.chatAiName = v;
            if (chatController != null) chatController.aiName = v;
            Save();
        });
        chatUserNameInput?.onEndEdit.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.chatUserName = v;
            if (chatController != null) chatController.userName = v;
            Save();
        });
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null) return;

        hermesHostInput?.SetTextWithoutNotify(data.hermesHost);
        hermesPortInput?.SetTextWithoutNotify(data.hermesPort.ToString());
        hermesApiKeyInput?.SetTextWithoutNotify(data.hermesApiKey);
        hermesModelIdInput?.SetTextWithoutNotify(data.hermesModelId);

        irodoriBaseUrlInput?.SetTextWithoutNotify(data.irodoriBaseUrl);
        voicesRootPathInput?.SetTextWithoutNotify(data.voicesRootPath);

        sentenceMinChunkLengthSlider?.SetValueWithoutNotify(data.sentenceMinChunkLength);
        if (sentenceMinChunkLengthLabel != null)
            sentenceMinChunkLengthLabel.text = data.sentenceMinChunkLength.ToString();
        ttsBarrierTimeoutSlider?.SetValueWithoutNotify(data.ttsBarrierTimeoutSeconds);
        if (ttsBarrierTimeoutLabel != null)
            ttsBarrierTimeoutLabel.text = data.ttsBarrierTimeoutSeconds.ToString("0.0") + "s";

        chatMaxMessagesSlider?.SetValueWithoutNotify(data.chatMaxMessages);
        if (chatMaxMessagesLabel != null)
            chatMaxMessagesLabel.text = data.chatMaxMessages.ToString();
        chatAutoScrollToggle?.SetIsOnWithoutNotify(data.chatAutoScroll);
        chatAiNameInput?.SetTextWithoutNotify(data.chatAiName);
        chatUserNameInput?.SetTextWithoutNotify(data.chatUserName);
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null) return;

        if (hermesClient != null)
        {
            hermesClient.Host = data.hermesHost;
            hermesClient.Port = data.hermesPort;
            hermesClient.SetApiKey(data.hermesApiKey);
            hermesClient.ModelId = data.hermesModelId;
            // Don't auto-reinitialize on every Apply — the OpenAIClient may be
            // mid-stream. The dedicated "Reinitialize" button drives that.
        }
        if (irodoriClient != null)
        {
            irodoriClient.BaseUrl = data.irodoriBaseUrl;
            irodoriClient.SetVoicesRootPath(data.voicesRootPath);
        }
        if (streamingOrchestrator != null)
        {
            streamingOrchestrator.SentenceMinChunkLength = data.sentenceMinChunkLength;
            streamingOrchestrator.TtsBarrierTimeoutSeconds = data.ttsBarrierTimeoutSeconds;
        }
        if (chatController != null)
        {
            chatController.maxMessages = data.chatMaxMessages;
            chatController.autoScroll = data.chatAutoScroll;
            chatController.aiName = data.chatAiName;
            chatController.userName = data.chatUserName;
        }
    }

    private void ApplyHermesAndReinitialize()
    {
        ApplySettings();
        if (hermesClient != null) hermesClient.Reinitialize();
    }

    private void Save()
    {
        SaveLoadHandler.Instance?.SaveToDisk();
    }

    public void ResetToDefaults()
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null) return;
        data.hermesHost = "localhost";
        data.hermesPort = 8642;
        data.hermesApiKey = "hermes_api_key";
        data.hermesModelId = "hermes-agent";
        data.irodoriBaseUrl = "http://localhost:8091";
        data.sentenceMinChunkLength = 50;
        data.ttsBarrierTimeoutSeconds = 30f;
        data.chatMaxMessages = 100;
        data.chatAutoScroll = true;
        data.chatAiName = "AI";
        data.chatUserName = "User";
        LoadSettings();
        ApplySettings();
        Save();
    }
}
