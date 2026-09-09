using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Endless scrolling environment made of independent rows. Each row keeps its
/// own pool of chunks moving past the plane, drawn at random from its own set
/// of variants, so you can run a detailed terrain row beside a distant ridge
/// row and a foreground debris row from one component.
///
/// Sources can be prefabs from the Project window or objects already sitting in
/// your scene. Scene objects are used as templates and hidden once the first
/// copy is made, so you can dress a row in the editor and hand it over.
/// </summary>
[DisallowMultipleComponent]
public class EndlessScroller : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        [Tooltip("Label for your own benefit. Shows on the row header in the inspector.")]
        public string name = "Row";

        [Tooltip("The variants this row chooses from. Prefabs or scene objects both work.")]
        public GameObject[] chunkPrefabs;

        [Tooltip("Shifts this row relative to the scroller object. Use Y for height " +
                 "and X to place the row sideways. Z only changes where the loop starts.")]
        public Vector3 offset;

        [Tooltip("How many chunks this row keeps alive. Three is the minimum that " +
                 "hides the recycling.")]
        public int activeChunks = 4;

        [Tooltip("Length of one chunk along the travel axis. Leave at 0 to measure " +
                 "it from the first variant of this row.")]
        public float chunkLength;

        [Tooltip("Empty distance left between one chunk and the next.")]
        public float spacing;

        [Tooltip("Scales the shared speed for this row. Below 1 for distant rows, " +
                 "above 1 for foreground rows. This is where the parallax comes from.")]
        public float speedMultiplier = 1f;

        [Tooltip("Stops the same variant appearing twice in a row.")]
        public bool avoidRepeats = true;

        [System.NonSerialized] public List<Chunk> chunks = new List<Chunk>();
        [System.NonSerialized] public List<List<Transform>> pools = new List<List<Transform>>();
        [System.NonSerialized] public float stride;
        [System.NonSerialized] public float stripLength;
        [System.NonSerialized] public int lastPicked = -1;
        [System.NonSerialized] public System.Random random;
        [System.NonSerialized] public bool ready;
    }

    public class Chunk
    {
        public Transform Transform;
        public int PrefabIndex;
    }

    [Header("Rows")]
    [SerializeField] private Row[] rows = new Row[1];

    [Header("Shared motion")]
    [Tooltip("Direction of travel for every row. Vector3.back brings chunks " +
             "toward the camera.")]
    [SerializeField] private Vector3 direction = Vector3.back;

    [Tooltip("Base speed in world units per second. Each row scales this by its " +
             "own multiplier.")]
    [SerializeField] private float speed = 40f;

    [Tooltip("How far past this object a chunk travels before it is recycled. " +
             "Increase it if chunks vanish while still on screen.")]
    [SerializeField] private float recycleMargin = 20f;

    [Tooltip("Seed for the shuffle. Leave at 0 for a different layout every run, " +
             "or set a value to get the same sequence each time while tuning. " +
             "Each row derives its own stream from this.")]
    [SerializeField] private int randomSeed;

    private Vector3 origin;

    /// <summary>Multiplies the speed of every row at runtime, e.g. for a boost.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    private void Start()
    {
        origin = transform.position;
        direction = direction.sqrMagnitude < 0.0001f ? Vector3.back : direction.normalized;

        if (rows == null || rows.Length == 0)
        {
            Debug.LogWarning($"{name}: no rows configured.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < rows.Length; i++)
        {
            SetUpRow(rows[i], i);
        }
    }

    private void Update()
    {
        float baseStep = speed * SpeedMultiplier * Time.deltaTime;

        for (int r = 0; r < rows.Length; r++)
        {
            Row row = rows[r];

            if (!row.ready)
            {
                continue;
            }

            Vector3 step = direction * (baseStep * row.speedMultiplier);
            Vector3 rowOrigin = origin + row.offset;

            for (int i = 0; i < row.chunks.Count; i++)
            {
                Chunk chunk = row.chunks[i];
                chunk.Transform.position += step;

                float passed = Vector3.Dot(chunk.Transform.position - rowOrigin, direction);

                if (passed > row.chunkLength + recycleMargin)
                {
                    Release(row, chunk);
                    Take(row, chunk);
                    Place(row, chunk, passed - row.stripLength);
                }
            }
        }
    }

    private void SetUpRow(Row row, int index)
    {
        if (row.chunkPrefabs == null || row.chunkPrefabs.Length == 0)
        {
            Debug.LogWarning($"{name}: row '{row.name}' has no sources and was skipped.", this);
            return;
        }

        row.activeChunks = Mathf.Max(2, row.activeChunks);
        row.spacing = Mathf.Max(0f, row.spacing);
        row.chunks = new List<Chunk>();
        row.pools = new List<List<Transform>>();
        row.random = randomSeed == 0
            ? new System.Random()
            : new System.Random(randomSeed + index * 7919);

        for (int i = 0; i < row.chunkPrefabs.Length; i++)
        {
            row.pools.Add(new List<Transform>());
        }

        if (row.chunkLength <= 0f)
        {
            row.chunkLength = MeasureFirstVariant(row);

            if (row.chunkLength <= 0f)
            {
                Debug.LogWarning($"{name}: could not measure row '{row.name}'. " +
                                 "Set its Chunk Length by hand.", this);
                return;
            }
        }

        // Measuring needs the source visible, so hide scene templates only now.
        HideSceneTemplates(row);

        row.stride = row.chunkLength + row.spacing;
        row.stripLength = row.stride * row.activeChunks;

        for (int i = 0; i < row.activeChunks; i++)
        {
            Chunk chunk = new Chunk();
            Take(row, chunk);
            Place(row, chunk, -i * row.stride);
            row.chunks.Add(chunk);
        }

        row.ready = true;
    }

    /// <summary>Puts a chunk at a given distance past its row origin.</summary>
    private void Place(Row row, Chunk chunk, float passed)
    {
        chunk.Transform.position = origin + row.offset + direction * passed;
    }

    /// <summary>Pulls a random variant out of the row's pool, creating one if needed.</summary>
    private void Take(Row row, Chunk chunk)
    {
        int index = PickVariant(row);
        List<Transform> pool = row.pools[index];

        Transform instance;

        if (pool.Count > 0)
        {
            instance = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
        }
        else
        {
            GameObject spawned = Instantiate(row.chunkPrefabs[index], transform);
            spawned.name = $"{row.name} · {row.chunkPrefabs[index].name}";
            instance = spawned.transform;
        }

        // Covers both pooled chunks and copies taken from a hidden scene template.
        instance.gameObject.SetActive(true);

        chunk.Transform = instance;
        chunk.PrefabIndex = index;
    }

    /// <summary>Parks a chunk back in its row's pool instead of destroying it.</summary>
    private void Release(Row row, Chunk chunk)
    {
        chunk.Transform.gameObject.SetActive(false);
        row.pools[chunk.PrefabIndex].Add(chunk.Transform);
    }

    private int PickVariant(Row row)
    {
        if (row.chunkPrefabs.Length == 1)
        {
            return 0;
        }

        int index = row.random.Next(row.chunkPrefabs.Length);

        if (row.avoidRepeats && index == row.lastPicked)
        {
            index = (index + 1 + row.random.Next(row.chunkPrefabs.Length - 1))
                    % row.chunkPrefabs.Length;
        }

        row.lastPicked = index;
        return index;
    }

    /// <summary>
    /// Switches off any source that lives in the scene rather than the Project
    /// window, so the original does not sit there motionless next to its copies.
    /// </summary>
    private void HideSceneTemplates(Row row)
    {
        for (int i = 0; i < row.chunkPrefabs.Length; i++)
        {
            GameObject source = row.chunkPrefabs[i];

            if (source != null && source.scene.IsValid())
            {
                source.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Measures a row's first variant along the travel axis using the combined
    /// bounds of every renderer inside it, then parks it in the pool for reuse.
    /// </summary>
    private float MeasureFirstVariant(Row row)
    {
        GameObject probe = Instantiate(row.chunkPrefabs[0], transform);
        probe.name = $"{row.name} · {row.chunkPrefabs[0].name}";
        probe.SetActive(true);

        Renderer[] renderers = probe.GetComponentsInChildren<Renderer>();
        float length = 0f;

        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 axis = new Vector3(
                Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));

            length = Vector3.Dot(bounds.size, axis);
        }

        probe.SetActive(false);
        row.pools[0].Add(probe.transform);

        return length;
    }

    private void OnDrawGizmosSelected()
    {
        if (rows == null)
        {
            return;
        }

        Vector3 axis = direction.sqrMagnitude < 0.0001f ? Vector3.back : direction.normalized;
        Vector3 root = Application.isPlaying ? origin : transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(root, 1f);

        for (int r = 0; r < rows.Length; r++)
        {
            Row row = rows[r];

            if (row == null)
            {
                continue;
            }

            Vector3 rowOrigin = root + row.offset;
            float step = row.chunkLength + Mathf.Max(0f, row.spacing);
            int count = Mathf.Max(2, row.activeChunks);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(root, rowOrigin);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(
                rowOrigin - axis * (step > 0f ? step * count : 100f),
                rowOrigin + axis * (row.chunkLength + recycleMargin));

            if (row.chunkLength <= 0f)
            {
                continue;
            }

            Gizmos.color = Color.green;
            for (int i = 0; i <= count; i++)
            {
                Vector3 start = rowOrigin - axis * (i * step);
                Vector3 end = start - axis * row.chunkLength;
                Gizmos.DrawLine(start + Vector3.left * 15f, start + Vector3.right * 15f);
                Gizmos.DrawLine(end + Vector3.left * 15f, end + Vector3.right * 15f);
            }
        }
    }
}
