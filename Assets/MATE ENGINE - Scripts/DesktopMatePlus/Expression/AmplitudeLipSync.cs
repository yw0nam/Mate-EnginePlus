using UnityEngine;

namespace DesktopMatePlus
{
    /// <summary>
    /// Reads AudioSource output amplitude every frame and drives UniversalBlendshapes.A
    /// with asymmetric, framerate-independent smoothing.
    /// </summary>
    public class AmplitudeLipSync : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("If empty, GetComponent<AudioSource>() on this GameObject.")]
        public AudioSource source;

        [Tooltip("Auto-found if left empty: VRMLoader model first, then scene-wide.")]
        public UniversalBlendshapes blendshapes;

        [Header("Analysis")]
        [Range(64, 2048)] public int sampleWindow = 1024;
        [Range(0f, 1f)]   public float noiseFloor = 0.02f;
        [Range(0.5f, 8f)] public float gain       = 3.0f;
        [Range(0f, 1f)]   public float maxOpen    = 1.0f;

        [Header("Smoothing (units: per-second)")]
        [Range(1f, 60f)] public float attackSpeed  = 20f;
        [Range(1f, 60f)] public float releaseSpeed = 8f;

        private float[] _buffer;
        private float   _current;

        void Awake()
        {
            _buffer = new float[sampleWindow];
            if (source == null) source = GetComponent<AudioSource>();
        }

        void Update()
        {
            TryFindBlendshapes();

            float target;
            if (source == null || !source.isPlaying)
            {
                target = 0f;
            }
            else
            {
                // Reallocate if user tweaked sampleWindow at runtime.
                if (_buffer.Length != sampleWindow)
                    _buffer = new float[sampleWindow];

                source.GetOutputData(_buffer, 0);

                float sumSq = 0f;
                for (int i = 0; i < _buffer.Length; i++)
                    sumSq += _buffer[i] * _buffer[i];

                float rms = Mathf.Sqrt(sumSq / _buffer.Length);
                if (rms < noiseFloor) rms = 0f;

                target = Mathf.Min(rms * gain, maxOpen);
            }

            float speed = (target > _current) ? attackSpeed : releaseSpeed;
            _current = Mathf.MoveTowards(_current, target, speed * Time.deltaTime);

            if (blendshapes != null)
                blendshapes.A = _current;
        }

        private void TryFindBlendshapes()
        {
            // Re-resolve if the current reference is destroyed OR its GameObject has
            // been deactivated. VRMLoader hot-swaps avatars at runtime: it clones a
            // new model and disables the default template. If our very first Update
            // ran BEFORE the swap, we cached the template UB; once the template was
            // deactivated, its LateUpdate stopped pumping values into the VRM proxy
            // and lip sync went silent. Checking activeInHierarchy recovers from that.
            if (blendshapes != null && blendshapes.gameObject.activeInHierarchy) return;
            blendshapes = null;

            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var model = loader.GetCurrentModel();
                if (model != null)
                    blendshapes = model.GetComponentInChildren<UniversalBlendshapes>(true);
            }

            // Fallback: first ACTIVE UB in the scene. Do NOT include inactive - we
            // would otherwise latch onto a deactivated template avatar whose
            // LateUpdate never runs and so never drives the VRM expression proxy.
            if (blendshapes == null)
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>();
        }
    }
}
