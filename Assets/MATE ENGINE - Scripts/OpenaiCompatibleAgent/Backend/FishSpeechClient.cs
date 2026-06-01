using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Unity-facing client for a vLLM-omni Fish-Speech-S2-Pro server exposing the
    /// OpenAI-compatible POST /v1/audio/speech endpoint. Voice ids are server-registered
    /// preset names, identical to Irodori's voice ids in this deployment.
    /// </summary>
    public class FishSpeechClient : MonoBehaviour, ITtsClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        [Header("Connection")]
        [SerializeField] private string baseUrl = "http://localhost:8092";
        [SerializeField] private string modelId = "fishaudio/s2-pro";

        [Header("Voice")]
        [SerializeField] private string defaultVoiceId = "七海";

        [Header("Synthesis")]
        [SerializeField] private string responseFormat = "wav";
        // Optional language hint ("Japanese", "Auto", ...). Sent only when non-empty.
        [SerializeField] private string language = string.Empty;

        public string BaseUrl
        {
            get => baseUrl;
            set => baseUrl = value ?? string.Empty;
        }

        public string DefaultVoiceId => defaultVoiceId;

        /// <summary>Serializable request body for POST /v1/audio/speech.</summary>
        public sealed class SpeechRequest
        {
            [JsonProperty("model")] public string Model { get; set; }
            [JsonProperty("input")] public string Input { get; set; }
            [JsonProperty("voice")] public string Voice { get; set; }
            [JsonProperty("response_format")] public string ResponseFormat { get; set; }
            [JsonProperty("language", NullValueHandling = NullValueHandling.Ignore)] public string Language { get; set; }
        }

        /// <summary>
        /// Pure mapping from synthesis params to the request body. Voice falls back to
        /// <paramref name="defaultVoiceId"/> when <paramref name="referenceId"/> is null/empty;
        /// response_format defaults to "wav"; language is omitted (null) when blank.
        /// </summary>
        public static SpeechRequest BuildRequest(
            string modelId, string text, string referenceId, string defaultVoiceId,
            string responseFormat, string language)
        {
            string voice = string.IsNullOrEmpty(referenceId) ? defaultVoiceId : referenceId;
            return new SpeechRequest
            {
                Model = modelId,
                Input = text ?? string.Empty,
                Voice = voice,
                ResponseFormat = string.IsNullOrEmpty(responseFormat) ? "wav" : responseFormat,
                Language = string.IsNullOrEmpty(language) ? null : language,
            };
        }

        public async Task<byte[]> SynthesizeAsync(string text, string referenceId, CancellationToken ct)
        {
            try
            {
                SpeechRequest request = BuildRequest(modelId, text, referenceId, defaultVoiceId, responseFormat, language);
                string json = JsonConvert.SerializeObject(request);
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _http.PostAsync(BuildUrl("v1/audio/speech"), content, ct))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync();
                        Debug.LogError($"[FishSpeech] HTTP {(int)response.StatusCode}: {errorBody}");
                        return null;
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    bool truncated = request.Input.Length > 40;
                    string preview = request.Input.Substring(0, Math.Min(40, request.Input.Length));
                    Debug.Log($"[FishSpeech] Synthesized {bytes.Length} bytes for: {preview}{(truncated ? "..." : "")}");
                    return bytes;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FishSpeech] {ex.Message}");
                return null;
            }
        }

        /// <summary>Returns true when the server answers GET /health with HTTP 200.</summary>
        public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
        {
            try
            {
                using (HttpResponseMessage response = await _http.GetAsync(BuildUrl("health"), ct))
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private string BuildUrl(string path)
        {
            return $"{baseUrl.TrimEnd('/')}/{path}";
        }
    }
}
