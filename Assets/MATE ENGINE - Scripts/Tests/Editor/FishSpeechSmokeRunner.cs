using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenaiCompatibleAgent;
using UnityEditor;
using UnityEngine;

namespace OpenaiCompatibleAgent.Tests
{
    /// <summary>
    /// Manual smoke test against the live Fish-Speech-S2-Pro server on :8092.
    /// Invoke via the Unity Editor menu, or:
    ///   unity-cli exec "EditorApplication.ExecuteMenuItem(\"Tools/Hermes/Smoke - FishSpeech Synthesize\");"
    /// Results go to Debug.Log with the [Smoke] prefix.
    /// </summary>
    public static class FishSpeechSmokeRunner
    {
        [MenuItem("Tools/Hermes/Smoke - FishSpeech Synthesize")]
        public static async void FishSpeechSmoke()
        {
            Debug.Log("[Smoke] FishSpeech starting...");
            GameObject go = null;
            try
            {
                go = new GameObject("__FishSpeechSmoke");
                var client = go.AddComponent<FishSpeechClient>();
                await Task.Yield();

                bool ok = await client.HealthCheckAsync();
                Debug.Log("[Smoke] FishSpeech health: " + ok);
                if (!ok)
                {
                    Debug.LogError("[Smoke] FishSpeech health check failed - server down?");
                    return;
                }

                byte[] bytes = await client.SynthesizeAsync("こんにちは。今日はいい天気ですね。", "七海", CancellationToken.None);
                int len = bytes == null ? -1 : bytes.Length;
                Debug.Log("[Smoke] FishSpeech synthesize: bytes=" + len);

                bool isWav = bytes != null && bytes.Length > 44
                    && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F';
                if (isWav)
                {
                    string path = Path.Combine(Path.GetTempPath(), "fishspeech_smoke.wav");
                    File.WriteAllBytes(path, bytes);
                    Debug.Log("[Smoke] Wrote WAV: " + path);
                }
                else
                {
                    Debug.LogError("[Smoke] FishSpeech returned null / too short / not a RIFF/WAVE stream");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Smoke] FishSpeech exception: " + e);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log("[Smoke] FishSpeech done.");
            }
        }
    }
}
