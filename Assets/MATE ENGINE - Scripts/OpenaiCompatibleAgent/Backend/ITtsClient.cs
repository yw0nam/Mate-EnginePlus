using System.Threading;
using System.Threading.Tasks;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Provider-neutral TTS synthesis seam. Implementations turn text into WAV bytes.
    /// </summary>
    public interface ITtsClient
    {
        /// <summary>
        /// Synthesizes <paramref name="text"/> in the voice identified by
        /// <paramref name="referenceId"/> (provider-specific id; null/empty selects the
        /// implementation's default voice). Returns raw WAV bytes, or null on failure.
        /// </summary>
        Task<byte[]> SynthesizeAsync(string text, string referenceId, CancellationToken ct);
    }
}
