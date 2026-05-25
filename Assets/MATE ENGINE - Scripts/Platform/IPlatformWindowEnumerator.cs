using System;
using System.Collections.Generic;

namespace MateEngine.Platform
{
    public interface IPlatformWindowEnumerator
    {
        bool IsSupported { get; }

        IEnumerable<ForeignWindow> EnumerateOnScreen();

        bool IsAbove(IntPtr a, IntPtr b);
    }
}
