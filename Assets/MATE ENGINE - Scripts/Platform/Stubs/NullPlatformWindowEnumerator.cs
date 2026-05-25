using System;
using System.Collections.Generic;

namespace MateEngine.Platform.Stubs
{
    internal sealed class NullPlatformWindowEnumerator : IPlatformWindowEnumerator
    {
        public bool IsSupported => false;
        public IEnumerable<ForeignWindow> EnumerateOnScreen() => System.Array.Empty<ForeignWindow>();
        public bool IsAbove(IntPtr a, IntPtr b) => false;
    }
}
