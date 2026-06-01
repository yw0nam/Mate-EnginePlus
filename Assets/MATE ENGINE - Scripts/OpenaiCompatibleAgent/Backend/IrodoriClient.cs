using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Unity-facing HTTP client for the local Irodori-TTS server.
    /// </summary>
    /// <remarks>
    /// Request fields were checked against both the nanobot Python client and the FastAPI server.
    /// No form-field divergence: both use text, reference_audio, seconds, num_steps,
    /// cfg_scale_text, and cfg_scale_speaker. These tuning values are sourced from serialized
    /// Inspector defaults; callers cannot override them per request. The Python client labels
    /// the MP3 upload as audio/wav, but the server accepts arbitrary upload suffixes; Unity
    /// sends audio/mpeg.
    /// </remarks>
    public class IrodoriClient : MonoBehaviour, ITtsClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        [Header("Connection")]
        [SerializeField] private string irodoriBaseUrl = "http://localhost:8091";

        [Header("Voices")]
        // Empty by default: the runtime fallback chain (env var → serialized →
        // platform defaults) in VoicesRootPath picks the actual path so a scene
        // baked on one OS still works on the other.
        [SerializeField] private string voicesRootPath = string.Empty;
        [SerializeField] private string defaultVoiceId = "七海";

        // Environment variable that always wins over Inspector/serialized state.
        // Useful for swapping voice roots without restarting the Editor.
        private const string VoicesRootEnvVar = "MATE_VOICES_ROOT";

        // Cached resolved path so we only log/scan once per Editor session.
        private string _resolvedVoicesRoot;
        private bool _voicesRootResolved;

        [Header("Synthesis params")]
        [SerializeField] private float defaultSeconds = 30f;
        [SerializeField] private int defaultNumSteps = 40;
        [SerializeField] private float defaultCfgScaleText = 3.0f;
        [SerializeField] private float defaultCfgScaleSpeaker = 5.0f;

        /// <summary>
        /// Gets the configured Irodori base URL currently used for diagnostics and requests.
        /// </summary>
        public string EffectiveBaseUrl => irodoriBaseUrl;

        public string BaseUrl
        {
            get => irodoriBaseUrl;
            set => irodoriBaseUrl = value ?? string.Empty;
        }

        /// <summary>
        /// Replaces the serialized voices root and invalidates the resolution cache so
        /// the next <see cref="VoicesRootPath"/> read re-runs the env→serialized→platform chain.
        /// </summary>
        public void SetVoicesRootPath(string value)
        {
            voicesRootPath = value ?? string.Empty;
            _voicesRootResolved = false;
            _resolvedVoicesRoot = null;
        }

        /// <summary>
        /// Gets the root directory containing voice reference folders. Resolved
        /// once on first access via this chain (first hit wins):
        ///   1. Environment variable <c>MATE_VOICES_ROOT</c>
        ///   2. Serialized <c>voicesRootPath</c> (if it exists and has voice folders)
        ///   3. Platform defaults — macOS: <c>~/Desktop/data/reference_voices/short_references</c>,
        ///      Windows: <c>D:\codes\waifu\references_voices</c>
        ///   4. Original serialized value (returned as-is so callers can log the
        ///      misconfiguration even though nothing under it will load)
        /// Each subfolder name is a voice id; each must contain merged_audio.mp3.
        /// </summary>
        public string VoicesRootPath => ResolveVoicesRoot();

        private string ResolveVoicesRoot()
        {
            if (_voicesRootResolved)
            {
                return _resolvedVoicesRoot;
            }

            string resolved = ResolveVoicesRootUncached();
            _resolvedVoicesRoot = resolved;
            _voicesRootResolved = true;
            Debug.Log($"[Irodori] VoicesRootPath resolved to: '{resolved}' (serialized='{voicesRootPath}')");
            return resolved;
        }

        private string ResolveVoicesRootUncached()
        {
            // 1. Env var override.
            string envValue;
            try { envValue = Environment.GetEnvironmentVariable(VoicesRootEnvVar); }
            catch { envValue = null; }
            if (!string.IsNullOrEmpty(envValue) && HasMergedAudioChild(envValue))
            {
                return envValue;
            }

            // 2. Serialized path (only if it actually contains voices).
            if (!string.IsNullOrEmpty(voicesRootPath) && HasMergedAudioChild(voicesRootPath))
            {
                return voicesRootPath;
            }

            // 3. Platform defaults.
            foreach (string candidate in PlatformFallbackCandidates())
            {
                if (HasMergedAudioChild(candidate))
                {
                    return candidate;
                }
            }

            // 4. Last resort — return the original serialized value so callers can
            // surface the misconfiguration to the user.
            return voicesRootPath ?? string.Empty;
        }

        private static IEnumerable<string> PlatformFallbackCandidates()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, "Desktop", "data", "reference_voices", "short_references");
            yield return Path.Combine(home, "Desktop", "data", "reference_voices");
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            yield return @"D:\codes\waifu\references_voices";
            yield return @"D:\codes\waifu\references_voices\short_references";
