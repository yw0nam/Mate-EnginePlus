#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MateEngine.Platform.Win32
{
    // Placeholder. Existing implementation lives in OpenaiCompatibleAgent.ScreenCaptureManager.
    // Migration to this interface happens in a follow-up commit (handoff §6 step 1).
    internal sealed class Win32ScreenCapture : IPlatformScreenCapture
    {
        public bool IsSupported => false; // not yet migrated
        public IEnumerable<CaptureSource> EnumerateSources() => Array.Empty<CaptureSource>();
        public Task<Texture2D> CaptureAsync(CaptureSource src) => Task.FromResult<Texture2D>(null);
    }
}
#endif
