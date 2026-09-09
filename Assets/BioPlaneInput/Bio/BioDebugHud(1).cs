using UnityEngine;

namespace BioPlane.Bio
{
    /// Draws the body signals over the Game view so you can see what the input
    /// layer is actually producing. Put it on the same GameObject as BioRouter and
    /// AltitudeMixer; it finds both by itself.
    ///
    /// Every visual property is a serialized field, and IMGUI redraws from scratch
    /// each frame, so anything changed in the inspector while the game is running
    /// takes effect immediately. That is the fastest way to dial this in: press
    /// Play, drag the sliders, then copy the component values with the context menu
    /// and paste them back after leaving Play mode.
    ///
    /// Scaling works by pushing one matrix onto the GUI stack and then laying
    /// everything out in fixed design units. Text, bars and spacing all scale
    /// together, which is what you want, and it means the layout code below never
    /// has to think about resolution.
    ///
    /// This is an OnGUI overlay, so it appears in the Game view and in a desktop
    /// build but NOT in a VR headset. Unity's immediate mode GUI is not rendered to
    /// an HMD at all.
    [AddComponentMenu("BioPlane/Bio/Bio Debug HUD")]
    public class BioDebugHud : MonoBehaviour
    {
        public enum Anchor
        {
            TopLeft, TopCentre, TopRight,
            MiddleLeft, Centre, MiddleRight,
            BottomLeft, BottomCentre, BottomRight
        }

        public enum ScaleMode
        {
            /// One design unit is one pixel, whatever the resolution.
            Fixed,
            /// Grows and shrinks with window height. The usual choice.
            WithHeight,
            WithWidth,
            /// Never lets the HUD overflow either axis of an odd window shape.
            WithShorterSide
        }

        [Header("Sources")]
        public BioRouter bio;
        public AltitudeMixer mixer;

        [Header("Placement")]
        public Anchor anchor = Anchor.TopLeft;
        [Tooltip("Distance from the anchored corner, in design units.")]
        public Vector2 margin = new Vector2(20f, 20f);

