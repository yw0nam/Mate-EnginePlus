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
            if (blendshapes != null) return;

            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var model = loader.GetCurrentModel();
                if (model != null)
                    blendshapes = model.GetComponentInChildren<UniversalBlendshapes>(true);
            }

            if (blendshapes == null)
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>();
        }
    }
}
