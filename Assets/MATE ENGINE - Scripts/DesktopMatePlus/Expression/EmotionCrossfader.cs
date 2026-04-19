using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// Subscribes to TtsAudioPlayer.OnChunkStarted and crossfades UniversalBlendshapes
    /// emotion fields (Joy / Angry / Sorrow / Fun / Neutral) toward the chunk's emotion.
    /// UniversalBlendshapes is pass-through, so this component is the canonical smoother.
    ///
    /// Runs in LateUpdate so the write happens AFTER the avatar's Animator samples
    /// FACE_RESET / FACE_SMILE / FACE_IDLE clips (which also bind the same fields). A
    /// DefaultExecutionOrder of -100 ensures we execute BEFORE UniversalBlendshapes'
    /// own LateUpdate, so our fresh value reaches the VRM BlendShapeProxy this frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EmotionCrossfader : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Auto-found if left empty: VRMLoader model first, then scene-wide.")]
        public UniversalBlendshapes blendshapes;

        [Tooltip("Required. Fires OnChunkStarted when each TTS chunk begins playback.")]
        public TtsAudioPlayer player;

        [Header("Crossfade")]
        [Range(0.05f, 1f)] public float crossfadeDuration = 0.25f;
        [Range(0.1f, 3f)]  public float decayToNeutral   = 0.5f;

        private static readonly string[] Fields = { "Joy", "Angry", "Sorrow", "Fun", "Neutral" };

        private static readonly Dictionary<string, string> EmotionMap = new()
        {
            { "happy",     "Joy" },
            { "joy",       "Joy" },
            { "sad",       "Sorrow" },
            { "sorrow",    "Sorrow" },
            { "angry",     "Angry" },
            { "surprised", "Fun" },
            { "fun",       "Fun" },
            { "relaxed",   "Fun" },
            { "neutral",   "Neutral" },
        };

        private readonly Dictionary<string, float> _current = new();
        private readonly HashSet<string> _unknownWarned = new();
        private string _targetField = "Neutral";
        private float _lastChunkStartedTime;
        private bool _subscribed;

        void Awake()
        {
            foreach (var f in Fields) _current[f] = 0f;
        }

        void OnEnable()
        {
            TrySubscribe();
        }

        void OnDisable()
        {
            if (_subscribed && player != null)
            {
                player.OnChunkStarted -= HandleChunkStarted;
                _subscribed = false;
            }
        }

        void LateUpdate()
        {
            if (!_subscribed) TrySubscribe();

            TryFindBlendshapes();
            if (blendshapes == null) return;

            // Idle decay: after audio stops and decayToNeutral elapses, slide back to Neutral.
            if (player != null && !player.IsPlaying &&
                Time.time - _lastChunkStartedTime > decayToNeutral)
            {
                _targetField = "Neutral";
            }

            float step = Time.deltaTime / Mathf.Max(crossfadeDuration, 0.001f);

            foreach (var f in Fields)
            {
                float target = (f == _targetField) ? 1f : 0f;
                _current[f] = Mathf.MoveTowards(_current[f], target, step);
                WriteField(f, _current[f]);
            }
        }

        /// <summary>
        /// Snap all emotion fields to 0 immediately. Call on session change / interrupt.
        /// Name avoids collision with MonoBehaviour's virtual Reset() editor callback.
        /// </summary>
        public void ResetExpressions()
        {
            foreach (var f in Fields) _current[f] = 0f;
            _targetField = "Neutral";
            if (blendshapes != null)
            {
                foreach (var f in Fields) WriteField(f, 0f);
            }
        }

        private void TrySubscribe()
        {
            if (_subscribed || player == null) return;
            player.OnChunkStarted += HandleChunkStarted;
            _subscribed = true;
        }

        private void HandleChunkStarted(TtsChunkData chunk)
        {
            _targetField = MapEmotion(chunk?.emotion);
            _lastChunkStartedTime = Time.time;
        }

        private string MapEmotion(string emotion)
        {
            if (string.IsNullOrEmpty(emotion)) return "Neutral";
            string lower = emotion.ToLowerInvariant();
            if (EmotionMap.TryGetValue(lower, out var mapped)) return mapped;
            if (_unknownWarned.Add(lower))
                Debug.LogWarning($"[EmotionCrossfader] unknown emotion '{emotion}' — using Neutral");
            return "Neutral";
        }

        private void WriteField(string field, float value)
        {
            if (blendshapes == null) return;
            switch (field)
            {
                case "Joy":     blendshapes.Joy = value; break;
                case "Angry":   blendshapes.Angry = value; break;
                case "Sorrow":  blendshapes.Sorrow = value; break;
                case "Fun":     blendshapes.Fun = value; break;
                case "Neutral": blendshapes.Neutral = value; break;
            }
        }

        private void TryFindBlendshapes()
        {
            // Re-resolve if the current reference is destroyed OR its GameObject has
            // been deactivated. See AmplitudeLipSync.TryFindBlendshapes for the
            // VRMLoader hot-swap rationale: without this, we latch onto a template
            // UB that gets deactivated mid-scene and silently stops driving the VRM.
            if (blendshapes != null && blendshapes.gameObject.activeInHierarchy) return;
            blendshapes = null;

            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var model = loader.GetCurrentModel();
                if (model != null)
                    blendshapes = model.GetComponentInChildren<UniversalBlendshapes>(true);
            }

            // Fallback: first ACTIVE UB in the scene. Do NOT include inactive.
            if (blendshapes == null)
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>();
        }
    }
}
