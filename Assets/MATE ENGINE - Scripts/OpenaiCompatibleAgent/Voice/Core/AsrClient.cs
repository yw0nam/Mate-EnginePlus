using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OpenaiCompatibleAgent.Voice
{
    /// <summary>OpenAI-compatible ASR client (vLLM /v1/audio/transcriptions). No auth (local).</summary>
    public sealed class AsrClient
    {
        static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        readonly string _baseUrl;
        readonly string _model;

        public AsrClient(string baseUrl = "http://localhost:5517", string model = "Qwen/Qwen3-ASR-1.7B")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
        }

        public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
        {
            if (wav == null || wav.Length == 0) return string.Empty;

            using (var form = new MultipartFormDataContent())
            {
                var audio = new ByteArrayContent(wav);
                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(audio, "file", "speech.wav");
                form.Add(new StringContent(_model), "model");
                form.Add(new StringContent("json"), "response_format");

                using (var resp = await _http.PostAsync(_baseUrl + "/v1/audio/transcriptions", form, ct))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.LogWarning($"[Voice] ASR HTTP {(int)resp.StatusCode}: {body}");
                        return string.Empty;
                    }
                    return ParseText(body);
                }
            }
        }

        static string ParseText(string json)
        {
            try
            {
                var parsed = JsonUtility.FromJson<TranscriptionResponse>(json);
                return parsed != null && parsed.text != null ? parsed.text.Trim() : string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] ASR parse failed: {e.Message} | body={json}");
                return string.Empty;
            }
        }

        [Serializable]
        class TranscriptionResponse { public string text; }
    }
}
