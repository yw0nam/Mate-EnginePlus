#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using UnityEngine;

namespace MateEngine.Platform.Win32
{
    // Thin Win32 IPlatformWindow stub. UniWindowController already handles
    // transparent/topmost/click-through cross-platform, so direct callers
    // should keep using UniWindowController. This stub exists so future Win-only
    // window features can land here without scattering P/Invoke across the codebase.
    internal sealed class Win32Window : IPlatformWindow
    {
        public bool IsSupported => true;

        public void SetTransparent(bool on) { }
        public void SetTopmost(bool on) { }
        public void SetClickThrough(bool on) { }
        public Rect GetWindowRect() => Rect.zero;
        public void SetWindowRect(Rect r) { }
        public void HideFromTaskbar() => PlatformFactory.AppShell.HideFromTaskbar();
        public void ShowInTaskbar() => PlatformFactory.AppShell.ShowInTaskbar();
    }
}
#endif
