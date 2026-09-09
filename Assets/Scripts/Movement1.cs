using UnityEngine;
using UnityEngine.InputSystem;
using BioPlane.Bio;

/// <summary>
/// Level cruise, flown by the body. The plane holds its heading and climbs or
/// dives on the combined breath, heart rate and skin conductance signal coming
/// out of <see cref="AltitudeMixer"/>, trying to stay inside the optimal
/// altitude band shown on the HUD. The nose pitches with the climb rate so the
/// motion reads as flight rather than as a sliding sprite.
///
/// Keyboard, stick and a bound input action still work and take priority while
/// they are being pushed, which is what you want during tuning: you can shove
/// the plane somewhere and let go to see what the body does from there.
///
/// By default the transform keeps its starting X and Z and the world scrolls
/// past it (see ParallaxLayer and ScrollingGrid). Turn off
/// <c>holdPosition</c> if you would rather the plane actually travel forward.
/// </summary>
[DisallowMultipleComponent]
public class Movement : MonoBehaviour
{
    [Header("Cruise")]
    [Tooltip("Forward speed in metres per second. Used for the HUD readout, and " +
             "for real movement when Hold Position is off.")]
    [SerializeField] private float forwardSpeed = 60f;

    [Tooltip("On: the plane stays put and the background scrolls past it. " +
             "Off: the plane flies down its local +Z axis.")]
    [SerializeField] private bool holdPosition = true;

    [Header("Altitude")]
    [SerializeField] private float startAltitude = 25f;
    [SerializeField] private float minAltitude = 5f;
    [SerializeField] private float maxAltitude = 60f;

    [Tooltip("Maximum climb and dive rate in metres per second.")]
    [SerializeField] private float climbSpeed = 12f;

    [Tooltip("How quickly the climb rate reaches the input. Higher is snappier, " +
             "lower feels heavier.")]
    [SerializeField] private float climbSmoothing = 4f;

    [Header("Body control")]
    [Tooltip("Leave empty to find the BioRouter in the scene automatically.")]
    [SerializeField] private BioRouter bio;

    [Tooltip("Leave empty to take the AltitudeMixer sitting next to the router. " +
             "Without a mixer the plane flies on breath alone.")]
    [SerializeField] private AltitudeMixer mixer;

    [Tooltip("Off: the body is ignored and only keyboard, stick and the bound " +
             "action fly the plane.")]
    [SerializeField] private bool useBodyInput = true;

    [Tooltip("Keyboard, stick and the bound action override the body while they " +
             "are being pushed. Useful for tuning, turn off for a real session.")]
    [SerializeField] private bool allowManualOverride = true;

    [Tooltip("Fraction of the altitude band over which the ceiling and floor " +
             "ease in. The body cannot let go of the stick, so stopping dead at " +
             "the limits reads as the controls having broken.")]
    [Range(0.02f, 0.5f)]
    [SerializeField] private float softWallFraction = 0.15f;

    [Header("Optimal zone")]
    [SerializeField] private float optimalMin = 22f;
    [SerializeField] private float optimalMax = 38f;

    [Header("Attitude")]
    [Tooltip("Nose-up angle at full climb, in degrees. The nose drops by the " +
             "same amount at full dive.")]
    [SerializeField] private float maxPitchAngle = 18f;

    [Tooltip("Amplitude of the idle bob, in metres. Purely cosmetic, it does not " +
             "affect the altitude reading. Set to 0 to disable.")]
    [SerializeField] private float bobAmount = 0.15f;

    [SerializeField] private float bobSpeed = 1.6f;

    [Header("Input")]
    [Tooltip("Optional. A Value action of type Axis or Vector2. " +
             "Leave empty to fall back to W/S, arrow keys and the left stick.")]
    [SerializeField] private InputActionReference climbAction;

    [Tooltip("Correction for models whose nose does not point along local +Z. " +
         "Try X = 90 or X = -90 for nose-down, Y = 180 for backwards.")]
    [SerializeField] private Vector3 modelRotationOffset;

