using UnityEngine;
using BioPlane.Util;

namespace BioPlane.Bio
{
    /// Fake body, so you can build and tune the whole game before touching any
    /// hardware. Arrow keys up and down override the breath curve. The stress
    /// slider drives heart rate and skin conductance together, the way they move in
    /// a real person, and pressing R fires a phasic response so you can check that
    /// a startle actually drops the plane.
    [AddComponentMenu("BioPlane/Bio/Simulated Bio Source")]
    public class SimulatedBioSource : BioSourceBase
    {
        public float breathsPerMinute = 6f;
        public float baseHeartRate = 68f;
        [Tooltip("How far heart rate swings across one breath cycle.")]
        public float rsaAmplitude = 6f;
        public float noise = 0.01f;
        [Range(0f, 1f)] public float stress = 0f;
        public bool keyboardOverride = true;

        [Header("Skin conductance, microsiemens")]
        public float calmLevel = 4f;
        public float stressedLevel = 11f;
        [Tooltip("Slow upward drift over a session, as the hand warms under the electrodes.")]
        public float driftPerMinute = 0.5f;
        [Tooltip("Average seconds between spontaneous responses when calm.")]
        public float responseInterval = 25f;
        public float responseSize = 0.9f;
        [Tooltip("Seconds for a response to decay back to baseline.")]
        public float responseDecay = 9f;

        float phase;
        float manual = 0.5f;
        float prevBreath = 0.5f;
        float tonic = -1f;
        float phasic;
        float nextResponse = 8f;
        float elapsed;

        public override bool IsReady { get { return true; } }

        public override BioFrame Sample(float dt)
        {
            elapsed += dt;

            // ---- breath ---------------------------------------------------------
            float rate = Mathf.Max(0.5f, breathsPerMinute) / 60f;
            phase += dt * rate * Mathf.PI * 2f;
            float breath = 0.5f + 0.5f * Mathf.Sin(phase - Mathf.PI * 0.5f);

            if (keyboardOverride)
            {
                float v = InputCompat.Vertical();
                if (Mathf.Abs(v) > 0.01f)
                {
                    manual = Mathf.Clamp01(manual + v * dt * 0.9f);
                    breath = manual;
                }
                else
                {
                    manual = Mathf.Lerp(manual, breath, 1f - Mathf.Exp(-2f * dt));
                }
                if (InputCompat.KeyDown("r")) TriggerResponse(responseSize * 2f);
            }

            breath = Mathf.Clamp01(breath + Random.Range(-noise, noise));
            float bv = dt > 0f ? (breath - prevBreath) / dt : 0f;
            prevBreath = breath;

            // ---- heart -------------------------------------------------------------
            float hr = baseHeartRate + stress * 45f
                     + rsaAmplitude * (breath - 0.5f) * 2f * (1f - stress)
                     + Random.Range(-0.8f, 0.8f);

            // ---- skin conductance -----------------------------------------------------
            // A slow tonic floor set by stress, drifting upward over the session,
            // with sharp responses on top that decay over several seconds. Real
            // recordings look exactly like this, and reproducing the shape matters:
            // a flight model tuned against a clean sine wave falls apart the first
            // time a real response fires.
            float tonicTarget = Mathf.Lerp(calmLevel, stressedLevel, stress)
                              + driftPerMinute * (elapsed / 60f);
            if (tonic < 0f) tonic = tonicTarget;
            tonic = Mathf.Lerp(tonic, tonicTarget, 1f - Mathf.Exp(-dt / 20f));

            phasic = Mathf.Lerp(phasic, 0f, 1f - Mathf.Exp(-dt / Mathf.Max(0.5f, responseDecay)));
            nextResponse -= dt * (1f + stress * 3f);
            if (nextResponse <= 0f)
            {
                TriggerResponse(Random.Range(0.4f, 1.4f) * responseSize * (0.6f + stress));
                nextResponse = Random.Range(0.4f, 1.6f) * responseInterval;
            }

            float eda = tonic + phasic + Random.Range(-0.02f, 0.02f);

            // ---- frame ------------------------------------------------------------------
            BioFrame f = BioFrame.Neutral;
            f.hasBreath = true;
            f.hasHeart = true;
            f.hasSkin = true;
            f.breath01 = breath;
            f.breathVelocity = bv;
            f.breathRateBpm = breathsPerMinute;
            f.heartRateBpm = hr;
            f.hrvMs = Mathf.Lerp(70f, 15f, stress);
            f.skinConductanceUs = eda;
            return f;
        }

        /// Fires a phasic response, as a startle would. Hook it to game events.
        public void TriggerResponse(float size) { phasic = Mathf.Max(phasic, size); }
    }
}
