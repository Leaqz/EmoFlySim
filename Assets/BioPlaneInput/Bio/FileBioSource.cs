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
    /// Units do not matter for breath and EDA: BioRouter normalizes both against
    /// whatever range actually shows up, so raw ADC counts work as well as
    /// microsiemens.
    [AddComponentMenu("BioPlane/Bio/File Bio Source")]
    public class FileBioSource : BioSourceBase
    {
        [Header("Where the data comes from")]
        [Tooltip("Drag a .csv or .txt asset here. Takes priority over filePath.")]
        public TextAsset file;
        [Tooltip("Absolute path, or a name relative to StreamingAssets.")]
        public string filePath = "";

        [Header("Playback")]
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