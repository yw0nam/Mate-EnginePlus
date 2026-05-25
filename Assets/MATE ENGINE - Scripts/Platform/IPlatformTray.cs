using System.Collections.Generic;
using UnityEngine;

namespace MateEngine.Platform
{
    public interface IPlatformTray
    {
        bool IsSupported { get; }

        void Show(Texture2D icon, string tooltip, IReadOnlyList<TrayMenuItem> menu);
        void Hide();
    }
}