    private Vector3 anchor;
    private float altitude;
    private float verticalSpeed;
    private float distanceTravelled;
    private float bodyClimb;
    private bool manualActive;

    /// <summary>Current altitude in world units, ignoring the cosmetic bob.</summary>
    public float Altitude => altitude;

    /// <summary>Altitude remapped to 0..1 between min and max. For the HUD gauge.</summary>
    public float NormalizedAltitude => Mathf.InverseLerp(minAltitude, maxAltitude, altitude);

    /// <summary>Lower edge of the optimal band, as 0..1.</summary>
    public float NormalizedOptimalMin => Mathf.InverseLerp(minAltitude, maxAltitude, optimalMin);

    /// <summary>Upper edge of the optimal band, as 0..1.</summary>
    public float NormalizedOptimalMax => Mathf.InverseLerp(minAltitude, maxAltitude, optimalMax);

    /// <summary>True while the plane is inside the green band.</summary>
    public bool InOptimalZone => altitude >= optimalMin && altitude <= optimalMax;

    /// <summary>Point the camera frames. Excludes the bob so the shot stays steady.</summary>
    public Vector3 FocusPoint => new Vector3(
        transform.position.x, altitude, transform.position.z);

    /// <summary>Forward speed in metres per second.</summary>
    public float Speed => forwardSpeed;

    /// <summary>Climb rate in metres per second. Positive is up.</summary>
    public float VerticalSpeed => verticalSpeed;

    /// <summary>Climb rate as -1..1, for camera lean and effects.</summary>
    public float ClimbRatio => Mathf.Clamp(verticalSpeed / Mathf.Max(climbSpeed, 0.01f), -1f, 1f);

    /// <summary>The body's climb command as -1..1, before smoothing. For the HUD.</summary>
    public float BodyClimb => bodyClimb;

    /// <summary>True while a hand is overriding the body.</summary>
    public bool ManualOverrideActive => manualActive;

    /// <summary>The router actually in use, once resolved. May be null.</summary>
    public BioRouter Router => bio;

    /// <summary>The mixer actually in use, once resolved. May be null.</summary>
    public AltitudeMixer Mixer => mixer;

    private void Awake()
    {
        anchor = transform.position;
        altitude = Mathf.Clamp(startAltitude, minAltitude, maxAltitude);
        ResolveBio();
    }

    /// <summary>
    /// Finds the router and mixer so neither has to be dragged in. Both are
    /// optional: with no router the plane falls back to manual input, which keeps
    /// the scene playable while the sensors are unplugged.
    /// </summary>
    private void ResolveBio()
    {
        if (bio == null)
        {
#if UNITY_2023_1_OR_NEWER
            bio = Object.FindFirstObjectByType<BioRouter>();
#else
            bio = Object.FindObjectOfType<BioRouter>();
#endif
        }

        if (mixer == null && bio != null)
        {
            mixer = bio.GetComponent<AltitudeMixer>();
        }

        if (mixer == null)
        {
#if UNITY_2023_1_OR_NEWER
            mixer = Object.FindFirstObjectByType<AltitudeMixer>();
#else
            mixer = Object.FindObjectOfType<AltitudeMixer>();
#endif
        }
    }

