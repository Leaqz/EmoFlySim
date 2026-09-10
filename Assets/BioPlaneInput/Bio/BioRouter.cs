using System.Collections.Generic;
using UnityEngine;
using BioPlane.Util;

namespace BioPlane.Bio
{
    /// This script is the middle layer between the sensors and the game.
    ///
    /// On one side there are "sources". A source is anything that produces body
    /// data: a file being replayed, a fake body for testing, a Raspberry Pi sending
    /// numbers over the network. On the other side is the game, which wants clean,
    /// simple values it can use immediately.
    ///
    /// Raw sensor data is not usable directly. There are four problems, and this
    /// script solves one after the other:
    ///
    /// 1. There can be several sources at once. Which one should we believe?
    ///    Answer: each source has a "priority" number, and for each of the three
    ///    channels we take the value from the source with the highest priority.
    ///    This is done per channel, so a microphone can give the breath while a
    ///    chest strap gives the heart rate.
    ///
    /// 2. The numbers mean different things for different people. One person's
    ///    skin conductance sits at 3, another's at 15, and both are normal. If the
    ///    game used those numbers directly it would be unplayable for most people.
    ///    Answer: every channel is converted to a 0 to 1 value, measured against
    ///    the range that this person actually produces. See AdaptiveNormalizer.
    ///
    /// 3. Real signals are noisy. A value that jumps around makes the plane shake.
    ///    Answer: smoothing. Each channel gets a different amount, because each
    ///    channel changes at a different natural speed.
    ///
    /// 4. Skin conductance is really two signals mixed together, and the game wants
    ///    them separately. This is explained in detail further down.
    ///
    /// The result is stored in "Frame", which everything else in the game reads.
    ///
    /// Note on units: "us" in this file means microsiemens, the unit of skin
    /// conductance. "bpm" means beats per minute.
    [DefaultExecutionOrder(-100)]   // runs before other scripts, so the data is ready when they read it
    [AddComponentMenu("BioPlane/Bio/Bio Router")]
    public class BioRouter : MonoBehaviour
    {
        // All the sources we listen to. Normally you do not fill this by hand:
        // autoCollectSources finds them for you when the game starts.
        public List<BioSourceBase> sources = new List<BioSourceBase>();
        public bool autoCollectSources = true;

        [Header("Smoothing (seconds)")]
        // These numbers say "how long does it take to react to a change".
        // A small number = fast and nervous. A large number = slow and calm.
        //
        // Each channel gets a different value because the body itself is different
        // on each channel. Breathing changes in a fraction of a second, so it must
        // stay fast or the plane feels disconnected from the player. Heart rate
        // changes over many seconds, so smoothing it hard removes noise and loses
        // nothing real.
        [Tooltip("Breath must stay responsive or the plane feels disconnected from the body.")]
        public float breathSmoothing = 0.12f;
        [Tooltip("Heart rate is a slow signal, so smooth it hard.")]
        public float heartSmoothing = 2.0f;
        public float skinSmoothing = 0.5f;

        [Header("Skin conductance")]
        // Skin conductance (also called EDA or GSR) measures how well the skin
        // conducts electricity. Sweat glands make it conduct better. Those glands
        // react to stress, effort and surprise, and you cannot control them on
        // purpose. That is what makes the signal interesting.
        //
        // The signal contains two things at the same time:
        //
        //   the TONIC level - a slow floor that moves over tens of seconds.
        //                     This is general arousal: how activated the person is
        //                     right now, overall.
        //
        //   the PHASIC part - short bumps sitting on top of that floor. One bump
        //                     follows a surprise or an effort after about 1 to 3
        //                     seconds, and fades away over 5 to 30 seconds.
        //
        // We separate them by taking a very slow average of the signal. That slow
        // average IS the tonic level, because it is too slow to follow the bumps.
        // Whatever is left when we subtract it must be the phasic part.

        [Tooltip("Time constant of the tonic baseline, in seconds. 20 to 60 is normal.")]
        // How slow that slow average is. Larger = slower = the baseline ignores
        // more of the bumps, but also takes longer to follow a real change.
        public float tonicTau = 25f;

        [Tooltip("Microsiemens above baseline that counts as a full phasic response.")]
        // How far above the baseline the signal must go before we call it a full
        // response (a value of 1). Real responses are usually between 0.01 and 1
        // microsiemens, so 0.35 is a reasonable middle.
        public float phasicScale = 0.35f;

        [Tooltip("Phasic level that counts as a response, for the per-minute count.")]
        // Only used for counting responses per minute, for display. It does not
        // affect the flying.
        public float scrThreshold = 0.35f;

