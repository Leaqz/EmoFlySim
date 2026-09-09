using System;

namespace BioPlane.Bio
{
    /// One reading of the player's body. Everything downstream (flight model,
    /// visuals, audio, HUD) consumes this struct and nothing else, so swapping a
    /// simulated source for real hardware changes no game code.
    ///
    /// The three channels sit on very different timescales, which is the single
    /// most important fact about controlling anything with them:
    ///   breath   changes in tenths of a second, and is voluntary
    ///   heart    changes over several seconds, and is only nudgeable
    ///   skin     changes over 1 to 30 seconds, and is not voluntary at all
    [Serializable]
    public struct BioFrame
    {
        public bool hasBreath;
        public bool hasHeart;
        public bool hasSkin;

        /// 0 = fully exhaled, 1 = fully inhaled, normalized to this player's range.
        public float breath01;
        /// d(breath01)/dt. Positive while inhaling.
        public float breathVelocity;
        public float breathRateBpm;

        public float heartRateBpm;
        /// Heart rate mapped onto the player's own observed range.
        public float heartRate01;
        /// RMSSD in milliseconds.
        public float hrvMs;
        /// How strongly heart rate follows the breath curve (respiratory sinus
        /// arrhythmia). High values mean slow, regular, relaxed breathing.
        public float coherence01;

        /// Raw skin conductance in microsiemens. Typically 1 to 20 between people,
        /// which is why nothing downstream uses this number directly.
        public float skinConductanceUs;
        /// Tonic level (slow component), normalized to the player's own range.
        /// This is sustained arousal.
        public float skin01;
        /// Phasic component (fast component), 0..1. The bursts that follow a
        /// startle, an effort or a thought by one to three seconds, decaying over
        /// several more.
        public float skinPhasic01;

        public static BioFrame Neutral
        {
            get
            {
                return new BioFrame
                {
                    hasBreath = false,
                    hasHeart = false,
                    hasSkin = false,
                    breath01 = 0.5f,
                    breathVelocity = 0f,
                    breathRateBpm = 12f,
                    heartRateBpm = 70f,
                    heartRate01 = 0.5f,
                    hrvMs = 40f,
                    coherence01 = 0.5f,
                    skinConductanceUs = 4f,
                    skin01 = 0.5f,
                    skinPhasic01 = 0f
                };
            }
        }
    }
}
