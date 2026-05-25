using UnityEngine;

namespace MateEngine.Platform.Stubs
{
    internal sealed class NullPlatformWindow : IPlatformWindow
    {
        public bool IsSupported => false;
        public void SetTransparent(bool on) { }
        public void SetTopmost(bool on) { }
        public void SetClickThrough(bool on) { }
        public Rect GetWindowRect() => Rect.zero;
        public void SetWindowRect(Rect r) { }
        public void HideFromTaskbar() { }
        public void ShowInTaskbar() { }
    }
}