        [Header("Scale")]
        public ScaleMode scaleMode = ScaleMode.WithHeight;
        [Range(0.2f, 5f)] public float scale = 1f;
        [Tooltip("Window size at which scale 1 means actual size.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("Keeps the HUD readable on small windows and sane on huge ones.")]
        public Vector2 scaleClamp = new Vector2(0.4f, 3f);

        [Header("Layout, in design units")]
        public float padding = 12f;
        public float rowHeight = 30f;
        public float columnGap = 10f;
        public float labelWidth = 70f;
        public float levelBarWidth = 90f;
        public float valueWidth = 150f;
        public float contributionBarWidth = 90f;
        public float barHeight = 9f;

        [Header("Type")]
        [Tooltip("Optional. Leave empty for Unity's default GUI font.")]
        public Font font;
        public int titleFontSize = 15;
        public int rowFontSize = 12;
        public FontStyle titleFontStyle = FontStyle.Normal;

        [Header("Colours")]
        [Range(0f, 1f)] public float masterAlpha = 1f;
        public Color panelColour = new Color(0f, 0f, 0f, 0.55f);
        public Color textColour = Color.white;
        public Color trackColour = new Color(1f, 1f, 1f, 0.15f);
        public Color ruleColour = new Color(1f, 1f, 1f, 0.25f);
        public Color breathColour = new Color(0.5f, 0.85f, 1f, 0.95f);
        public Color heartColour = new Color(1f, 0.45f, 0.5f, 0.95f);
        public Color skinColour = new Color(1f, 0.82f, 0.35f, 0.95f);
        public Color combinedColour = new Color(0.7f, 1f, 0.6f, 0.95f);

        [Header("What to show")]
        public bool show = true;
        public bool showActiveSource = true;
        public bool showColumnHeader = true;
        public bool showBreath = true;
        public bool showHeart = true;
        public bool showSkin = true;
        public bool showCombined = true;
        public bool showDerivedLine = true;
        public bool showKeyHints = true;
        public bool showLevelBars = true;
        public bool showContributionBars = true;

        [Header("Keys")]
        public KeyCode toggleKey = KeyCode.H;
        public KeyCode recalibrateKey = KeyCode.C;

        GUIStyle title, row;
        Texture2D white;
        int styleStamp = -1;
        float cursorY;
        float panelWidth;

        void Awake()
        {
            if (bio == null) bio = GetComponent<BioRouter>();
            if (mixer == null) mixer = GetComponent<AltitudeMixer>();
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void Update()
        {
            if (mixer != null) mixer.Evaluate();
        }

        /// Flips the overlay. Public so it can also be driven from a UI button or an
        /// InputAction, which is what you want once the project has a real input map.
        public void Toggle() { show = !show; }

        // ---- scale and placement ------------------------------------------------

        float EffectiveScale()
        {
            float s = scale;
            switch (scaleMode)
            {
                case ScaleMode.WithHeight:
                    s *= Screen.height / Mathf.Max(1f, referenceResolution.y);
                    break;
                case ScaleMode.WithWidth:
                    s *= Screen.width / Mathf.Max(1f, referenceResolution.x);
                    break;
                case ScaleMode.WithShorterSide:
                    s *= Mathf.Min(Screen.width / Mathf.Max(1f, referenceResolution.x),
                                   Screen.height / Mathf.Max(1f, referenceResolution.y));
                    break;
            }
            return Mathf.Clamp(s, scaleClamp.x, scaleClamp.y);
        }

        /// Top-left corner of the panel in screen pixels, given where it is anchored
        /// and how big it ends up once scaled.
        Vector2 Origin(float s, float w, float h)
        {
            float pw = w * s, ph = h * s;
            float mx = margin.x * s, my = margin.y * s;
            float x, y;

            switch (anchor)
            {
                case Anchor.TopCentre:
                case Anchor.Centre:
                case Anchor.BottomCentre: x = (Screen.width - pw) * 0.5f; break;
                case Anchor.TopRight:
                case Anchor.MiddleRight:
                case Anchor.BottomRight: x = Screen.width - pw - mx; break;
                default: x = mx; break;
            }

            switch (anchor)
            {
                case Anchor.MiddleLeft:
                case Anchor.Centre:
                case Anchor.MiddleRight: y = (Screen.height - ph) * 0.5f; break;
                case Anchor.BottomLeft:
                case Anchor.BottomCentre:
                case Anchor.BottomRight: y = Screen.height - ph - my; break;
                default: y = my; break;
            }

            return new Vector2(x, y);
        }

        // ---- keys ------------------------------------------------------------------

        /// <summary>
        /// Key handling goes through the IMGUI event queue rather than through
        /// Input.GetKeyDown, because this project uses the new Input System and the
        /// old static Input class throws when the legacy backend is disabled. IMGUI
        /// events are fed by both backends, so this needs no conditional compilation.
        ///
        /// It runs before the early return below on purpose. Handling keys after it
        /// would let you hide the HUD and never get it back.
        /// </summary>
        void HandleKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            if (e.keyCode == toggleKey) { Toggle(); e.Use(); }
            else if (e.keyCode == recalibrateKey && bio != null) { bio.Recalibrate(); e.Use(); }
        }

        // ---- drawing -------------------------------------------------------------------

        void OnGUI()
        {
            HandleKeys();
            if (!show || bio == null) return;

            EnsureStyles();

            BioFrame f = bio.Frame;
            bool calibrating = bio.Calibrating;

            panelWidth = padding * 2f + labelWidth + columnGap
                       + (showLevelBars ? levelBarWidth + columnGap : 0f)
                       + valueWidth + columnGap
                       + (showContributionBars ? contributionBarWidth : 0f);

            float panelHeight = padding * 2f + CountRows(calibrating) * rowHeight;

            float s = EffectiveScale();
            Vector2 origin = Origin(s, panelWidth, panelHeight);

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(s, s, 1f));

            // From here down everything is in design units with the panel at 0,0.
            Box(new Rect(0f, 0f, panelWidth, panelHeight), panelColour);

            cursorY = padding;

            if (showActiveSource) Line(ActiveSourceName(), row);
            if (showColumnHeader) Line("channel        level" + (showLevelBars ? "" : "  ")
                                       + "              lift", row);

            if (showBreath)
                Channel("breath", f.breath01,
                        f.hasBreath ? f.breath01.ToString("0.00") + "   " +
                                      f.breathRateBpm.ToString("0.0") + "/min" : "no source",
                        mixer != null ? mixer.BreathContribution : 0f, breathColour);

            if (showHeart)
                Channel("heart", f.heartRate01,
                        f.hasHeart ? f.heartRateBpm.ToString("0") + " bpm   hrv " +
                                     f.hrvMs.ToString("0") : "no source",
                        mixer != null ? mixer.HeartContribution : 0f, heartColour);

            if (showSkin)
                Channel("skin", f.skin01,
                        f.hasSkin ? f.skinConductanceUs.ToString("0.00") + " uS" +
                                    (f.skinPhasic01 > 0.25f ? "   response!" : "") : "no source",
                        mixer != null ? mixer.SkinContribution + mixer.PhasicContribution : 0f,
                        skinColour);

            if (showCombined)
            {
                float climb = mixer != null ? mixer.Climb : 0f;
                Box(new Rect(padding, cursorY + 2f, panelWidth - padding * 2f, 1f), ruleColour);
                Channel("COMBINED", -1f, (climb >= 0f ? "+" : "") + climb.ToString("0.00"),
                        climb, combinedColour);
            }

            if (showDerivedLine)
                Line("arousal " + bio.Arousal01.ToString("0.00") +
                     "     coherence " + f.coherence01.ToString("0.00"), row);

            if (showKeyHints)
                Line(recalibrateKey + " recalibrate     " + toggleKey +
                     " hide     up/down fake a breath", row);

            if (calibrating) Line("calibrating, breathe normally", title);

            GUI.matrix = saved;
        }

        int CountRows(bool calibrating)
        {
            int n = 0;
            if (showActiveSource) n++;
            if (showColumnHeader) n++;
            if (showBreath) n++;
            if (showHeart) n++;
            if (showSkin) n++;
            if (showCombined) n++;
            if (showDerivedLine) n++;
            if (showKeyHints) n++;
            if (calibrating) n++;
            return Mathf.Max(1, n);
        }

        string ActiveSourceName()
        {
            string src = "no source";
            for (int i = 0; i < bio.sources.Count; i++)
            {
                BioSourceBase s = bio.sources[i];
                if (s != null && s.isActiveAndEnabled && s.IsReady) src = s.GetType().Name;
            }
            return src;
        }

        void Line(string text, GUIStyle style)
        {
            GUI.Label(new Rect(padding, cursorY, panelWidth - padding * 2f, rowHeight), text, style);
            cursorY += rowHeight;
        }

        /// One row: name, an optional 0..1 level bar, a text value, and an optional
        /// signed bar showing how much this channel pushes the output up or down.
        /// The signed bar is the point of the whole display: it answers "why is the
        /// value moving".
        void Channel(string name, float level, string value, float contribution, Color c)
        {
            float x = padding;
            float mid = cursorY + (rowHeight - barHeight) * 0.5f;

            GUI.Label(new Rect(x, cursorY, labelWidth, rowHeight), name, row);
            x += labelWidth + columnGap;

            if (showLevelBars)
            {
                if (level >= 0f)
                {
                    Rect bar = new Rect(x, mid, levelBarWidth, barHeight);
                    Box(bar, trackColour);
                    Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(level), bar.height), c);
                }
                x += levelBarWidth + columnGap;
            }