        [Header("Calibration")]
        // At the start of a session we do not yet know this person's range, so the
        // first seconds are a learning period. The game can show a message during
        // this time. Everything still works, it is just less accurate.
        public float calibrationSeconds = 20f;

        // One normalizer per channel, each converting raw numbers into 0 to 1.
        // "adaptRate" controls how quickly each one forgets old extremes.
        // Breath forgets fastest, because breathing depth changes within a session.
        // Skin forgets slowest, because its range is stable and drifts only slowly.
        public AdaptiveNormalizer breathNorm = new AdaptiveNormalizer { adaptRate = 0.04f };
        public AdaptiveNormalizer heartNorm = new AdaptiveNormalizer { adaptRate = 0.01f };
        public AdaptiveNormalizer skinNorm = new AdaptiveNormalizer { adaptRate = 0.008f };

        [Header("Arousal mix")]
        // "Arousal" is one number saying how activated the person is: 0 is calm,
        // 1 is highly activated. It is built from several channels because no
        // single one is reliable on its own.
        //
        // Skin conductance gets the largest weight because it is the most direct
        // measure of this. Heart rate helps but also rises for boring reasons like
        // moving your arm. Incoherence (see below) is a weak hint and gets little.
        //
        // These do not need to add up to 1. The code divides by their sum, and it
        // ignores the weight of any channel that has no sensor connected.
        [Range(0f, 1f)] public float arousalFromSkin = 0.45f;
        [Range(0f, 1f)] public float arousalFromPhasic = 0.15f;
        [Range(0f, 1f)] public float arousalFromHeart = 0.30f;
        [Range(0f, 1f)] public float arousalFromIncoherence = 0.10f;

        /// The finished, cleaned data. This is what the rest of the game reads.
        public BioFrame Frame { get; private set; }
        /// 0 = calm, 1 = activated. Drives speed, turbulence and colour.
        public float Arousal01 { get; private set; }
        /// How well breathing and heart rate move together. See EstimateCoherence.
        public float Coherence01 { get; private set; }
        /// How many skin responses happened in the last minute. For display only.
        public float ScrPerMinute { get; private set; }
        /// True during the learning period at the start.
        public bool Calibrating { get { return Time.time - calibStart < calibrationSeconds; } }

        float calibStart;

        // SmoothDamp needs a place to remember the current speed of each value
        // between frames. These three variables are only for that. Never read them.
        float breathVel, heartVel, skinVel;

        // "sm" means smoothed. These hold the smoothed value from the last frame.
        float smBreath = 0.5f, smHeart = 70f, smSkin, prevBreath = 0.5f;

        float tonicBaseline;    // the slow average of skin conductance
        bool skinPrimed;        // false until we have seen the first skin value
        float lastScrTime = -99f;
        readonly Queue<float> scrTimes = new Queue<float>();   // times of recent responses

        // A ring buffer holding the last N frames of breath and heart rate, used to
        // calculate coherence. A ring buffer is a fixed size array that we write to
        // in a circle: when we reach the end we go back to the start and overwrite
        // the oldest entry. This way we always have the most recent N values without
        // ever allocating memory.
        const int N = 256;
        readonly float[] bBuf = new float[N];
        readonly float[] hBuf = new float[N];
        int idx, count;   // idx = where to write next, count = how many we have so far

        void Awake()
        {
            Frame = BioFrame.Neutral;
            Arousal01 = 0.5f;
            Coherence01 = 0.5f;

            // Find every source on this object and on its children, including the
            // ones that are currently switched off, so that turning one on later
            // works without any extra setup.
            if (autoCollectSources)
            {
                BioSourceBase[] found = GetComponentsInChildren<BioSourceBase>(true);
                foreach (BioSourceBase s in found) if (!sources.Contains(s)) sources.Add(s);
            }

            Recalibrate();
        }

