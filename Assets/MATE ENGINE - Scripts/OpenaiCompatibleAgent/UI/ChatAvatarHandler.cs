using System.IO;
using UnityEngine;
using UnityEngine.UI;
using SFB;

namespace OpenaiCompatibleAgent
{
    public class ChatAvatarHandler : MonoBehaviour
    {
        [Header("Chat Target")]
        [SerializeField] private DmpChatController chatController;

        [Header("Defaults (used when no custom avatar saved or after Reset)")]
        [SerializeField] private Sprite defaultAiSprite;
        [SerializeField] private Sprite defaultUserSprite;

        [Header("AI Avatar UI")]
        [SerializeField] private Image aiPreviewImage;
        [SerializeField] private Button aiPickButton;
        [SerializeField] private Button aiResetButton;

        [Header("User Avatar UI")]
        [SerializeField] private Image userPreviewImage;
        [SerializeField] private Button userPickButton;
        [SerializeField] private Button userResetButton;

        private const string AvatarDirName = "avatars";
        private const string AiFileName = "ai_avatar.png";
        private const string UserFileName = "user_avatar.png";

        private Sprite _currentAi;
        private Sprite _currentUser;

        private void Awake()
        {
            EnsureAvatarDir();
        }

        private void Start()
        {
            if (aiPreviewImage != null) aiPreviewImage.preserveAspect = true;
            if (userPreviewImage != null) userPreviewImage.preserveAspect = true;

            LoadAndApplyAll();
        }

        // ---- Inspector-exposed (wire these to Button.onClick in the Editor) ----
        public void PickAiAvatar()    => PickAvatar(true);
        public void PickUserAvatar()  => PickAvatar(false);
        public void ResetAiAvatar()   => ResetAvatar(true);
        public void ResetUserAvatar() => ResetAvatar(false);

        public void LoadAndApplyAll()
        {
            _currentAi = LoadSavedOrDefault(true);
            _currentUser = LoadSavedOrDefault(false);

            if (aiPreviewImage != null) aiPreviewImage.sprite = _currentAi;
            if (userPreviewImage != null) userPreviewImage.sprite = _currentUser;

            if (chatController != null)
                chatController.SetAvatarSprites(_currentAi, _currentUser);
        }

        private void PickAvatar(bool isAI)
        {
            string title = isAI ? "Select AI Avatar" : "Select User Avatar";
            var extensions = new[] { new ExtensionFilter("Image Files", "png", "jpg", "jpeg") };
            string[] paths = StandaloneFileBrowser.OpenFilePanel(title, "", extensions, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            string src = paths[0];
            if (!File.Exists(src))
            {
                Debug.LogWarning("[ChatAvatar] picked file does not exist: " + src);
                return;
            }

            string targetName = isAI ? AiFileName : UserFileName;
            string targetPath = Path.Combine(AvatarDirPath(), targetName);

            try
            {
                EnsureAvatarDir();
                File.Copy(src, targetPath, overwrite: true);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[ChatAvatar] copy failed: " + ex.Message);
                return;
            }

            if (SaveLoadHandler.Instance != null)
            {
                if (isAI) SaveLoadHandler.Instance.data.aiAvatarPath = targetName;
                else SaveLoadHandler.Instance.data.userAvatarPath = targetName;
                SaveLoadHandler.Instance.SaveToDisk();
            }

            Sprite loaded = LoadSpriteFromFile(targetPath);
            if (loaded == null)
            {
                Debug.LogWarning("[ChatAvatar] failed to load copied file as sprite: " + targetPath);
                return;
            }

            if (isAI) _currentAi = loaded; else _currentUser = loaded;
            ApplyToPreviewAndChat(isAI);
        }

        private void ResetAvatar(bool isAI)
        {
            string targetName = isAI ? AiFileName : UserFileName;
            string targetPath = Path.Combine(AvatarDirPath(), targetName);
            try { if (File.Exists(targetPath)) File.Delete(targetPath); }
            catch (System.Exception ex) { Debug.LogWarning("[ChatAvatar] delete failed: " + ex.Message); }

            if (SaveLoadHandler.Instance != null)
            {
                if (isAI) SaveLoadHandler.Instance.data.aiAvatarPath = "";
                else SaveLoadHandler.Instance.data.userAvatarPath = "";
                SaveLoadHandler.Instance.SaveToDisk();
            }

            if (isAI) _currentAi = defaultAiSprite; else _currentUser = defaultUserSprite;
            ApplyToPreviewAndChat(isAI);
        }

        private void ApplyToPreviewAndChat(bool isAI)
        {
            if (isAI && aiPreviewImage != null) aiPreviewImage.sprite = _currentAi;
            if (!isAI && userPreviewImage != null) userPreviewImage.sprite = _currentUser;

            if (chatController != null)
                chatController.SetAvatarSprites(isAI ? _currentAi : null, isAI ? null : _currentUser);
        }

        private Sprite LoadSavedOrDefault(bool isAI)
        {
            string savedName = "";
            if (SaveLoadHandler.Instance != null)
                savedName = isAI ? SaveLoadHandler.Instance.data.aiAvatarPath
                                 : SaveLoadHandler.Instance.data.userAvatarPath;

            if (!string.IsNullOrEmpty(savedName))
            {
                string p = Path.Combine(AvatarDirPath(), savedName);
                if (File.Exists(p))
                {
                    var s = LoadSpriteFromFile(p);
                    if (s != null) return s;
                }
            }
            return isAI ? defaultAiSprite : defaultUserSprite;
        }

        private static Sprite LoadSpriteFromFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (!tex.LoadImage(bytes)) return null;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ChatAvatar] LoadSpriteFromFile failed: " + ex.Message);
                return null;
            }
        }

        private static string AvatarDirPath()
        {
            return Path.Combine(Application.persistentDataPath, AvatarDirName);
        }

        private static void EnsureAvatarDir()
        {
            string dir = AvatarDirPath();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
