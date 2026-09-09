using System.Collections.Generic;
using UnityEngine;
using BioPlane.Util;

namespace BioPlane.Bio
{
    /// Collects every BioSource in the scene, picks the best one per channel,
    /// normalizes and smooths the result, and derives the high level values the
    /// rest of the game plays with.
    ///
    /// Skin conductance is split here rather than in the sources, so a source only
    /// has to report raw microsiemens. The split is the standard one: a very slow
    /// low pass gives the tonic level, which is general arousal, and what is left
    /// over is the phasic component, the short responses that follow a startle, an
    /// effort or a thought by one to three seconds.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("BioPlane/Bio/Bio Router")]
    public class BioRouter : MonoBehaviour
    {
        public List<BioSourceBase> sources = new List<BioSourceBase>();
        public bool autoCollectSources = true;

        [Header("Smoothing (seconds)")]
        [Tooltip("Breath must stay responsive or the plane feels disconnected from the body.")]
        public float breathSmoothing = 0.12f;
        [Tooltip("Heart rate is a slow signal, so smooth it hard.")]
        public float heartSmoothing = 2.0f;
        public float skinSmoothing = 0.5f;

        [Header("Skin conductance")]
        [Tooltip("Time constant of the tonic baseline, in seconds. 20 to 60 is normal.")]
        public float tonicTau = 25f;
        [Tooltip("Microsiemens above baseline that counts as a full phasic response.")]
        public float phasicScale = 0.35f;
        [Tooltip("Phasic level that counts as a response, for the per-minute count.")]
        public float scrThreshold = 0.35f;

        [Header("Calibration")]
        public float calibrationSeconds = 20f;
        public AdaptiveNormalizer breathNorm = new AdaptiveNormalizer { adaptRate = 0.04f };
        public AdaptiveNormalizer heartNorm = new AdaptiveNormalizer { adaptRate = 0.01f };
        public AdaptiveNormalizer skinNorm = new AdaptiveNormalizer { adaptRate = 0.008f };

        [Header("Arousal mix")]
        [Range(0f, 1f)] public float arousalFromSkin = 0.45f;
        [Range(0f, 1f)] public float arousalFromPhasic = 0.15f;
        [Range(0f, 1f)] public float arousalFromHeart = 0.30f;
        [Range(0f, 1f)] public float arousalFromIncoherence = 0.10f;

        public BioFrame Frame { get; private set; }
        /// 0 = calm, 1 = activated. Drives speed, turbulence and colour.
        public float Arousal01 { get; private set; }
        public float Coherence01 { get; private set; }
        public float ScrPerMinute { get; private set; }
        public bool Calibrating { get { return Time.time - calibStart < calibrationSeconds; } }

        float calibStart;
        float breathVel, heartVel, skinVel;
        float smBreath = 0.5f, smHeart = 70f, smSkin, prevBreath = 0.5f;
        float tonicBaseline;
        bool skinPrimed;
        float lastScrTime = -99f;
        readonly Queue<float> scrTimes = new Queue<float>();

        const int N = 256;
        readonly float[] bBuf = new float[N];
        readonly float[] hBuf = new float[N];
        int idx, count;

        void Awake()
        {
            Frame = BioFrame.Neutral;
            Arousal01 = 0.5f;
            Coherence01 = 0.5f;
            if (autoCollectSources)
            {
                BioSourceBase[] found = GetComponentsInChildren<BioSourceBase>(true);
                foreach (BioSourceBase s in found) if (!sources.Contains(s)) sources.Add(s);
            }
            Recalibrate();
        }

