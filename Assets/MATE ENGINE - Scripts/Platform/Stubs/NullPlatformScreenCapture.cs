using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MateEngine.Platform.Stubs
{
    internal sealed class NullPlatformScreenCapture : IPlatformScreenCapture
    {
        public bool IsSupported => false;
        public IEnumerable<CaptureSource> EnumerateSources() => System.Array.Empty<CaptureSource>();
        public Task<Texture2D> CaptureAsync(CaptureSource src) => Task.FromResult<Texture2D>(null);
    }
}
