using UnityEngine;

/// <summary>
/// Scrolls a background layer (cloud bank, distant ridge, prop) along a fixed
/// direction and wraps it after a set distance, so the scene never runs out.
/// Put one of these on each layer and give the far layers a lower speed to get
/// the parallax depth.
/// </summary>
[DisallowMultipleComponent]
public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("Direction the layer travels. Vector3.back moves it toward the camera.")]
    [SerializeField] private Vector3 direction = Vector3.back;

    [Tooltip("World units per second. Far layers should be slower than near ones.")]
    [SerializeField] private float speed = 12f;

    [Tooltip("Distance after which the layer snaps back to its start. " +
             "Set this to the length of one tile of the layer.")]
    [SerializeField] private float wrapDistance = 120f;

    [Tooltip("Spawns a second copy one wrap-length behind, so there is never a gap. " +
             "Turn this off if you have already placed the tiles by hand.")]
    [SerializeField] private bool autoDuplicate = true;

    [SerializeField, HideInInspector] private bool isClone;

    private Vector3 startPosition;
    private float travelled;

    /// <summary>Multiplies the scroll speed at runtime, e.g. for a boost effect.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    private void Start()
    {
        direction = direction.sqrMagnitude < 0.0001f ? Vector3.back : direction.normalized;
        wrapDistance = Mathf.Max(0.01f, wrapDistance);
        startPosition = transform.position;

        if (autoDuplicate && !isClone)
        {
            GameObject clone = Instantiate(
                gameObject,
                startPosition - direction * wrapDistance,
                transform.rotation,
                transform.parent);

            clone.name = gameObject.name + " (wrap)";

            // Start() has not run on the clone yet, so this flag lands in time.
            ParallaxLayer cloneLayer = clone.GetComponent<ParallaxLayer>();
            if (cloneLayer != null)
            {
                cloneLayer.isClone = true;
            }
        }
    }

    private void Update()
    {
        travelled += speed * SpeedMultiplier * Time.deltaTime;

        if (travelled >= wrapDistance)
        {
            travelled -= wrapDistance;
        }
        else if (travelled < 0f)
        {
            travelled += wrapDistance;
        }

        transform.position = startPosition + direction * travelled;
    }
}
