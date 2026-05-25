#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace MateEngine.Platform.Win32
{
    // Placeholder. Existing implementation lives in Utils.TrayIcon. Caller
    // migration to this interface happens in a follow-up commit (handoff §6 step 1).
    internal sealed class Win32Tray : IPlatformTray
    {
        public bool IsSupported => false; // not yet migrated
        public void Show(Texture2D icon, string tooltip, IReadOnlyList<TrayMenuItem> menu) { }
        public void Hide() { }
    }
}
#endif
