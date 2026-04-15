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

        [Header("Screenshot")]
        public Button screenshotButton;
        public ScreenCapturePanelController capturePanel;
        public ScreenCaptureChip captureChip;

        private ScreenCaptureSource _pendingCapture;

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

            if (capturePanel != null)
                capturePanel.OnSourceSelected += OnCaptureSourceSelected;

            if (captureChip != null)
                captureChip.OnChipCancelled += OnCaptureCancelled;

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
            if (capturePanel != null)
                capturePanel.OnSourceSelected -= OnCaptureSourceSelected;

            if (captureChip != null)
                captureChip.OnChipCancelled -= OnCaptureCancelled;
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

        private void UpdateConnectionStatus(bool connected, string overrideMsg = null)
        {
            if (connectionStatusText == null) return;
            if (overrideMsg != null)
            {
                connectionStatusText.text = overrideMsg;
                // 3초 후 정상 상태로 복귀
                CancelInvoke(nameof(RestoreConnectionStatus));
                Invoke(nameof(RestoreConnectionStatus), 3f);
            }
            else
            {
                connectionStatusText.text = connected ? "Connected" : "Disconnected";
            }
        }

        private void RestoreConnectionStatus()
        {
            UpdateConnectionStatus(_connected);
        }

        private void SetInputInteractable(bool interactable)
        {
            if (inputField != null) inputField.interactable = interactable;
            if (sendButton != null) sendButton.interactable = interactable;
            if (screenshotButton != null) screenshotButton.interactable = interactable;
        }

        // ==== Send Message ====

        public async void OnSendClicked()
        {
            try
            {
                await OnSendClickedCore();
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP-Chat] Send error: {e}");
                _isStreaming = false;
                _activeAIBubble = null;
                ShowThinking(false);
                SetInputInteractable(true);
                SetTalking(false);
                _pendingCapture = null;
                captureChip?.Hide();
                SetScreenshotButtonArmed(false);
            }
        }

        private async Awaitable OnSendClickedCore()
        {
            if (_isStreaming || !_connected) return;

            string message = inputField != null ? inputField.text.Trim() : "";
            if (string.IsNullOrEmpty(message)) return;

            if (inputField != null)
            {
                inputField.text = "";
                inputField.ActivateInputField();
            }

            // 캡처 대상이 있으면 Send 시점에 스크린샷 실행
            string[] captureImages = null;
            if (_pendingCapture != null)
            {
                var tex = await ScreenCaptureManager.CaptureAsync(_pendingCapture);
                if (tex != null)
                {
                    string b64 = ScreenCaptureManager.ToBase64PNG(tex);
                    if (b64 != null)
                        captureImages = new[] { b64 };
                    else
                        UpdateConnectionStatus(_connected, "캡처 실패: 이미지 크기 초과");
                    UnityEngine.Object.Destroy(tex);
                }
                else
                {
                    UpdateConnectionStatus(_connected, "캡처 실패");
                }
                // 캡처 후 칩/버튼 리셋 (성공 여부 무관)
                _pendingCapture = null;
                captureChip?.Hide();
                SetScreenshotButtonArmed(false);
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
                },
                images: captureImages
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

        // ==== Screenshot ====

        private void OnCaptureSourceSelected(ScreenCaptureSource src)
        {
            _pendingCapture = src;
            captureChip?.Show(src.DisplayName);
            SetScreenshotButtonArmed(true);
        }

        private void OnCaptureCancelled()
        {
            _pendingCapture = null;
            captureChip?.Hide();
            SetScreenshotButtonArmed(false);
        }

        private void SetScreenshotButtonArmed(bool armed)
        {
            if (screenshotButton == null) return;
            var colors = screenshotButton.colors;
            colors.normalColor = armed
                ? new Color(0.48f, 0.3f, 1f, 1f)   // 보라색 활성
                : new Color(0.16f, 0.16f, 0.22f, 1f); // 기본 회색
            screenshotButton.colors = colors;
        }
    }
}