        void Update()
        {
            // Never let dt be zero. We divide by it later, and dividing by zero
            // produces infinity, which would spread through every later value.
            float dt = Mathf.Max(1e-4f, Time.deltaTime);

            if (InputCompat.KeyDown("c")) Recalibrate();

            // "merged" is the frame we are building this update. It starts as
            // Neutral, which means "no sensor connected, use safe middle values".
            BioFrame merged = BioFrame.Neutral;

            // ---- step 1: choose the best source for each channel ------------------
            //
            // We look at every source and keep the value from the one with the
            // highest priority. Each channel is decided separately, so different
            // sources can win for breath, heart and skin.
            //
            // int.MinValue is the smallest possible number, so the first working
            // source we find always wins at the start.
            int bestBreath = int.MinValue, bestHeart = int.MinValue, bestSkin = int.MinValue;

            for (int i = 0; i < sources.Count; i++)
            {
                BioSourceBase s = sources[i];

                // Skip sources that are missing, switched off, or not delivering
                // data (for example a network source that has received nothing yet).
                if (s == null || !s.isActiveAndEnabled || !s.IsReady) continue;

                BioFrame f = s.Sample(dt);

                // Each source says which channels it actually has, using the
                // hasBreath / hasHeart / hasSkin flags. A microphone has breath but
                // no heart rate, so it must not overwrite the heart rate with zero.
                if (f.hasBreath && s.priority >= bestBreath)
                {
                    bestBreath = s.priority;
                    merged.hasBreath = true;
                    merged.breath01 = f.breath01;
                    merged.breathRateBpm = f.breathRateBpm;
                }
                if (f.hasHeart && s.priority >= bestHeart)
                {
                    bestHeart = s.priority;
                    merged.hasHeart = true;
                    merged.heartRateBpm = f.heartRateBpm;
                    merged.hrvMs = f.hrvMs;
                }
                if (f.hasSkin && s.priority >= bestSkin)
                {
                    bestSkin = s.priority;
                    merged.hasSkin = true;
                    merged.skinConductanceUs = f.skinConductanceUs;
                }
            }

            // ---- step 2: breath ------------------------------------------------------
            //
            // Convert to 0..1 against this person's own range, then smooth it.
            // If there is no breath sensor we use 0.5, the neutral middle, so the
            // plane neither climbs nor dives.
            float b = merged.hasBreath ? breathNorm.Normalize(merged.breath01, dt) : 0.5f;

            // SmoothDamp moves a value gradually towards a target. Unlike a simple
            // average it also keeps track of speed, so the result has no sudden
            // corners. That is why it needs the "breathVel" helper variable.
            smBreath = Mathf.SmoothDamp(smBreath, b, ref breathVel, breathSmoothing);
            merged.breath01 = smBreath;

            // Speed of change: how fast the value moved since the last frame.
            // Dividing by dt turns "change per frame" into "change per second", so
            // the number does not depend on the frame rate.
            //
            // This matters for the game: the moment of breathing IN is a different
            // signal from the state of HAVING breathed in, and the flight model uses
            // both.
            merged.breathVelocity = (smBreath - prevBreath) / dt;
            prevBreath = smBreath;

            // ---- step 3: heart rate -----------------------------------------------------
            if (merged.hasHeart)
            {
                smHeart = Mathf.SmoothDamp(smHeart, merged.heartRateBpm, ref heartVel, heartSmoothing);
                merged.heartRateBpm = smHeart;          // the real number, in bpm, for display
                merged.heartRate01 = heartNorm.Normalize(smHeart, dt);   // 0..1, for the game
            }
            else merged.heartRate01 = 0.5f;

            // ---- step 4: skin conductance --------------------------------------------------
            if (merged.hasSkin)
            {
                float raw = merged.skinConductanceUs;

                // A little smoothing first, to remove electrical noise.
                smSkin = Mathf.SmoothDamp(smSkin, raw, ref skinVel, skinSmoothing);

                // On the very first reading, start the baseline at the current value.
                // Otherwise it would begin at zero and slowly climb for a minute,
                // and the game would read that climb as a huge stress response.
                if (!skinPrimed) { tonicBaseline = smSkin; skinPrimed = true; }

                // The slow average, which becomes the tonic level.
                //
                // The formula 1 - exp(-dt / tau) looks strange but it simply means
                // "move this fraction of the way towards the new value this frame,
                // so that the whole movement takes about tau seconds". Written this
                // way, the result is identical at 30 and at 144 frames per second.
                // A plain Lerp with a fixed fraction would be faster on a faster
                // computer, which is a bug that is very easy to miss.
                tonicBaseline = Mathf.Lerp(tonicBaseline, smSkin, 1f - Mathf.Exp(-dt / Mathf.Max(1f, tonicTau)));

                merged.skinConductanceUs = smSkin;

                // The slow floor, as 0..1 against this person's range. General arousal.
                merged.skin01 = skinNorm.Normalize(tonicBaseline, dt);

                // What is left after removing the floor: the short bumps.
                // Clamp01 also throws away negative values, which happen when the
                // signal drops below its own baseline. Those are not responses.
                merged.skinPhasic01 = Mathf.Clamp01((smSkin - tonicBaseline) / Mathf.Max(0.001f, phasicScale));

                // Count responses, for the display only. The 3 second gap stops one
                // long bump from being counted many times in a row.
                if (merged.skinPhasic01 > scrThreshold && Time.time - lastScrTime > 3f)
                {
                    lastScrTime = Time.time;
                    scrTimes.Enqueue(Time.time);
                }
                // Forget anything older than a minute, so the number always means
                // "responses in the last 60 seconds".
                while (scrTimes.Count > 0 && Time.time - scrTimes.Peek() > 60f) scrTimes.Dequeue();
                ScrPerMinute = scrTimes.Count;
            }
            else
            {
                merged.skin01 = 0.5f;
                merged.skinPhasic01 = 0f;
            }

            // ---- step 5: values calculated from the others -------------------------------------
            Push(merged.breath01, merged.heartRate01);
            merged.coherence01 = EstimateCoherence();
            Coherence01 = merged.coherence01;

            // Build the single arousal number.
            //
            // Each weight is set to zero when its sensor is missing. Then we divide
            // by the sum of the weights that are left. This is important: without
            // it, a player with no skin sensor would always look calm, because the
            // largest term would simply be missing from the total.
            float wSkin = merged.hasSkin ? arousalFromSkin : 0f;
            float wPha = merged.hasSkin ? arousalFromPhasic : 0f;
            float wHr = merged.hasHeart ? arousalFromHeart : 0f;
            float wCoh = arousalFromIncoherence;
            float wSum = wSkin + wPha + wHr + wCoh;

            // No usable channel at all, or every weight set to zero: stay neutral.
            if (wSum < 1e-4f) Arousal01 = 0.5f;
            else Arousal01 = Mathf.Clamp01((merged.skin01 * wSkin
                                          + merged.skinPhasic01 * wPha
                                          + merged.heartRate01 * wHr
                                          // Note the "1 minus": LOW coherence means
                                          // HIGH arousal, so the value is flipped.
                                          + (1f - merged.coherence01) * wCoh) / wSum);

            // Publish. Everything else in the game reads Frame, and only here is it
            // ever written.
            Frame = merged;
        }

