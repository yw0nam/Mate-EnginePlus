using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace MateEngine.EditorTools
{
    /// <summary>
    /// On macOS Editor, Console "Error Pause" turns Play Mode into a frozen
    /// state because this project's Win32 P/Invokes (user32.dll, kernel32.dll)
    /// throw DllNotFoundException every frame. The frozen player loop stalls
    /// com.utilities.async coroutines, which in turn hangs com.openai.unity's
    /// <c>await Awaiters.UnityMainThread</c> forever (see handoff_hermes_sdk_hang.md).
    /// This forces Error Pause off on Editor load on non-Windows hosts.
    /// </summary>
    [InitializeOnLoad]
    internal static class MacOSEditorAutoConfigure
    {
        static MacOSEditorAutoConfigure()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;

            EditorApplication.delayCall += DisableConsoleErrorPause;
        }

        private static void DisableConsoleErrorPause()
        {
            try
            {
                var consoleType = typeof(EditorWindow).Assembly
                    .GetType("UnityEditor.ConsoleWindow");
                if (consoleType == null) return;

                var setM = consoleType.GetMethod(
                    "SetConsoleErrorPause",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (setM == null) return;

                setM.Invoke(null, new object[] { false });
                Debug.Log("[MacOSEditorAutoConfigure] Console Error Pause disabled " +
                          "(prevents Play Mode freeze from Win32 P/Invoke exceptions).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MacOSEditorAutoConfigure] Failed to disable Error Pause: " + e.Message);
            }
        }
    }
}
