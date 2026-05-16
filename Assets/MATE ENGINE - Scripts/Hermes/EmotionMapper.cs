using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Hermes
{
    /// <summary>
    /// Maps an emotion string or emoji to facial-expression keyframes loaded from a shallow YAML rules file.
    /// When constructed without YAML text, this class first tries Unity's TextAsset path
    /// <c>Resources.Load&lt;TextAsset&gt;("emotion_motion_map")</c>, then the non-generic
    /// <c>Resources.Load("emotion_motion_map") as TextAsset</c> fallback. If loading or parsing fails,
    /// the instance keeps an empty rule set and <see cref="Map"/> returns a neutral fallback keyframe.
    /// </summary>
    public class EmotionMapper
    {
        private const string ResourceName = "emotion_motion_map";

        private readonly Dictionary<string, List<Keyframe>> rules = new Dictionary<string, List<Keyframe>>();
        private bool warned;

        public EmotionMapper(string yamlText = null)
        {
            if (yamlText == null)
            {
                yamlText = LoadDefaultYaml();
                if (string.IsNullOrEmpty(yamlText))
                {
                    WarnOnce("Emotion motion map resource was not found; using neutral fallback only.");
                    return;
                }
            }

            try
            {
                ParseYaml(yamlText);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                rules.Clear();
                WarnOnce("Emotion motion map could not be parsed; using neutral fallback only. " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the configured keyframes for <paramref name="emotion"/>, or a neutral default keyframe
        /// when the emotion is null, empty, or not present in the loaded rules.
        /// </summary>
        public List<Keyframe> Map(string emotion)
        {
            if (string.IsNullOrEmpty(emotion) || !rules.TryGetValue(emotion, out List<Keyframe> keyframes) || keyframes.Count == 0)
            {
                return CreateDefaultKeyframes();
            }

            return CloneKeyframes(keyframes);
        }

        private static string LoadDefaultYaml()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                asset = Resources.Load(ResourceName) as TextAsset;
            }

            return asset != null ? asset.text : null;
        }

        private void ParseYaml(string yamlText)
        {
            string currentEmotion = null;
            KeyframeBuilder currentKeyframe = null;
            bool inTargets = false;

            string[] lines = yamlText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int indent = CountLeadingSpaces(line);
                string trimmed = line.Trim();

                if (indent == 0 && trimmed.EndsWith(":", StringComparison.Ordinal))
                {
                    AddPendingKeyframe(currentEmotion, currentKeyframe);
                    currentEmotion = Unquote(trimmed.Substring(0, trimmed.Length - 1).Trim());
                    currentKeyframe = null;
                    inTargets = false;

                    if (!rules.ContainsKey(currentEmotion))
                    {
                        rules[currentEmotion] = new List<Keyframe>();
                    }
                    continue;
                }

                if (currentEmotion == null)
                {
                    continue;
                }

                if (indent == 2 && trimmed.StartsWith("- duration:", StringComparison.Ordinal))
                {
                    AddPendingKeyframe(currentEmotion, currentKeyframe);
                    currentKeyframe = new KeyframeBuilder
                    {
                        Duration = ParseFloat(trimmed.Substring("- duration:".Length).Trim())
                    };
                    inTargets = false;
                    continue;
                }

                if (indent == 4 && trimmed == "targets:")
                {
                    if (currentKeyframe == null)
                    {
                        currentKeyframe = new KeyframeBuilder();
                    }
                    inTargets = true;
                    continue;
                }

                if (indent == 6 && inTargets && currentKeyframe != null)
                {
                    int colonIndex = trimmed.IndexOf(':');
                    if (colonIndex <= 0)
                    {
                        continue;
                    }

                    string targetName = Unquote(trimmed.Substring(0, colonIndex).Trim());
                    string valueText = trimmed.Substring(colonIndex + 1).Trim();
                    currentKeyframe.Targets[targetName] = ParseFloat(valueText);
                }
            }

            AddPendingKeyframe(currentEmotion, currentKeyframe);
        }

        private void AddPendingKeyframe(string emotion, KeyframeBuilder builder)
        {
            if (emotion == null || builder == null)
            {
                return;
            }

            if (!rules.TryGetValue(emotion, out List<Keyframe> keyframes))
            {
                keyframes = new List<Keyframe>();
                rules[emotion] = keyframes;
            }

            keyframes.Add(new Keyframe(builder.Duration, new Dictionary<string, float>(builder.Targets)));
        }

        private static float ParseFloat(string value)
        {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string StripComment(string line)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                }
                else if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                }
                else if (c == '#' && !inSingleQuote && !inDoubleQuote)
                {
                    return line.Substring(0, i).TrimEnd();
                }
            }

            return line.TrimEnd();
        }

        private static int CountLeadingSpaces(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }
            return count;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2)
            {
                char first = value[0];
                char last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return value.Substring(1, value.Length - 2);
                }
            }

            return value;
        }

        private static List<Keyframe> CreateDefaultKeyframes()
        {
            return new List<Keyframe>
            {
                new Keyframe(0.3f, new Dictionary<string, float> { { "neutral", 1.0f } })
            };
        }

        private static List<Keyframe> CloneKeyframes(List<Keyframe> keyframes)
        {
            List<Keyframe> clone = new List<Keyframe>(keyframes.Count);
            for (int i = 0; i < keyframes.Count; i++)
            {
                clone.Add(new Keyframe(keyframes[i].duration, new Dictionary<string, float>(keyframes[i].targets)));
            }
            return clone;
        }

        private void WarnOnce(string message)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning("[Hermes] " + message);
        }

        private sealed class KeyframeBuilder
        {
            public float Duration;
            public readonly Dictionary<string, float> Targets = new Dictionary<string, float>();
        }
    }
}
