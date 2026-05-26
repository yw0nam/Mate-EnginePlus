using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Hermes
{
    /// <summary>
    /// Test seam for Irodori-TTS synthesis.
    /// </summary>
    public interface IIrodoriClient
    {
        /// <summary>
        /// Synthesizes speech through Irodori-TTS and returns raw WAV bytes, or null on failure.
        /// </summary>
        Task<byte[]> SynthesizeAsync(
            string text,
            string referenceId = null,
            float? seconds = null,
            int? numSteps = null,
            float? cfgScaleText = null,
            float? cfgScaleSpeaker = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Unity-facing HTTP client for the local Irodori-TTS server.
    /// </summary>
    /// <remarks>
    /// Request fields were checked against both the nanobot Python client and the FastAPI server.
    /// No form-field divergence: both use text, reference_audio, seconds, num_steps,
    /// cfg_scale_text, and cfg_scale_speaker. The Python client labels the MP3 upload as
    /// audio/wav, but the server accepts arbitrary upload suffixes; Unity sends audio/mpeg.
    /// </remarks>
    public class IrodoriClient : MonoBehaviour, IIrodoriClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        [Header("Connection")]
        [SerializeField] private string irodoriBaseUrl = "http://localhost:8091";

        [Header("Voices")]
        [SerializeField] private string voicesRootPath = @"D:\codes\waifu\references_voices";
        [SerializeField] private string defaultVoiceId = "七海";

        [Header("Synthesis params")]
        [SerializeField] private float defaultSeconds = 30f;
        [SerializeField] private int defaultNumSteps = 40;
        [SerializeField] private float defaultCfgScaleText = 3.0f;
        [SerializeField] private float defaultCfgScaleSpeaker = 5.0f;

        /// <summary>
        /// Gets the configured Irodori base URL currently used for diagnostics and requests.
        /// </summary>
        public string EffectiveBaseUrl => irodoriBaseUrl;

        /// <summary>
        /// Gets the root directory containing voice reference folders.
        /// Each subfolder name is a voice id; each must contain merged_audio.mp3.
        /// </summary>
        public string VoicesRootPath => voicesRootPath;

        /// <summary>
        /// Gets the default voice id used when no specific voice is selected.
        /// </summary>
        public string DefaultVoiceId => defaultVoiceId;

        /// <summary>
        /// Synthesizes speech through Irodori-TTS and returns raw WAV bytes, or null on failure.
        /// </summary>
        /// <param name="text">Text to synthesize.</param>
        /// <param name="referenceId">Optional voice reference id. When null, the configured default voice is used if non-empty.</param>
        /// <param name="seconds">Optional output duration override in seconds.</param>
        /// <param name="numSteps">Optional diffusion step count override.</param>
        /// <param name="cfgScaleText">Optional text CFG scale override.</param>
        /// <param name="cfgScaleSpeaker">Optional speaker CFG scale override.</param>
        /// <param name="ct">Cancellation token for the HTTP request and response read.</param>
        /// <returns>Raw WAV response bytes on success; otherwise null.</returns>
        public async Task<byte[]> SynthesizeAsync(
            string text,
            string referenceId = null,
            float? seconds = null,
            int? numSteps = null,
            float? cfgScaleText = null,
            float? cfgScaleSpeaker = null,
            CancellationToken ct = default)
        {
            try
            {
                using (MultipartFormDataContent form = new MultipartFormDataContent())
                {
                    form.Add(new StringContent(text ?? string.Empty), "text");
                    form.Add(new StringContent((seconds ?? defaultSeconds).ToString("R", CultureInfo.InvariantCulture)), "seconds");
                    form.Add(new StringContent((numSteps ?? defaultNumSteps).ToString(CultureInfo.InvariantCulture)), "num_steps");
                    form.Add(new StringContent((cfgScaleText ?? defaultCfgScaleText).ToString("R", CultureInfo.InvariantCulture)), "cfg_scale_text");
                    form.Add(new StringContent((cfgScaleSpeaker ?? defaultCfgScaleSpeaker).ToString("R", CultureInfo.InvariantCulture)), "cfg_scale_speaker");

                    string effectiveReferenceId = referenceId ?? defaultVoiceId;
                    if (!string.IsNullOrEmpty(effectiveReferenceId))
                    {
                        string referenceAudioPath = Path.Combine(voicesRootPath, effectiveReferenceId, "merged_audio.mp3");
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
