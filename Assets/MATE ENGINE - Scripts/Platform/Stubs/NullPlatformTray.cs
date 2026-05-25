using System.Collections.Generic;
using UnityEngine;

namespace MateEngine.Platform.Stubs
{
    internal sealed class NullPlatformTray : IPlatformTray
    {
        public bool IsSupported => false;
        public void Show(Texture2D icon, string tooltip, IReadOnlyList<TrayMenuItem> menu) { }
        public void Hide() { }
    }
}
