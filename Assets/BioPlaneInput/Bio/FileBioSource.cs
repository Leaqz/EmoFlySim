using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BioPlane.Bio
{
    /// Plays back recorded body data from a text file. This is the source to
    /// develop against: real sensors are slow to put on, drift between sessions and
    /// cannot be replayed, so tuning a flight model against live hardware wastes
    /// hours. Record one good session, then iterate against the file.
    ///
    /// Accepts CSV, semicolons, tabs or spaces. A header line naming the columns is
    /// optional but recommended. Recognised names:
    ///   t, time, timestamp, seconds
    ///   breath, resp, respiration
    ///   hr, bpm, heart, heart_rate
    ///   eda, gsr, scl, skin, conductance
    ///   ibi, rr
    /// Anything else is ignored. Without a time column the file is assumed to be
    /// evenly sampled at defaultSampleRate.
    ///
    ///
    /// WHAT NORMAL VALUES LOOK LIKE
    ///
    /// Units do not matter for breath and skin conductance, because BioRouter
    /// normalizes both against whatever range actually shows up in the session, so
    /// raw ADC counts work as well as calibrated physical units. The numbers below
    /// are for sanity checking a file, not for hard-coding thresholds anywhere.
    ///
    ///   t          seconds, ascending. Gaps are fine, the reader interpolates.
    ///              10 to 32 Hz sampling is plenty for all three channels.
    ///
    ///   breath     any scale. A chest belt on a 10 bit ADC gives roughly 0 to 1023,
    ///              a normalized signal gives 0 to 1. What matters is that inhaling
    ///              moves it in the positive direction; invert at the sensor if not.
    ///              Resting adult respiration is 12 to 20 breaths per minute, which
    ///              is a cycle every 3 to 5 seconds. Paced or meditative breathing
    ///              runs 5 to 7 per minute, a cycle every 9 to 12 seconds, and that
    ///              is the range this game is really tuned for.
    ///
    ///   hr         beats per minute. 60 to 100 is the textbook resting adult band,
    ///              50 to 90 is what you actually see sitting still, trained
    ///              endurance athletes drop into the 40s. Anything under 30 or over
    ///              200 is almost certainly a sensor artifact rather than a heart.
    ///              Rough ceiling under exertion is 220 minus age.
    ///
    ///   ibi / rr   the gap between beats, in milliseconds, as chest straps report
    ///              it. 300 to 2000 ms covers 30 to 200 bpm. Values under 10 are
    ///              read as seconds instead. Supplying ibi rather than hr is better
    ///              when you have it: heart rate variability can only be computed
    ///              from beat to beat intervals, and a strap that only reports an
    ///              averaged bpm has already thrown that away.
    ///
    ///   eda        microsiemens, the reciprocal of resistance in megaohms. 1 to 20
    ///              uS across people, 2 to 5 uS is a common resting level, and any
    ///              one person moves over maybe 2 to 5 uS within a session. Two
    ///              components matter and BioRouter separates them: the tonic level
    ///              drifts over tens of seconds and is sustained arousal, while
    ///              phasic responses rise 0.01 to 1 uS over 1 to 3 seconds and decay
    ///              over 5 to 30. A rise under about 0.01 uS is conventionally not
    ///              counted as a response at all.
    ///
    ///              Expect the level to climb for the first several minutes after
    ///              electrodes go on, as sweat accumulates underneath them. That is
    ///              not arousal. Between 5 and 25 percent of people produce almost
    ///              no phasic activity at all, so never build a mechanic that
    ///              requires a response to appear.
    ///
    /// A file whose numbers sit far outside these bands usually means a unit
    /// mix-up: kilohms instead of microsiemens, seconds instead of milliseconds, or
    /// a raw ADC count where a physical value was expected. The game will still
    /// play, because everything is normalized, but the readouts will read as
    /// nonsense and thresholds tuned on it will not transfer to real hardware.
    [AddComponentMenu("BioPlane/Bio/File Bio Source")]
    public class FileBioSource : BioSourceBase
    {
        [Header("Where the data comes from")]
        [Tooltip("Drag a .csv or .txt asset here. Takes priority over filePath.")]
        public TextAsset file;
        [Tooltip("Absolute path, or a name relative to StreamingAssets.")]
        public string filePath = "";

        [Header("Playback")]
        [Tooltip("Used only when the file has no time column. 10 to 32 Hz is typical.")]
        public float defaultSampleRate = 10f;
        public float playbackSpeed = 1f;
        public bool loop = true;
        public bool interpolate = true;

        // Named Row, not Sample: BioSourceBase already defines a Sample(float)
        // method, and C# will not allow a nested type and a member to share a name.
        struct Row
        {
            public float t, breath, hr, eda;
        }

        readonly List<Row> samples = new List<Row>();
        bool hasBreathCol, hasHrCol, hasEdaCol;
        float duration;
        float cursor;
        int index;
        float prevBreath = 0.5f;
        bool loaded;

        void Start() { Load(); }

        public override bool IsReady { get { return loaded && samples.Count > 1; } }

        public override void Recalibrate() { cursor = 0f; index = 0; }

        public void Load()
        {
            samples.Clear();
            loaded = false;

            string text = null;
            if (file != null) text = file.text;
            else if (!string.IsNullOrEmpty(filePath))
            {
                string p = Path.IsPathRooted(filePath)
                    ? filePath
                    : Path.Combine(Application.streamingAssetsPath, filePath);
                if (File.Exists(p)) text = File.ReadAllText(p);
                else Debug.LogWarning("[BioPlane] Bio file not found: " + p);
            }
            if (string.IsNullOrEmpty(text)) return;

            Parse(text);
            loaded = samples.Count > 1;
            if (loaded)
            {
                duration = samples[samples.Count - 1].t;
                Debug.Log("[BioPlane] Loaded " + samples.Count + " bio samples, " +
                          duration.ToString("0.0") + " s (breath " + hasBreathCol +
                          ", heart " + hasHrCol + ", eda " + hasEdaCol + ")");
            }
        }

        void Parse(string text)
        {
            string[] lines = text.Split('\n');
            int cT = -1, cBreath = -1, cHr = -1, cEda = -1, cIbi = -1;
            bool headerDone = false;
            float dt = 1f / Mathf.Max(0.01f, defaultSampleRate);
            int n = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(new char[] { ',', ';', '\t', ' ' },
                                            StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (!headerDone)
                {
                    headerDone = true;
                    float probe;
                    bool numeric = float.TryParse(parts[0], NumberStyles.Float,
                                                  CultureInfo.InvariantCulture, out probe);
                    if (!numeric)
                    {
                        for (int c = 0; c < parts.Length; c++)
                        {
                            string k = parts[c].Trim().ToLowerInvariant();
                            if (k == "t" || k == "time" || k == "timestamp" || k == "seconds") cT = c;
                            else if (k.StartsWith("breath") || k.StartsWith("resp")) cBreath = c;
                            else if (k == "hr" || k == "bpm" || k.StartsWith("heart")) cHr = c;
                            else if (k == "eda" || k == "gsr" || k == "scl" ||
                                     k.StartsWith("skin") || k.StartsWith("conduct")) cEda = c;
                            else if (k == "ibi" || k == "rr") cIbi = c;
                        }
                        continue;
                    }
                    // No header: assume t, breath, hr, eda in that order.
                    cT = 0;
                    if (parts.Length > 1) cBreath = 1;
                    if (parts.Length > 2) cHr = 2;
                    if (parts.Length > 3) cEda = 3;
                }

                Row s = new Row();
                s.t = cT >= 0 && cT < parts.Length ? Num(parts[cT]) : n * dt;
                s.breath = cBreath >= 0 && cBreath < parts.Length ? Num(parts[cBreath]) : 0f;
                s.hr = cHr >= 0 && cHr < parts.Length ? Num(parts[cHr]) : 0f;
                if (s.hr <= 0f && cIbi >= 0 && cIbi < parts.Length)
                {
                    // Straps report the beat gap in milliseconds; anything under 10
                    // is assumed to be seconds instead.
                    float ibi = Num(parts[cIbi]);
                    if (ibi > 10f) ibi /= 1000f;
                    if (ibi > 0.2f) s.hr = 60f / ibi;
                }
                s.eda = cEda >= 0 && cEda < parts.Length ? Num(parts[cEda]) : 0f;

                samples.Add(s);
                n++;
            }

            hasBreathCol = cBreath >= 0;
            hasHrCol = cHr >= 0 || cIbi >= 0;
            hasEdaCol = cEda >= 0;

            // Normalise the time base to start at zero.
            if (samples.Count > 0)
            {
                float t0 = samples[0].t;
                for (int i = 0; i < samples.Count; i++)
                {
                    Row s = samples[i];
                    s.t -= t0;
                    samples[i] = s;
                }
            }
        }

        static float Num(string s)
        {
            float v;
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        public override BioFrame Sample(float dt)
        {
            BioFrame f = BioFrame.Neutral;
            if (!IsReady) return f;

            cursor += dt * playbackSpeed;
            bool wrapped = false;
            if (cursor > duration)
            {
                if (!loop) { cursor = duration; }
                else { cursor -= duration; index = 0; wrapped = true; }
            }

            while (index < samples.Count - 2 && samples[index + 1].t <= cursor) index++;
            while (index > 0 && samples[index].t > cursor) index--;

            Row a = samples[index];
            Row b = samples[Mathf.Min(index + 1, samples.Count - 1)];
            float span = Mathf.Max(1e-5f, b.t - a.t);
            float k = interpolate ? Mathf.Clamp01((cursor - a.t) / span) : 0f;

            float breath = Mathf.Lerp(a.breath, b.breath, k);
            float hr = Mathf.Lerp(a.hr, b.hr, k);
            float eda = Mathf.Lerp(a.eda, b.eda, k);

            // Without this the loop point produces one enormous breath velocity
            // spike, which the flight model reads as a violent inhale.
            if (wrapped) prevBreath = breath;

            f.hasBreath = hasBreathCol;
            f.breath01 = breath;
            f.breathVelocity = dt > 0f ? (breath - prevBreath) / dt : 0f;
            prevBreath = breath;

            f.hasHeart = hasHrCol && hr > 0f;
            f.heartRateBpm = hr;

            f.hasSkin = hasEdaCol;
            f.skinConductanceUs = eda;

            return f;
        }
    }
}