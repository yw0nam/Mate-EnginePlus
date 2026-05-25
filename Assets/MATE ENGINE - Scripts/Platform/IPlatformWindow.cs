using UnityEngine;

namespace MateEngine.Platform
{
    public interface IPlatformWindow
    {
        bool IsSupported { get; }

        void SetTransparent(bool on);
        void SetTopmost(bool on);
        void SetClickThrough(bool on);

        Rect GetWindowRect();
        void SetWindowRect(Rect r);

        void HideFromTaskbar();
        void ShowInTaskbar();
    }
}