        void Update()
        {
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            if (InputCompat.KeyDown("c")) Recalibrate();

            BioFrame merged = BioFrame.Neutral;
            int bestBreath = int.MinValue, bestHeart = int.MinValue, bestSkin = int.MinValue;

            for (int i = 0; i < sources.Count; i++)
            {
                BioSourceBase s = sources[i];
                if (s == null || !s.isActiveAndEnabled || !s.IsReady) continue;
                BioFrame f = s.Sample(dt);

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

            // ---- breath -----------------------------------------------------------
            float b = merged.hasBreath ? breathNorm.Normalize(merged.breath01, dt) : 0.5f;
            smBreath = Mathf.SmoothDamp(smBreath, b, ref breathVel, breathSmoothing);
            merged.breath01 = smBreath;
            merged.breathVelocity = (smBreath - prevBreath) / dt;
            prevBreath = smBreath;

            // ---- heart ---------------------------------------------------------------
            if (merged.hasHeart)
            {
                smHeart = Mathf.SmoothDamp(smHeart, merged.heartRateBpm, ref heartVel, heartSmoothing);
                merged.heartRateBpm = smHeart;
                merged.heartRate01 = heartNorm.Normalize(smHeart, dt);
            }
            else merged.heartRate01 = 0.5f;

            // ---- skin conductance -------------------------------------------------------
            if (merged.hasSkin)
            {
                float raw = merged.skinConductanceUs;
                smSkin = Mathf.SmoothDamp(smSkin, raw, ref skinVel, skinSmoothing);

                if (!skinPrimed) { tonicBaseline = smSkin; skinPrimed = true; }
                tonicBaseline = Mathf.Lerp(tonicBaseline, smSkin, 1f - Mathf.Exp(-dt / Mathf.Max(1f, tonicTau)));

                merged.skinConductanceUs = smSkin;
                merged.skin01 = skinNorm.Normalize(tonicBaseline, dt);
                merged.skinPhasic01 = Mathf.Clamp01((smSkin - tonicBaseline) / Mathf.Max(0.001f, phasicScale));

                if (merged.skinPhasic01 > scrThreshold && Time.time - lastScrTime > 3f)
                {
                    lastScrTime = Time.time;
                    scrTimes.Enqueue(Time.time);
                }
                while (scrTimes.Count > 0 && Time.time - scrTimes.Peek() > 60f) scrTimes.Dequeue();
                ScrPerMinute = scrTimes.Count;
            }
            else
            {
                merged.skin01 = 0.5f;
                merged.skinPhasic01 = 0f;
            }

            // ---- derived ------------------------------------------------------------------
            Push(merged.breath01, merged.heartRate01);
            merged.coherence01 = EstimateCoherence();
            Coherence01 = merged.coherence01;

            float wSkin = merged.hasSkin ? arousalFromSkin : 0f;
            float wPha = merged.hasSkin ? arousalFromPhasic : 0f;
            float wHr = merged.hasHeart ? arousalFromHeart : 0f;
            float wCoh = arousalFromIncoherence;
            float wSum = wSkin + wPha + wHr + wCoh;
            if (wSum < 1e-4f) Arousal01 = 0.5f;
            else Arousal01 = Mathf.Clamp01((merged.skin01 * wSkin
                                          + merged.skinPhasic01 * wPha
                                          + merged.heartRate01 * wHr
                                          + (1f - merged.coherence01) * wCoh) / wSum);

            Frame = merged;
        }

        public void Recalibrate()
        {
            calibStart = Time.time;
            breathNorm.Reset();
            heartNorm.Reset();
            skinNorm.Reset();
            skinPrimed = false;
            scrTimes.Clear();
            count = 0; idx = 0;
            for (int i = 0; i < sources.Count; i++) if (sources[i] != null) sources[i].Recalibrate();
        }

        void Push(float b, float h)
        {
            bBuf[idx] = b; hBuf[idx] = h;
            idx = (idx + 1) % N;
            if (count < N) count++;
        }

        /// Absolute Pearson correlation between the breath curve and the heart rate
        /// curve over the last few seconds. When someone breathes slowly and evenly,
        /// heart rate rises on the inhale and falls on the exhale, the two curves
        /// lock together, and this value climbs.
        float EstimateCoherence()
        {
            if (count < 32) return 0.5f;
            float mb = 0f, mh = 0f;
            for (int i = 0; i < count; i++) { mb += bBuf[i]; mh += hBuf[i]; }
            mb /= count; mh /= count;

            float sbb = 0f, shh = 0f, sbh = 0f;
            for (int i = 0; i < count; i++)
            {
                float db = bBuf[i] - mb, dh = hBuf[i] - mh;
                sbb += db * db; shh += dh * dh; sbh += db * dh;
            }
            float denom = Mathf.Sqrt(sbb * shh);
            if (denom < 1e-6f) return 0.5f;
            return Mathf.Clamp01(Mathf.Abs(sbh / denom));
        }
    }
}
