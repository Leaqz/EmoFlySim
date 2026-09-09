using UnityEngine;

namespace BioPlane.Bio
{
    /// Maps a raw signal to 0..1 against a window that follows the player.
    /// Raw breath amplitude and resting heart rate differ enormously between
    /// people and between sessions, so a fixed calibration would make the game
    /// unplayable for most of them. The window expands whenever a new extreme
    /// arrives and slowly contracts otherwise, which keeps the full control range
    /// reachable without ever asking the player to configure anything.
    [System.Serializable]
    public class AdaptiveNormalizer
    {
        [Tooltip("How fast the window contracts, per second. Small = slow, stable.")]
        public float adaptRate = 0.05f;
        public float minRange = 1e-4f;

        float min = float.MaxValue;
        float max = float.MinValue;

        public bool Warm { get { return min <= max; } }
        public float Min { get { return min; } }
        public float Max { get { return max; } }

        public void Reset()
        {
            min = float.MaxValue;
            max = float.MinValue;
        }

        public float Normalize(float v, float dt)
        {
            if (!Warm)
            {
                min = v - minRange * 0.5f;
                max = v + minRange * 0.5f;
            }
            if (v < min) min = v;
            if (v > max) max = v;

            float mid = (min + max) * 0.5f;
            float k = 1f - Mathf.Exp(-adaptRate * Mathf.Max(0f, dt));
            min = Mathf.Lerp(min, mid, k);
            max = Mathf.Lerp(max, mid, k);

            float range = Mathf.Max(max - min, minRange);
            return Mathf.Clamp01((v - min) / range);
        }
    }
}