        /// Starts the learning period again. Call this when a new person sits down,
        /// or when the values feel wrong because the sensors have drifted.
        /// The player can also press C.
        public void Recalibrate()
        {
            calibStart = Time.time;

            // Throw away the learned ranges so they are measured again.
            breathNorm.Reset();
            heartNorm.Reset();
            skinNorm.Reset();

            skinPrimed = false;     // the baseline will restart at the next reading
            scrTimes.Clear();
            count = 0; idx = 0;     // empty the ring buffer

            // Sources may have their own calibration, so tell them as well.
            for (int i = 0; i < sources.Count; i++) if (sources[i] != null) sources[i].Recalibrate();
        }

        /// Stores one pair of values in the ring buffer.
        /// The % operator gives the remainder of a division, so idx jumps back to 0
        /// when it reaches N. That is what makes the buffer circular.
        void Push(float b, float h)
        {
            bBuf[idx] = b; hBuf[idx] = h;
            idx = (idx + 1) % N;
            if (count < N) count++;
        }

        /// How strongly breathing and heart rate move together.
        ///
        /// When a person breathes slowly and calmly, their heart speeds up slightly
        /// while breathing in and slows down while breathing out. Doctors call this
        /// respiratory sinus arrhythmia. The two curves then have the same shape,
        /// and this function returns a high number. When someone is stressed or
        /// breathing irregularly, the link weakens and the number falls.
        ///
        /// The maths is the Pearson correlation coefficient. In words: for each of
        /// the last N frames we ask how far breath was from its own average, and how
        /// far heart rate was from its own average, and we multiply those two
        /// differences together. If the curves rise and fall together, the products
        /// are mostly positive and add up to something large. If they are unrelated,
        /// the products are randomly positive and negative and cancel out to almost
        /// nothing. Dividing by the two standard deviations at the end turns the
        /// result into a value between -1 and 1, independent of how large the
        /// signals themselves were.
        ///
        /// We take the absolute value, so -1 and +1 both count as "strongly linked".
        float EstimateCoherence()
        {
            // Too few samples to say anything. 0.5 means "no opinion".
            if (count < 32) return 0.5f;

            // First pass: the average of each signal.
            float mb = 0f, mh = 0f;
            for (int i = 0; i < count; i++) { mb += bBuf[i]; mh += hBuf[i]; }
            mb /= count; mh /= count;

            // Second pass: how much each signal varies (sbb, shh) and how much they
            // vary together (sbh).
            float sbb = 0f, shh = 0f, sbh = 0f;
            for (int i = 0; i < count; i++)
            {
                float db = bBuf[i] - mb, dh = hBuf[i] - mh;
                sbb += db * db; shh += dh * dh; sbh += db * dh;
            }

            // If either signal is completely flat, its variation is zero and the
            // division below would be a division by zero. A flat signal also tells
            // us nothing, so we return "no opinion".
            float denom = Mathf.Sqrt(sbb * shh);
            if (denom < 1e-6f) return 0.5f;

            return Mathf.Clamp01(Mathf.Abs(sbh / denom));
        }
    }
}