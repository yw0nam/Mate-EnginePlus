using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MateEngine.Platform
{
    public interface IPlatformScreenCapture
    {
        bool IsSupported { get; }

        IEnumerable<CaptureSource> EnumerateSources();

        Task<Texture2D> CaptureAsync(CaptureSource src);
    }
}