            GUI.Label(new Rect(x, cursorY, valueWidth, rowHeight), value, row);
            x += valueWidth + columnGap;

            if (showContributionBars)
            {
                Rect cr = new Rect(x, mid, contributionBarWidth, barHeight);
                Box(cr, trackColour);
                float centre = cr.x + cr.width * 0.5f;
                Box(new Rect(centre - 0.5f, cr.y - 2f, 1f, cr.height + 4f), ruleColour);
                float len = Mathf.Clamp(contribution, -1f, 1f) * cr.width * 0.5f;
                Box(new Rect(len >= 0f ? centre : centre + len, cr.y, Mathf.Abs(len), cr.height), c);
            }

            cursorY += rowHeight;
        }

        void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = Fade(c);
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        Color Fade(Color c) { c.a *= masterAlpha; return c; }

        /// Styles are cached because building a GUIStyle every frame is wasteful,
        /// and rebuilt whenever any property that feeds them changes, so editing
        /// font size or colour in the inspector during Play still updates live.
        void EnsureStyles()
        {
            int stamp = titleFontSize * 31 + rowFontSize * 7 + (int)titleFontStyle
                      + (font != null ? font.GetHashCode() : 0)
                      + Fade(textColour).GetHashCode();
            if (title != null && stamp == styleStamp) return;
            styleStamp = stamp;

            title = new GUIStyle(GUI.skin.label);
            title.fontSize = titleFontSize;
            title.fontStyle = titleFontStyle;
            title.normal.textColor = Fade(textColour);
            title.alignment = TextAnchor.MiddleLeft;
            if (font != null) title.font = font;

            row = new GUIStyle(title);
            row.fontSize = rowFontSize;
            row.fontStyle = FontStyle.Normal;
        }

        void OnValidate()
        {
            padding = Mathf.Max(0f, padding);
            rowHeight = Mathf.Max(8f, rowHeight);
            columnGap = Mathf.Max(0f, columnGap);
            labelWidth = Mathf.Max(10f, labelWidth);
            levelBarWidth = Mathf.Max(10f, levelBarWidth);
            valueWidth = Mathf.Max(10f, valueWidth);
            contributionBarWidth = Mathf.Max(10f, contributionBarWidth);
            barHeight = Mathf.Max(1f, barHeight);
            titleFontSize = Mathf.Max(6, titleFontSize);
            rowFontSize = Mathf.Max(6, rowFontSize);
            scaleClamp.x = Mathf.Max(0.05f, scaleClamp.x);
            scaleClamp.y = Mathf.Max(scaleClamp.x, scaleClamp.y);
            styleStamp = -1;
        }
    }
}