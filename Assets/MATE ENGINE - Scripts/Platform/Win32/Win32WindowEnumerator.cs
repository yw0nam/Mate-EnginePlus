#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace MateEngine.Platform.Win32
{
    // Placeholder. The existing implementation lives in AvatarWindowHandler.cs
    // and will be migrated here per handoff §6 step 1 (caller migration).
    // Until then, this stub returns an empty enumeration. Callers that have not
    // been migrated continue to use their inline P/Invoke and are unaffected.
    internal sealed class Win32WindowEnumerator : IPlatformWindowEnumerator
    {
        public bool IsSupported => false; // not yet migrated
        public IEnumerable<ForeignWindow> EnumerateOnScreen() => Array.Empty<ForeignWindow>();
        public bool IsAbove(IntPtr a, IntPtr b) => false;
    }
}
#endif
