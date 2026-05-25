#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using UnityEngine;

namespace MateEngine.Platform.Win32
{
    internal static class Win32PlatformBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PlatformFactory.RegisterAppShell(new Win32AppShell());
            PlatformFactory.RegisterWindow(new Win32Window());
            PlatformFactory.RegisterWindowEnumerator(new Win32WindowEnumerator());
            PlatformFactory.RegisterTray(new Win32Tray());
            PlatformFactory.RegisterScreenCapture(new Win32ScreenCapture());
        }
    }
}
#endif
