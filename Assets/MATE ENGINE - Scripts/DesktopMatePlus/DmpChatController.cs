using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DesktopMatePlus
{
    public class DmpChatController : MonoBehaviour
    {
        [Header("DMP")]
        public DesktopMatePlusClient dmpClient;
        public TtsAudioPlayer ttsPlayer;
        public KeyframeAnimationBridge keyframeBridge;

        [Header("Chat UI")]
        public ScrollRect chatScrollRect;
        public RectTransform messageListContent;
        public GameObject messageItemTemplate;
        public TMP_InputField inputField;
        public Button sendButton;
        public GameObject thinkingIndicator;
        public TMP_Text connectionStatusText;

        [Header("Avatar Sprites")]
        public Sprite aiAvatar;
        public Sprite userAvatar;

        [Header("Names")]
        public string aiName = "AI";
        public string userName = "User";

        [Header("Settings")]
        public int maxMessages = 100;
        public bool autoScroll = true;

        [Header("Session")]
        public SessionPanelController sessionPanel;

        private readonly List<GameObject> _messageObjects = new();
        private DmpChatMessageItem _activeAIBubble;
        private bool _isStreaming;
        private bool _connected;

        private Animator _avatarAnimator;
        private static readonly int IsTalkingHash = Animator.StringToHash("isTalking");

        private string _lastSentMessage;
        private bool _wasNewSession;

        void Start()
        {
            if (messageItemTemplate != null)
                messageItemTemplate.SetActive(false);

            if (dmpClient != null)
            {
                dmpClient.Connect();
                dmpClient.OnConnected += OnDmpConnected;
                dmpClient.OnDisconnected += OnDmpDisconnected;
            }

            if (sessionPanel != null)
            {
                sessionPanel.OnHistoryLoaded += LoadHistory;
                sessionPanel.OnChatCleared += OnNewChatRequested;
            }

            ShowThinking(false);
            UpdateConnectionStatus(false);
            FindAvatar();
        }

        void OnDestroy()
        {
            if (dmpClient != null)
            {
                dmpClient.OnConnected -= OnDmpConnected;
                dmpClient.OnDisconnected -= OnDmpDisconnected;
            }
            if (sessionPanel != null)
            {
                sessionPanel.OnHistoryLoaded -= LoadHistory;
                sessionPanel.OnChatCleared -= OnNewChatRequested;
            }
        }

        // ==== Connection ====

        private void OnDmpConnected()
        {
            _connected = true;
            UpdateConnectionStatus(true);
            SetInputInteractable(true);
        }

        private void OnDmpDisconnected(string reason)
        {
            _connected = false;
            UpdateConnectionStatus(false);
            Debug.Log($"[DMP-Chat] Disconnected: {reason}");
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (connectionStatusText != null)
                connectionStatusText.text = connected ? "Connected" : "Disconnected";
        }

        private void SetInputInteractable(bool interactable)
        {
            if (inputField != null) inputField.interactable = interactable;
            if (sendButton != null) sendButton.interactable = interactable;
        }

        // ==== Send Message ====

        public void OnSendClicked()
        {
            if (_isStreaming || !_connected) return;

            string message = inputField != null ? inputField.text.Trim() : "";
            if (string.IsNullOrEmpty(message)) return;

            if (inputField != null)
            {
                inputField.text = "";
                inputField.ActivateInputField();
            }

            _lastSentMessage = message;
            _wasNewSession = string.IsNullOrEmpty(dmpClient.SessionId);

            AddMessage(message, false, DateTime.Now);

            var aiBubble = AddMessage("...", true, DateTime.Now);
            _activeAIBubble = aiBubble;

            _isStreaming = true;
            ShowThinking(true);
            SetInputInteractable(false);
            SetTalking(true);

            if (ttsPlayer != null) ttsPlayer.Reset();
            if (keyframeBridge != null) keyframeBridge.ResetExpressions();

            dmpClient.SendChat(
                message,
                onPartialToken: (partial) =>
                {
                    if (_activeAIBubble != null)
                        _activeAIBubble.SetChatText(partial);
                    ScrollToBottom();
                },
                onTtsChunk: (chunk) =>
                {
                    if (ttsPlayer != null) ttsPlayer.EnqueueChunk(chunk);
                    if (keyframeBridge != null) keyframeBridge.EnqueueKeyframes(chunk);
                },
                onComplete: () =>
                {
                    _isStreaming = false;
                    _activeAIBubble = null;
                    ShowThinking(false);
                    SetInputInteractable(true);
                    SetTalking(false);

                    if (_wasNewSession && !string.IsNullOrEmpty(dmpClient.SessionId))
                    {
                        sessionPanel?.AddNewSession(dmpClient.SessionId, _lastSentMessage);
                        _wasNewSession = false;
                    }

                    ScrollToBottom();
                }
            );
        }

        // ==== Message Management ====

        private DmpChatMessageItem AddMessage(string content, bool isAI, DateTime time)
        {
            if (messageItemTemplate == null || messageListContent == null) return null;

            var go = Instantiate(messageItemTemplate, messageListContent);
            go.SetActive(true);
            go.name = isAI ? "AIMessage" : "UserMessage";

            var item = go.GetComponent<DmpChatMessageItem>();
            if (item != null)
            {
                item.Initialize(
                    content,
                    isAI,
                    isAI ? aiAvatar : userAvatar,
                    isAI ? aiName : userName,
                    time.ToString("HH:mm:ss")
                );
            }

            _messageObjects.Add(go);
            TrimMessages();

            if (autoScroll)
                StartCoroutine(ScrollToBottomNextFrame());

            return item;
        }

        public void ClearMessages()
        {
            foreach (var go in _messageObjects)
                if (go != null) Destroy(go);
            _messageObjects.Clear();
            _activeAIBubble = null;
        }

        public void LoadHistory(List<ChatMessageData> messages)
        {
            ClearMessages();
            foreach (var msg in messages)
            {
                if (string.IsNullOrWhiteSpace(msg.content)) continue;
                bool isAI = msg.role != "user";
                AddMessage(msg.content, isAI, DateTime.Now);
            }
            StartCoroutine(ScrollToBottomNextFrame());
        }

        private void OnNewChatRequested()
        {
            ClearMessages();
            if (dmpClient != null) dmpClient.SessionId = null;
        }

        private void TrimMessages()
        {
            if (maxMessages <= 0) return;
            while (_messageObjects.Count > maxMessages)
            {
                var go = _messageObjects[0];
                _messageObjects.RemoveAt(0);
                if (go != null) Destroy(go);
            }
        }

        // ==== UI Helpers ====

        public void ShowThinking(bool show)
        {
            if (thinkingIndicator != null)
                thinkingIndicator.SetActive(show);
        }

        private void ScrollToBottom()
        {
            if (chatScrollRect != null)
                StartCoroutine(ScrollToBottomNextFrame());
        }

        private IEnumerator ScrollToBottomNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (chatScrollRect != null)
                chatScrollRect.verticalNormalizedPosition = 0f;
        }

        // ==== Panel Toggle ====

        public void TogglePanel()
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }

        // ==== Avatar ====

        private void FindAvatar()
        {
            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var model = loader.GetCurrentModel();
                if (model != null)
                    _avatarAnimator = model.GetComponentInChildren<Animator>(true);
            }
            if (_avatarAnimator == null)
            {
                var modelParent = GameObject.Find("Model");
                if (modelParent != null)
                    _avatarAnimator = modelParent.GetComponentInChildren<Animator>(true);
            }
        }

        private void SetTalking(bool talking)
        {
            if (_avatarAnimator != null)
                _avatarAnimator.SetBool(IsTalkingHash, talking);
        }
    }
}
