using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hermes
{
    /// <summary>
    /// Buffers streaming text deltas and emits complete sentence chunks using fast-bunkai positions.
    /// </summary>
    public class SentenceChunker
    {
        private static readonly HashSet<char> _sentenceEnders = new HashSet<char> { '。', '！', '？', '.', '!', '?', '\n' };
        private static readonly Regex _toolCallPattern = new Regex(@"\{\s*\'type\'\s*:\s*\'tool_call\'[\s\S]*?\}\}", RegexOptions.Compiled);

        private readonly IFastBunkaiSidecarClient _sidecar;
        private readonly int _minChunkLength;
        private readonly string _reasoningStartTag;
        private readonly string _reasoningEndTag;
        private readonly StringBuilder _buffer = new StringBuilder();

        private bool _inReasoning;
        private string _partialTag = string.Empty;

        /// <summary>
        /// Initializes a sentence chunker with a sidecar client and chunking options.
        /// </summary>
        /// <param name="sidecar">Sidecar client used to detect sentence-boundary positions.</param>
        /// <param name="minChunkLength">Minimum UTF-16 length required before a sentence prefix is emitted.</param>
        /// <param name="reasoningStartTag">Case-insensitive tag that starts a hidden reasoning block.</param>
        /// <param name="reasoningEndTag">Case-insensitive tag that ends a hidden reasoning block.</param>
        public SentenceChunker(
            IFastBunkaiSidecarClient sidecar,
            int minChunkLength = 50,
            string reasoningStartTag = "<think>",
            string reasoningEndTag = "</think>")
        {
            _sidecar = sidecar;
            _minChunkLength = minChunkLength;
            _reasoningStartTag = reasoningStartTag ?? string.Empty;
            _reasoningEndTag = reasoningEndTag ?? string.Empty;
        }

        /// <summary>
        /// Consumes a streaming delta and returns any complete sentence chunks that are ready to emit.
        /// </summary>
        /// <param name="delta">New text delta to append after reasoning/tool-call filtering.</param>
        /// <param name="ct">Cancellation token for sidecar calls.</param>
        /// <returns>Zero or more emitted sentence chunks.</returns>
        public async Task<List<string>> FeedAsync(string delta, CancellationToken ct = default)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(delta))
            {
                return result;
            }

            string filtered = FilterReasoningStream(delta);
            if (filtered.Length > 0)
            {
                _buffer.Append(filtered);
                ApplyToolCallFilter();
            }

            if (_buffer.Length == 0 || !ContainsSentenceEnder(_buffer) || !LastNonWhitespaceIsSentenceEnder())
            {
                return result;
            }

            while (true)
            {
                int[] positions = await _sidecar.FindEosAsync(_buffer.ToString(), ct);
                List<int> realPositions = FilterRealPositions(positions);
                if (realPositions.Count == 0)
                {
                    break;
                }

                bool emitted = false;
                for (int i = 0; i < realPositions.Count; i++)
                {
                    int pos = realPositions[i];
                    string segment = _buffer.ToString(0, pos).Trim();
                    if (segment.Length >= _minChunkLength)
                    {
                        result.Add(segment);
                        _buffer.Remove(0, pos);
                        ApplyToolCallFilter();
                        emitted = true;
                        break;
                    }
                }

                if (!emitted || _buffer.Length == 0 || !LastNonWhitespaceIsSentenceEnder())
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the remaining buffered text regardless of length and clears all chunker state.
        /// </summary>
        /// <returns>The trimmed remaining buffer, or null when no text remains.</returns>
        public string Flush()
        {
            if (_partialTag.Length > 0 && !_inReasoning)
            {
                _buffer.Append(_partialTag);
            }

            ApplyToolCallFilter();
            string remaining = _buffer.ToString().Trim();
            Reset();
            return remaining.Length == 0 ? null : remaining;
        }

        private string FilterReasoningStream(string chunk)
        {
            string input = _partialTag + chunk;
            _partialTag = string.Empty;

            StringBuilder output = new StringBuilder();
            int index = 0;
            while (index < input.Length)
            {
                if (MatchesTagAt(input, index, _reasoningStartTag))
                {
                    _inReasoning = true;
                    index += _reasoningStartTag.Length;
                    continue;
                }

                if (MatchesTagAt(input, index, _reasoningEndTag))
                {
                    _inReasoning = false;
                    index += _reasoningEndTag.Length;
                    continue;
                }

                if (IsPotentialPartialTag(input, index))
                {
                    _partialTag = input.Substring(index);
                    break;
                }

                if (!_inReasoning)
                {
                    output.Append(input[index]);
                }

                index++;
            }

            return output.ToString();
        }

        private bool MatchesTagAt(string input, int index, string tag)
        {
            if (tag.Length == 0 || index + tag.Length > input.Length)
            {
                return false;
            }

            return string.Compare(input, index, tag, 0, tag.Length, true) == 0;
        }

        private bool IsPotentialPartialTag(string input, int index)
        {
            string remaining = input.Substring(index);
            return IsStrictPrefixOfTag(remaining, _reasoningStartTag) || IsStrictPrefixOfTag(remaining, _reasoningEndTag);
        }

        private bool IsStrictPrefixOfTag(string candidate, string tag)
        {
            return candidate.Length > 0
                && candidate.Length < tag.Length
                && string.Compare(tag, 0, candidate, 0, candidate.Length, true) == 0;
        }

        private void ApplyToolCallFilter()
        {
            string filtered = _toolCallPattern.Replace(_buffer.ToString(), string.Empty);
            _buffer.Length = 0;
            _buffer.Append(filtered);
        }

        private List<int> FilterRealPositions(int[] positions)
        {
            List<int> realPositions = new List<int>();
            if (positions == null)
            {
                return realPositions;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                int pos = positions[i];
                if (pos <= 0 || pos > _buffer.Length)
                {
                    continue;
                }

                string prefix = _buffer.ToString(0, pos).TrimEnd();
                if (prefix.Length > 0 && _sentenceEnders.Contains(prefix[prefix.Length - 1]))
                {
                    realPositions.Add(pos);
                }
            }

            return realPositions;
        }

        private bool ContainsSentenceEnder(StringBuilder builder)
        {
            for (int i = 0; i < builder.Length; i++)
            {
                if (_sentenceEnders.Contains(builder[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool LastNonWhitespaceIsSentenceEnder()
        {
            for (int i = _buffer.Length - 1; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(_buffer[i]))
                {
                    return _sentenceEnders.Contains(_buffer[i]);
                }
            }

            return false;
        }

        private void Reset()
        {
            _buffer.Length = 0;
            _inReasoning = false;
            _partialTag = string.Empty;
        }
    }
}
