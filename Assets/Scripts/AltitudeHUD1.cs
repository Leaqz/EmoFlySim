using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vertical altitude gauge. The marker slides along the track, the optimal band
/// is positioned from the values on <see cref="Movement"/>, and both recolour
/// when the plane is inside the band.
///
/// Marker and zone must be children of the track RectTransform. This script
/// drives their anchors, so their own anchor values in the inspector do not
/// matter.
/// </summary>
[DisallowMultipleComponent]
public class AltitudeHUD : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private Movement flight;

    [Header("Gauge")]
    [Tooltip("The full-height background of the gauge. Marker and zone go inside it.")]
    [SerializeField] private RectTransform track;
    [SerializeField] private RectTransform marker;
    [SerializeField] private RectTransform optimalZone;


    [Header("Colours")]
    [SerializeField] private Graphic markerGraphic;
    [SerializeField] private Graphic zoneGraphic;
    [SerializeField] private Color inZoneColour = new Color(0.11f, 0.62f, 0.46f);
    [SerializeField] private Color outOfZoneColour = new Color(0.89f, 0.29f, 0.29f);

    [Tooltip("How fast the marker catches up to the real altitude. " +
             "A little lag makes the gauge feel mechanical.")]
    [SerializeField] private float markerDamping = 12f;

    [Header("Opacity")]
    [Range(0f, 1f)] [SerializeField] private float markerAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] private float zoneAlphaInside = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float zoneAlphaOutside = 0.22f;
    [Tooltip("Fades the entire gauge. Needs a CanvasGroup on this object.")]
    [Range(0f, 1f)] [SerializeField] private float hudAlpha = 1f;
    [SerializeField] private CanvasGroup canvasGroup;
    private float displayedAltitude;
    private bool zonePlaced;

    private void Reset()
    {
        flight = FindFirstObjectByType<Movement>();
    }

    private void Start()
    {
        if (flight != null)
        {
            displayedAltitude = flight.NormalizedAltitude;
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }

    private void Update()
    {
        if (flight == null)
        {
            return;
        }

        if (!zonePlaced && optimalZone != null)
        {
            SetVerticalSpan(optimalZone, flight.NormalizedOptimalMin, flight.NormalizedOptimalMax);
            zonePlaced = true;
        }

        displayedAltitude = Mathf.Lerp(
            displayedAltitude,
            flight.NormalizedAltitude,
            1f - Mathf.Exp(-markerDamping * Time.deltaTime));

        if (marker != null)
        {
            SetVerticalSpan(marker, displayedAltitude, displayedAltitude);
        }

        bool inZone = flight.InOptimalZone;

        if (markerGraphic != null)
        {
            Color c = inZone ? inZoneColour : outOfZoneColour;
            c.a = markerAlpha;
            markerGraphic.color = c;
        }

        if (zoneGraphic != null)
        {
            Color zone = inZoneColour;
            zone.a = inZone ? zoneAlphaInside : zoneAlphaOutside;
            zoneGraphic.color = zone;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = hudAlpha;
        }
    }

    /// <summary>
    /// Stretches a child between two normalised heights of its parent. Anchoring
    /// this way keeps the gauge correct at every screen resolution.
    /// </summary>
    private static void SetVerticalSpan(RectTransform rect, float from, float to)
    {
        float low = Mathf.Clamp01(Mathf.Min(from, to));
        float high = Mathf.Clamp01(Mathf.Max(from, to));

        rect.anchorMin = new Vector2(0f, low);
        rect.anchorMax = new Vector2(1f, high);
        rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
        rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
        rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
    }
}