#else
            yield break;
#endif
        }

        private static bool HasMergedAudioChild(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            try
            {
                if (!Directory.Exists(root))
                {
                    return false;
                }

                // Voice folder layout: <root>/<voiceId>/merged_audio.mp3.
                foreach (string sub in Directory.EnumerateDirectories(root))
                {
                    if (File.Exists(Path.Combine(sub, "merged_audio.mp3")))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Irodori] HasMergedAudioChild('{root}') threw: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Forces the next call to <see cref="VoicesRootPath"/> to re-run the
        /// resolution chain. Useful after env-var or Inspector changes mid-session.
        /// </summary>
        public void InvalidateVoicesRoot()
        {
            _voicesRootResolved = false;
            _resolvedVoicesRoot = null;
        }

        /// <summary>
        /// Gets the default voice id used when no specific voice is selected.
        /// </summary>
        public string DefaultVoiceId => defaultVoiceId;

        /// <summary>
        /// Synthesizes speech through Irodori-TTS and returns raw WAV bytes, or null on failure.
        /// </summary>
        public async Task<byte[]> SynthesizeAsync(
            string text,
            string referenceId,
            CancellationToken ct)
        {
            try
            {
                using (MultipartFormDataContent form = new MultipartFormDataContent())
                {
                    form.Add(new StringContent(text ?? string.Empty), "text");
                    form.Add(new StringContent(defaultSeconds.ToString("R", CultureInfo.InvariantCulture)), "seconds");
                    form.Add(new StringContent(defaultNumSteps.ToString(CultureInfo.InvariantCulture)), "num_steps");
                    form.Add(new StringContent(defaultCfgScaleText.ToString("R", CultureInfo.InvariantCulture)), "cfg_scale_text");
                    form.Add(new StringContent(defaultCfgScaleSpeaker.ToString("R", CultureInfo.InvariantCulture)), "cfg_scale_speaker");

                    string effectiveReferenceId = referenceId ?? defaultVoiceId;
                    if (!string.IsNullOrEmpty(effectiveReferenceId))
                    {
                        string referenceAudioPath = Path.Combine(VoicesRootPath, effectiveReferenceId, "merged_audio.mp3");
                        using (FileStream referenceStream = File.OpenRead(referenceAudioPath))
                        {
                            StreamContent audioContent = new StreamContent(referenceStream);
                            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                            form.Add(audioContent, "reference_audio", "merged_audio.mp3");

                            return await PostSynthesizeAsync(form, text ?? string.Empty, ct);
                        }
                    }

                    return await PostSynthesizeAsync(form, text ?? string.Empty, ct);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Irodori] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks whether the configured Irodori server responds successfully to GET /health.
        /// </summary>
        /// <param name="ct">Cancellation token for the health-check request.</param>
        /// <returns>True when the server returns HTTP 200; false for any other result or exception.</returns>
        public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
        {
            try
            {
                using (HttpResponseMessage response = await _http.GetAsync(BuildUrl("health"), ct))
                {
                    return response.StatusCode == System.Net.HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<byte[]> PostSynthesizeAsync(MultipartFormDataContent form, string text, CancellationToken ct)
        {
            using (HttpResponseMessage response = await _http.PostAsync(BuildUrl("synthesize"), form, ct))
            {
                // .NET Standard 2.1 (Unity) only exposes the no-arg overload of
                // ReadAsByteArrayAsync. Cancellation is still honoured by the
                // upstream PostAsync above, so the read here is brief and safe.
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[Irodori] HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    return null;
                }

                string preview = text.Substring(0, Math.Min(40, text.Length));
                Debug.Log($"[Irodori] Synthesized {bytes.Length} bytes for: {preview}...");
                return bytes;
            }
        }

        private string BuildUrl(string path)
        {
            return $"{irodoriBaseUrl.TrimEnd('/')}/{path}";
        }
    }
}
