using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// Bridges tts_chunk keyframes to UniversalBlendshapes component on the avatar.
    /// Maps backend expression names to UniversalBlendshapes fields.
    /// </summary>
    public class KeyframeAnimationBridge : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Auto-found if left empty. Searches VRM model hierarchy.")]
        public UniversalBlendshapes blendshapes;

        private Coroutine _activeSequence;
        private readonly Queue<List<TimelineKeyframe>> _keyframeQueue = new();
        private bool _isPlaying;

        // Backend emotion name -> UniversalBlendshapes field name
        private static readonly Dictionary<string, string> ExpressionMap = new()
        {
            { "happy",     "Joy" },
            { "sad",       "Sorrow" },
            { "angry",     "Angry" },
            { "surprised", "Fun" },
            { "relaxed",   "Fun" },
            { "neutral",   "Neutral" },
            // VRM 1.0 names that might come directly
            { "joy",       "Joy" },
            { "sorrow",    "Sorrow" },
            { "fun",       "Fun" },
        };

        // All expression fields that we drive (to reset them)
        private static readonly string[] AllExpressions = { "Joy", "Angry", "Sorrow", "Fun", "Neutral" };

        /// <summary>
        /// Enqueue keyframes from a tts_chunk for sequential playback.
        /// </summary>
        public void EnqueueKeyframes(TtsChunkData chunk)
        {
            if (chunk.keyframes == null || chunk.keyframes.Count == 0) return;
            _keyframeQueue.Enqueue(chunk.keyframes);
            if (!_isPlaying)
                PlayNext();
        }

        /// <summary>
        /// Reset all expressions and clear queue.
        /// </summary>
        public void ResetExpressions()
        {
            if (_activeSequence != null)
                StopCoroutine(_activeSequence);
            _activeSequence = null;
            _keyframeQueue.Clear();
            _isPlaying = false;
            ClearAllExpressions();
        }

        private void PlayNext()
        {
            if (_keyframeQueue.Count == 0)
            {
                _isPlaying = false;
                ClearAllExpressions();
                return;
            }

            _isPlaying = true;
            var keyframes = _keyframeQueue.Dequeue();
            _activeSequence = StartCoroutine(PlayKeyframeSequence(keyframes));
        }

        private IEnumerator PlayKeyframeSequence(List<TimelineKeyframe> keyframes)
        {
            TryFindBlendshapes();
            if (blendshapes == null)
            {
                _isPlaying = false;
                yield break;
            }

            foreach (var kf in keyframes)
            {
                ApplyKeyframe(kf);
                if (kf.duration > 0f)
                    yield return new WaitForSeconds(kf.duration);
            }

            PlayNext();
        }

        private void ApplyKeyframe(TimelineKeyframe kf)
        {
            if (blendshapes == null) return;

            // Reset all expressions first
            ClearAllExpressions();

            // Apply targets
            foreach (var kvp in kf.targets)
            {
                string fieldName = MapExpression(kvp.Key);
                SetBlendshapeField(fieldName, kvp.Value);
            }
        }

        private void ClearAllExpressions()
        {
            if (blendshapes == null) return;
            foreach (var expr in AllExpressions)
                SetBlendshapeField(expr, 0f);
        }

        private static string MapExpression(string backendName)
        {
            string lower = backendName.ToLowerInvariant();
            return ExpressionMap.TryGetValue(lower, out var mapped) ? mapped : backendName;
        }

        private void SetBlendshapeField(string fieldName, float value)
        {
            if (blendshapes == null) return;

            switch (fieldName)
            {
                case "Joy":     blendshapes.Joy = value; break;
                case "Angry":   blendshapes.Angry = value; break;
                case "Sorrow":  blendshapes.Sorrow = value; break;
                case "Fun":     blendshapes.Fun = value; break;
                case "Neutral": blendshapes.Neutral = value; break;
                case "A":       blendshapes.A = value; break;
                case "I":       blendshapes.I = value; break;
                case "U":       blendshapes.U = value; break;
                case "E":       blendshapes.E = value; break;
                case "O":       blendshapes.O = value; break;
            }
        }

        private void TryFindBlendshapes()
        {
            if (blendshapes != null) return;

            // Search via VRMLoader first
            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var model = loader.GetCurrentModel();
                if (model != null)
                    blendshapes = model.GetComponentInChildren<UniversalBlendshapes>(true);
            }

            // Fallback: search scene
            if (blendshapes == null)
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>();
        }
    }
}