    private void OnEnable()
    {
        if (climbAction != null && climbAction.action != null)
        {
            climbAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (climbAction != null && climbAction.action != null)
        {
            climbAction.action.Disable();
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        float target = ReadClimbInput() * climbSpeed;

        // Soft walls. A hand can release the stick at the ceiling; a body cannot,
        // so the command has to be faded out near the limits instead.
        float fade = Mathf.Max(1f, (maxAltitude - minAltitude) * softWallFraction);
        if (target > 0f)
        {
            target *= Mathf.InverseLerp(maxAltitude, maxAltitude - fade, altitude);
        }
        else if (target < 0f)
        {
            target *= Mathf.InverseLerp(minAltitude, minAltitude + fade, altitude);
        }

        verticalSpeed = Mathf.Lerp(verticalSpeed, target, 1f - Mathf.Exp(-climbSmoothing * dt));

        altitude += verticalSpeed * dt;

        if (altitude <= minAltitude)
        {
            altitude = minAltitude;
            verticalSpeed = Mathf.Max(0f, verticalSpeed);
        }
        else if (altitude >= maxAltitude)
        {
            altitude = maxAltitude;
            verticalSpeed = Mathf.Min(0f, verticalSpeed);
        }

        if (!holdPosition)
        {
            distanceTravelled += forwardSpeed * dt;
        }

        float bob = bobAmount * Mathf.Sin(Time.time * bobSpeed);

        transform.position = new Vector3(
            anchor.x,
            altitude + bob,
            anchor.z + distanceTravelled);

        // Unity's positive X rotation is nose-down, hence the minus sign.
        transform.rotation = Quaternion.Euler(-ClimbRatio * maxPitchAngle, 0f, 0f)
                     * Quaternion.Euler(modelRotationOffset);
    }

    /// <summary>
    /// Body first, hands second. The mixer already combines breath, heart rate and
    /// skin conductance into one -1..1 command and weights them by how fast each
    /// can actually be moved on purpose, so there is nothing to blend here.
    /// </summary>
    private float ReadClimbInput()
    {
        bodyClimb = 0f;

        if (useBodyInput)
        {
            if (mixer != null)
            {
                bodyClimb = Mathf.Clamp(mixer.Evaluate(), -1f, 1f);
            }
            else if (bio != null)
            {
                // No mixer: breath alone, centred so a half breath holds level.
                bodyClimb = Mathf.Clamp((bio.Frame.breath01 - 0.5f) * 2f, -1f, 1f);
            }
        }

        float manual = ReadManualInput();
        manualActive = allowManualOverride && Mathf.Abs(manual) > 0.01f;

        if (manualActive)
        {
            return manual;
        }

        return useBodyInput ? bodyClimb : manual;
    }

    private float ReadManualInput()
    {
        if (climbAction != null && climbAction.action != null && climbAction.action.enabled)
        {
            InputAction action = climbAction.action;

            if (action.expectedControlType == "Vector2")
            {
                return Mathf.Clamp(action.ReadValue<Vector2>().y, -1f, 1f);
            }

            return Mathf.Clamp(action.ReadValue<float>(), -1f, 1f);
        }

        float value = 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                value += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                value -= 1f;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null && Mathf.Abs(value) < 0.01f)
        {
            value = gamepad.leftStick.ReadValue().y;
        }

        return Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>Returns the plane to its starting height without reloading the scene.</summary>
    public void ResetAltitude()
    {
        altitude = Mathf.Clamp(startAltitude, minAltitude, maxAltitude);
        verticalSpeed = 0f;
    }

    private void OnValidate()
    {
        minAltitude = Mathf.Max(0f, minAltitude);
        maxAltitude = Mathf.Max(minAltitude + 1f, maxAltitude);
        optimalMin = Mathf.Clamp(optimalMin, minAltitude, maxAltitude);
        optimalMax = Mathf.Clamp(optimalMax, optimalMin, maxAltitude);
        startAltitude = Mathf.Clamp(startAltitude, minAltitude, maxAltitude);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? anchor : transform.position;
        Vector3 span = new Vector3(0f, 0f, 60f);

        Gizmos.color = Color.green;
        DrawBand(origin, optimalMin, span);
        DrawBand(origin, optimalMax, span);

        Gizmos.color = new Color(1f, 0.5f, 0.2f);
        DrawBand(origin, minAltitude, span);
        DrawBand(origin, maxAltitude, span);
    }

    private static void DrawBand(Vector3 origin, float height, Vector3 span)
    {
        Vector3 point = new Vector3(origin.x, height, origin.z);
        Gizmos.DrawLine(point - span, point + span);
    }
}