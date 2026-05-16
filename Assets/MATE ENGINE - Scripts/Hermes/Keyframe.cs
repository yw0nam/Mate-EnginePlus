using System.Collections.Generic;

namespace Hermes
{
    public struct Keyframe
    {
        public float duration;
        public Dictionary<string, float> targets;

        public Keyframe(float duration, Dictionary<string, float> targets)
        {
            this.duration = duration;
            this.targets = targets;
        }
    }
}
