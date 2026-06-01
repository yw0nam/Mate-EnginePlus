using System;
using System.IO;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenaiCompatibleAgent;
using UnityEditor;
using UnityEngine;

namespace OpenaiCompatibleAgent.Tests
{
    /// <summary>
    /// Editor menu entries for Phase A2/A5 manual smoke tests against the live
    /// hermes-agent and Irodori-TTS servers. Invoke via Unity Editor menu, OR
    /// via unity-cli exec: EditorApplication.ExecuteMenuItem("Tools/Hermes/Smoke - Irodori Synthesize").
    /// Results are written to UnityEngine.Debug.Log with the [Smoke] prefix so
    /// they show up cleanly in unity-cli console --type log.
    /// </summary>
    public static class HermesSmokeRunner
    {
        [MenuItem("Tools/Hermes/Smoke - Irodori Synthesize")]
        public static async void IrodoriSmoke()
        {
            Debug.Log("[Smoke] Irodori starting...");
            GameObject go = null;
            try
            {
                go = new GameObject("__IrodoriSmoke");
                var client = go.AddComponent<IrodoriClient>();
                await Task.Yield();

                bool ok = await client.HealthCheckAsync();
                Debug.Log("[Smoke] Irodori health: " + ok);
                if (!ok)
                {
                    Debug.LogError("[Smoke] Irodori health check failed - server down?");
                    return;
                }

                byte[] bytes = await client.SynthesizeAsync("こんにちは。今日はいい天気ですね。", null, CancellationToken.None);
                int len = bytes == null ? -1 : bytes.Length;
                Debug.Log("[Smoke] Irodori synthesize: bytes=" + len);

                if (bytes != null && bytes.Length > 100)
                {
                    string path = Path.Combine(Path.GetTempPath(), "irodori_smoke.wav");
                    File.WriteAllBytes(path, bytes);
                    Debug.Log("[Smoke] Wrote WAV: " + path);
                }
                else
                {
                    Debug.LogError("[Smoke] Irodori returned null or too-short bytes");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Smoke] Irodori exception: " + e);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log("[Smoke] Irodori done.");
            }
        }

        [MenuItem("Tools/Hermes/Smoke - Hermes NonStreaming Chat")]
        public static async void HermesNonStreamingSmoke()
        {
            Debug.Log("[Smoke] Hermes non-streaming starting...");
            GameObject go = null;
            try
            {
                go = new GameObject("__HermesNonStreamingSmoke");
                var client = go.AddComponent<HermesResponseClient>();
                await Task.Yield();

                var task = client.SendNonStreamingAsync("Say 'hello' in one short sentence.");
                var winner = await Task.WhenAny(task, Task.Delay(45000));
                if (winner == task)
                {
                    Debug.Log("[Smoke] Hermes non-stream result text=" + task.Result + " lastId=" + (client.LastResponseId ?? "<null>"));
                }
                else
                {
                    Debug.LogError("[Smoke] Hermes non-stream TIMEOUT after 45s");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Smoke] Hermes non-stream exception: " + e);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log("[Smoke] Hermes non-stream done.");
            }
        }

