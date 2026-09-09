using UnityEngine;

namespace BioPlane.Bio
{
    /// Combines the three body channels into one climb command, and keeps each
    /// channel's share visible so the HUD can show the player why the plane is
    /// doing what it is doing.
    ///
    /// The three signals live on completely different timescales, which is the
    /// whole reason the mix works:
    ///
    ///   Breath is fast and voluntary. It is the throttle, and it is the only
    ///   channel that can push the plane both up and down on demand.
    ///
    ///   Heart rate moves over ten or twenty seconds and is only half voluntary.
    ///   It acts as ballast: a calm heart gives lift, a racing one weighs the
    ///   aircraft down.
    ///
    ///   Skin conductance is slower still and barely voluntary at all. Its tonic
    ///   level is a good measure of sustained arousal, so it is ballast too. Its
    ///   phasic bursts, the little spikes a second or two after a startle, are
    ///   fast, and they read beautifully as a sudden drop.
    ///
    /// Because two of the three are involuntary, the weights matter more than the
    /// formula. Set them too high and a stressed player cannot climb no matter how
    /// they breathe, which stops feeling like a challenge and starts feeling like
    /// a broken controller. The defaults keep breath dominant.
    [AddComponentMenu("BioPlane/Bio/Altitude Mixer")]
    public class AltitudeMixer : MonoBehaviour
    {
        public BioRouter bio;

        [Header("Breath, the throttle")]
        [Range(0f, 2f)] public float breathWeight = 1f;
        [Tooltip("Absolute breath depth. Holding a full breath keeps you up.")]
        [Range(0f, 1f)] public float depthWeight = 0.65f;
        [Tooltip("Rate of change. Gives the moment of inhaling an immediate kick.")]
        [Range(0f, 1f)] public float velocityWeight = 0.35f;
        public float breathVelocityGain = 1.2f;

        [Header("Heart rate, ballast")]
        [Range(0f, 2f)] public float heartWeight = 0.3f;

        [Header("Skin conductance, ballast plus gusts")]
        [Range(0f, 2f)] public float skinWeight = 0.05f;
        [Tooltip("How hard a phasic burst shoves the plane down. 0 disables it.")]
        [Range(0f, 1f)] public float phasicKick = 0.3f;

        [Header("Behaviour")]
        [Tooltip("On: calm gives lift and arousal weighs you down. Off: the two are reversed.")]
        public bool arousalIsBallast = true;
        [Tooltip("Keeps the total in -1..1 by dividing through the weights. Turn off to let a calm body out-climb the ceiling.")]
        public bool normalizeWeights = true;

        /// Each channel's signed share of the final command, ready to draw.
        public float BreathContribution { get; private set; }
        public float HeartContribution { get; private set; }
        public float SkinContribution { get; private set; }
        public float PhasicContribution { get; private set; }
        public float Climb { get; private set; }

        void Awake() { if (bio == null) bio = GetComponent<BioRouter>(); }

        /// Reads whatever the router last produced. Call Evaluate(frame) directly
        /// if you want to drive the mixer from somewhere else.
        public float Evaluate()
        {
            return Evaluate(bio != null ? bio.Frame : BioFrame.Neutral);
        }

        public float Evaluate(BioFrame f)
        {
            float depth = (f.breath01 - 0.5f) * 2f;
            float rate = Mathf.Clamp(f.breathVelocity * breathVelocityGain, -1f, 1f);
            float breathTerm = Mathf.Clamp(depth * depthWeight + rate * velocityWeight, -1f, 1f);

            // Centred on the player's own middle, so 0 is "however you usually are"
            // rather than some absolute bpm or microsiemens the game guessed.
            float heartTerm = (0.5f - f.heartRate01) * 2f;
            float skinTerm = (0.5f - f.skin01) * 2f;
            if (!arousalIsBallast) { heartTerm = -heartTerm; skinTerm = -skinTerm; }

            float hw = f.hasHeart ? heartWeight : 0f;
            float sw = f.hasSkin ? skinWeight : 0f;

            BreathContribution = breathTerm * breathWeight;
            HeartContribution = heartTerm * hw;
            SkinContribution = skinTerm * sw ;
            //PhasicContribution = f.hasSkin ? -f.skinPhasic01 * phasicKick : 0f;

            float sum = BreathContribution + HeartContribution + SkinContribution;
            if (normalizeWeights)
            {
                float total = breathWeight + hw + sw;
                if (total > 1e-4f)
                {
                    sum /= total;
                    BreathContribution /= total;
                    HeartContribution /= total;
                    SkinContribution /= total;
                }
            }

            Climb = Mathf.Clamp(sum , -1f, 1f);
            return Climb;
        }
    }
}
