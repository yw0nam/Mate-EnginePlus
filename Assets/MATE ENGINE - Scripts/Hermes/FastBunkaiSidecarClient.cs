using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hermes
{
    /// <summary>
    /// Test seam for sentence-boundary detection through a fast-bunkai sidecar.
    /// </summary>
    public interface IFastBunkaiSidecarClient
    {
        /// <summary>
        /// Finds sentence-ending positions in the supplied text.
        /// </summary>
        /// <param name="text">Text to analyze for sentence boundaries.</param>
        /// <param name="ct">Cancellation token for the HTTP request.</param>
        /// <returns>UTF-16 end positions returned by the sidecar, or an empty array on failure.</returns>
        Task<int[]> FindEosAsync(string text, CancellationToken ct = default);
    }

    /// <summary>
    /// HTTP client for the local Irodori-TTS /eos endpoint backed by fast-bunkai.
    /// </summary>
    public class FastBunkaiSidecarClient : IFastBunkaiSidecarClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly string _baseUrl;

        /// <summary>
        /// Initializes a new fast-bunkai sidecar client.
        /// </summary>
        /// <param name="baseUrl">Base URL of the Irodori-TTS server exposing /eos.</param>
        public FastBunkaiSidecarClient(string baseUrl = "http://localhost:8091")
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8091" : baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Posts text to /eos and returns the sidecar's sentence-ending positions.
        /// </summary>
        /// <param name="text">Text to analyze. Empty or whitespace-only text returns no positions without making an HTTP call.</param>
        /// <param name="ct">Cancellation token for the HTTP request.</param>
        /// <returns>The positions array from the sidecar response, or an empty array on HTTP/network failure.</returns>
        public async Task<int[]> FindEosAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new int[0];
            }

            try
            {
                string json = JsonConvert.SerializeObject(new EosRequest { text = text });
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _http.PostAsync(BuildUrl("eos"), content, ct))
                {
                    string responseText = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogError($"[FastBunkai] HTTP {(int)response.StatusCode}: {response.ReasonPhrase} {responseText}");
                        return new int[0];
                    }

                    JObject root = JObject.Parse(responseText);
                    JToken positionsToken = root["positions"];
                    return positionsToken != null ? positionsToken.ToObject<int[]>() ?? new int[0] : new int[0];
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FastBunkai] {ex.Message}");
                return new int[0];
            }
        }

        private string BuildUrl(string path)
        {
            return $"{_baseUrl}/{path}";
        }

        private sealed class EosRequest
        {
            public string text;
        }
    }
}
