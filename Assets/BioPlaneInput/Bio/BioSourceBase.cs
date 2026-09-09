using UnityEngine;

namespace BioPlane.Bio
{
    /// Base class for anything that can produce body data: a simulator, a
    /// microphone, a webcam, a UDP bridge to a real sensor.
    public abstract class BioSourceBase : MonoBehaviour
    {
        [Tooltip("When several sources are ready, the highest priority wins for each channel.")]
        public int priority = 0;

        public abstract bool IsReady { get; }
        public abstract BioFrame Sample(float dt);
        public virtual void Recalibrate() { }
    }
}