        [MenuItem("Tools/Hermes/Smoke - Hermes Streaming Chat")]
        public static async void HermesSmoke()
        {
            Debug.Log("[Smoke] Hermes starting...");
            GameObject go = null;
            try
            {
                go = new GameObject("__HermesSmoke");
                var client = go.AddComponent<HermesResponseClient>();
                await Task.Yield();

                var sb = new StringBuilder();
                int deltaCount = 0;
                var tcs = new TaskCompletionSource<string>();

                var sendTask = client.SendAsync(
                    "Say 'hello' in Japanese with one short polite sentence.",
                    delta => { sb.Append(delta); deltaCount++; },
                    () => tcs.TrySetResult("done"),
                    msg => tcs.TrySetResult("error: " + msg)
                );

                // Edit-mode polling loop: MonoBehaviour.Update isn't reliably ticked while
                // await Task.Delay holds the main thread, so we pump the callback queue
                // ourselves between yields. This is Editor-smoke specific - Play mode and
                // production runtime rely on the normal Update() drain.
                double start = EditorApplication.timeSinceStartup;
                while (!tcs.Task.IsCompleted && (EditorApplication.timeSinceStartup - start) < 45.0)
                {
                    client.PumpMainThreadQueue();
                    await Task.Yield();
                }
                client.PumpMainThreadQueue();

                string result = tcs.Task.IsCompleted ? tcs.Task.Result : "timeout";

                Debug.Log("[Smoke] Hermes result=" + result + " deltas=" + deltaCount + " lastId=" + (client.LastResponseId ?? "<null>"));
                Debug.Log("[Smoke] Hermes text: " + sb.ToString());

                // Wait for the underlying SDK task to finish so we don't leave it dangling.
                if (!sendTask.IsCompleted)
                {
                    var sendWinner = await Task.WhenAny(sendTask, Task.Delay(5000));
                    if (sendWinner != sendTask)
                    {
                        Debug.LogWarning("[Smoke] SendAsync did not unwind within 5s after smoke loop exit.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Smoke] Hermes exception: " + e);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log("[Smoke] Hermes done.");
            }
        }

        [MenuItem("Test/HermesOrchestratorE2E")]
        public static async void HermesOrchestratorE2E()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[E2E] Enter PlayMode first. Press the Play button before clicking this menu.");
                return;
            }

            var orchestrator = UnityEngine.Object.FindFirstObjectByType<StreamingOrchestrator>(FindObjectsInactive.Include);
            if (orchestrator == null)
            {
                Debug.LogError("[E2E] StreamingOrchestrator not found in scene. Wire it on a GameObject in Mate Engine Main first.");
                return;
            }

            // Read LastResponseId through the orchestrator's pass-through property so we
            // always observe the same HermesResponseClient instance the orchestrator actually
            // streamed through (avoids FindFirstObjectByType picking a stale/transient instance).
            var ttsAudioPlayer = FindFirstObjectByTypeName("OpenaiCompatibleAgent.TtsAudioPlayer");

            int audioClips = 0;
            Delegate wavChunkStartedHandler = null;

            if (ttsAudioPlayer == null)
            {
                Debug.LogWarning("[E2E] TtsAudioPlayer not found; audioClips=N/A (event not exposed).");
            }
            else
            {
                Action incrementAudioClips = () => audioClips++;
                wavChunkStartedHandler = AddCounterEventHandler(ttsAudioPlayer, "OnWavChunkStarted", incrementAudioClips);
            }

            try
            {
                var turn1 = await RunOrchestratorE2ETurn(
                    orchestrator,
                    "こんにちは。今日はいい天気ですね。",
                    () => audioClips);
                string firstId = orchestrator.LastResponseId ?? string.Empty;
                LogE2ETurn(1, turn1.deltas, turn1.text, turn1.audioClips, firstId, ttsAudioPlayer != null);

                var turn2 = await RunOrchestratorE2ETurn(
                    orchestrator,
                    "今、私が何と言いましたか?",
                    () => audioClips);
                string secondId = orchestrator.LastResponseId ?? string.Empty;
                LogE2ETurn(2, turn2.deltas, turn2.text, turn2.audioClips, secondId, ttsAudioPlayer != null);

                bool idsChanged = !string.IsNullOrEmpty(firstId) && !string.IsNullOrEmpty(secondId) && firstId != secondId;
                bool deltasOk = turn1.deltas > 0 && turn2.deltas > 0;
                bool audioOk = ttsAudioPlayer == null || (turn1.audioClips > 0 && turn2.audioClips > 0);

                if (idsChanged && deltasOk && audioOk)
                {
                    Debug.Log("[E2E] PASS");
                }
                else
                {
                    Debug.LogError($"[E2E] FAIL reason: idsChanged={idsChanged} deltasOk={deltasOk} audioOk={audioOk}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[E2E] FAIL reason: exception=" + e);
            }
            finally
            {
                if (ttsAudioPlayer != null)
                {
                    RemoveEventHandler(ttsAudioPlayer, "OnWavChunkStarted", wavChunkStartedHandler);
                }
            }
        }

        private static UnityEngine.Object FindFirstObjectByTypeName(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName);
                if (type == null || !typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    continue;
                }

                UnityEngine.Object[] objects = UnityEngine.Object.FindObjectsByType(
                    type,
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                return objects.Length > 0 ? objects[0] : null;
            }

            return null;
        }

        private static Delegate AddCounterEventHandler(UnityEngine.Object target, string eventName, Action increment)
        {
            var eventInfo = target.GetType().GetEvent(eventName);
            if (eventInfo == null)
            {
                return null;
            }

            var invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
            var parameters = invoke.GetParameters();
            var expressions = new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                expressions[i] = Expression.Parameter(parameters[i].ParameterType, parameters[i].Name);
            }

            Delegate handler = Expression.Lambda(eventInfo.EventHandlerType, Expression.Invoke(Expression.Constant(increment)), expressions).Compile();
            eventInfo.AddEventHandler(target, handler);
            return handler;
        }

        private static void RemoveEventHandler(UnityEngine.Object target, string eventName, Delegate handler)
        {
            if (handler == null)
            {
                return;
            }

            var eventInfo = target.GetType().GetEvent(eventName);
            eventInfo?.RemoveEventHandler(target, handler);
        }

        private static async Task<(int deltas, string text, int audioClips)> RunOrchestratorE2ETurn(
            StreamingOrchestrator orchestrator,
            string prompt,
            Func<int> readAudioClips)
        {
            var sb = new StringBuilder();
            int deltas = 0;
            int audioBaseline = readAudioClips();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var turnComplete = new TaskCompletionSource<bool>();
            Task sendTask = orchestrator.SendAsync(
                prompt,
                onTokenDelta: token =>
                {
                    sb.Append(token);
                    deltas++;
                },
                onTurnComplete: () => turnComplete.TrySetResult(true),
                onError: error =>
                {
                    Debug.LogError($"[E2E] orchestrator error: {error}");
                    turnComplete.TrySetResult(false);
                },
                ct: cts.Token);

            Task completed = await Task.WhenAny(sendTask, turnComplete.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != sendTask && completed != turnComplete.Task)
            {
                Debug.LogError("[E2E] Timed out after 60s waiting for onTurnComplete.");
                cts.Cancel();
            }

            try
            {
                await sendTask;
            }
            catch (OperationCanceledException)
            {
                Debug.LogError("[E2E] SendAsync cancelled after timeout.");
            }

            return (deltas, sb.ToString(), readAudioClips() - audioBaseline);
        }

        private static void LogE2ETurn(int turn, int deltas, string text, int audioClips, string lastResponseId, bool audioEventExposed)
        {
            string audio = audioEventExposed ? audioClips.ToString() : "N/A (event not exposed)";
            string id = string.IsNullOrEmpty(lastResponseId) ? "(null)" : lastResponseId;
            Debug.Log($"[E2E] Turn {turn}: deltas={deltas} bytes={text.Length} audioClips={audio} lastId={id}");
            Debug.Log($"[E2E] Turn {turn} text: {text}");
        }
    }
}
