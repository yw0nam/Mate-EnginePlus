using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hermes
{
    /// <summary>
    /// Cleans TTS-bound sentence text by removing *action* and [meta] blocks,
    /// collapsing whitespace, and returning the first configured emotion emoji
    /// found in the original sentence while keeping that emoji in the cleaned text.
    /// Faithful port of D:\codes\waifu\agents\yuri-local\nanobot_runtime\src\nanobot_runtime\services\tts\preprocessor.py.
    ///
    /// Manual verification cases:
    /// Process("hello") -> ("hello", null)
    /// Process("こんにちは 😊") -> ("こんにちは 😊", "😊")
    /// Process("*action* hello") -> ("hello", null)
    /// Process("[meta] hello") -> ("hello", null)
    /// Process("  multiple   spaces  ") -> ("multiple spaces", null)
    /// Process("") -> ("", null)
    /// Process("   ") -> ("", null)
    /// </summary>
    public static class Preprocessor
    {
        private static readonly HashSet<string> DefaultEmojiSet = new HashSet<string>
        {
            "😊",
            "😭",
            "😠",
            "😮",
            "😪",
            "🤭",
            "😰",
            "😆",
            "😱",
            "😟",
            "😌",
            "🤔",
            "😲",
            "😖",
            "🥺",
            "😏",
            "🫶",
            "😒",
            "🥵",
        };

        public static (string clean, string emotion) Process(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return ("", null);
            }

            string emotionTag = null;
            int firstPosition = sentence.Length;

            foreach (string emoji in DefaultEmojiSet)
            {
                int position = sentence.IndexOf(emoji, System.StringComparison.Ordinal);
                if (position != -1 && position < firstPosition)
                {
                    firstPosition = position;
                    emotionTag = emoji;
                }
            }

            string cleaned = sentence;
            cleaned = Regex.Replace(cleaned, @"\*[^*]*\*", "");
            cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return (cleaned, emotionTag);
        }
    }
}
