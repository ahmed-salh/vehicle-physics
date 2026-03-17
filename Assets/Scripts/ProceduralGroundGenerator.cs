using System.Collections.Generic;
using UnityEngine;

public class ProceduralGroundGenerator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────

    [Header("Targets")]
    [Tooltip("All transforms to follow (one per car). Auto-found if empty.")]
    public Transform[] targets;

    [Header("Chunk Settings")]
    [Tooltip("Size of each square chunk in world units.")]
    public float chunkSize = 40f;

    [Tooltip("How many chunks to keep visible in each direction from the car. " +
             "Total pool = (2*viewRadius+1)^2.")]
    [Range(1, 6)] public int viewRadius = 3;

    [Tooltip("Number of mesh subdivisions per chunk edge. " +
             "Higher = smoother noise but more vertices.")]
    [Range(1, 20)] public int subdivisions = 8;

    [Header("Terrain Noise")]
    [Tooltip("Horizontal scale of the Perlin noise. Larger = gentler hills.")]
    public float noiseScale = 0.025f;

    [Tooltip("Height amplitude. Set to 0 for a perfectly flat surface.")]
    public float noiseAmplitude = 0.6f;

    [Tooltip("Noise seed — change to get a different landscape.")]
    public float noiseSeed = 42.3f;

    [Header("Visuals")]
    [Tooltip("Material applied to every chunk. Leave null for auto-generated grey.")]
    public Material groundMaterial;

    [Tooltip("Tint applied to the ground mesh colour. Only used if no material is assigned.")]
    public Color groundColor = new Color(0.22f, 0.22f, 0.22f);



    // ─────────────────────────────────────────────────────────
    //  Runtime
    // ─────────────────────────────────────────────────────────

    private class Chunk
    {
        public GameObject go;
        public MeshFilter mf;
        public MeshRenderer mr;
        public MeshCollider mc;     // must be updated every time the mesh changes
        public Vector2Int coord;
    }

    private readonly Dictionary<Vector2Int, Chunk> _active = new Dictionary<Vector2Int, Chunk>();
    private readonly Queue<Chunk> _pool = new Queue<Chunk>();

    private readonly System.Collections.Generic.Dictionary<Transform, Vector2Int> _targetLastCoords
        = new System.Collections.Generic.Dictionary<Transform, Vector2Int>();
    private Material _mat;

    // ─────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (targets == null || targets.Length == 0)
        {
            var vcs = FindObjectsByType<VehicleController>(FindObjectsSortMode.None);
            targets = new Transform[vcs.Length];
            for (int i = 0; i < vcs.Length; i++)
                targets[i] = vcs[i].transform;
        }

        // Build shared material
        if (groundMaterial != null)
        {
            _mat = groundMaterial;
        }
        else
        {
            // "Standard" is always present; fall back to Unlit/Color on SRP
            Shader sh = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            _mat = new Material(sh) { color = groundColor };
        }

        // Pre-warm the pool with enough chunks to fill the view radius
        int side = viewRadius * 2 + 1;
        int poolCount = side * side;
        for (int p = 0; p < poolCount; p++)
            _pool.Enqueue(CreateChunk());
    }

    private void Start()
    {
        // Force-build all chunks under the car before the first FixedUpdate.
        // Without this the car spawns above empty space and falls through.
        if (targets != null && targets.Length > 0)
        {
            // Prime the _targetLastCoords so Update can detect future moves
            foreach (var t in targets)
                if (t != null)
                    _targetLastCoords[t] = WorldToCoord(t.position);

            RefreshAll();                  // load the union of all targets at once
            Physics.SyncTransforms();
        }
    }

    private void Update()
    {
        if (targets == null || targets.Length == 0) return;

        // Check whether any target has crossed a chunk boundary
        bool anyMoved = false;
        foreach (var t in targets)
        {
            if (t == null) continue;
            Vector2Int coord = WorldToCoord(t.position);
            if (!_targetLastCoords.TryGetValue(t, out Vector2Int last) || last != coord)
            {
                _targetLastCoords[t] = coord;
                anyMoved = true;
            }
        }

        // Only rebuild the full required set when something actually changed.
        // RefreshAll considers ALL targets at once — never recycles a chunk
        // still needed by another car.
        if (anyMoved)
        {
            RefreshAll();
            Physics.SyncTransforms();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Chunk management
    // ─────────────────────────────────────────────────────────

    /// <summary>Compute the union of all targets' view windows and refresh.</summary>
    /// <summary>
    /// Rebuilds the active chunk set so every coordinate needed by ANY target
    /// is loaded, and coordinates needed by NO target are recycled.
    /// Called once whenever any car crosses a chunk boundary.
    /// </summary>
    private void RefreshAll()
    {
        // 1. Build the union of all coordinates required by every target
        var required = new HashSet<Vector2Int>();
        foreach (var t in targets)
        {
            if (t == null) continue;
            Vector2Int centre = WorldToCoord(t.position);
            for (int dx = -viewRadius; dx <= viewRadius; dx++)
                for (int dz = -viewRadius; dz <= viewRadius; dz++)
                    required.Add(new Vector2Int(centre.x + dx, centre.y + dz));
        }

        // 2. Recycle any chunk that is no longer required by any target
        var toRecycle = new List<Vector2Int>();
        foreach (var kv in _active)
            if (!required.Contains(kv.Key))
                toRecycle.Add(kv.Key);

        foreach (var coord in toRecycle)
        {
            Chunk c = _active[coord];
            _active.Remove(coord);
            c.go.SetActive(false);
            _pool.Enqueue(c);
        }

        // 3. Place any required chunk that is not yet active
        foreach (var coord in required)
        {
            if (_active.ContainsKey(coord)) continue;
            Chunk chunk = _pool.Count > 0 ? _pool.Dequeue() : CreateChunk();
            PlaceChunk(chunk, coord);
            _active[coord] = chunk;
        }
    }

    // Legacy name kept so existing callers compile without change
    private void RefreshAround(Vector3 _) => RefreshAll();

    private void PlaceChunk(Chunk c, Vector2Int coord)
    {
        c.coord = coord;
        Vector3 origin = CoordToWorld(coord);
        c.go.transform.position = origin;

        // Build mesh BEFORE enabling so the collider is ready on frame 1
        Mesh m = BuildMesh(origin);
        c.mf.sharedMesh = m;        // renderer

        // MeshCollider.sharedMesh MUST be set explicitly — it does NOT
        // auto-read from the MeshFilter on the same GameObject.
        c.mc.sharedMesh = null;     // force Unity to re-cook the collider
        c.mc.sharedMesh = m;

        c.go.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────
    //  Mesh building
    // ─────────────────────────────────────────────────────────

    private Mesh BuildMesh(Vector3 worldOrigin)
    {
        int verts1D = subdivisions + 1;
        int totalVerts = verts1D * verts1D;

        var vertices = new Vector3[totalVerts];
        var uvs = new Vector2[totalVerts];
        var normals = new Vector3[totalVerts];
        var colors = new Color32[totalVerts];

        float step = chunkSize / subdivisions;

        for (int z = 0; z < verts1D; z++)
            for (int x = 0; x < verts1D; x++)
            {
                int idx = z * verts1D + x;

                float lx = x * step;          // local X (0 .. chunkSize)
                float lz = z * step;          // local Z (0 .. chunkSize)
                float wx = worldOrigin.x + lx;
                float wz = worldOrigin.z + lz;

                float y = noiseAmplitude > 0f
                    ? Mathf.PerlinNoise((wx + noiseSeed) * noiseScale,
                                        (wz + noiseSeed) * noiseScale) * noiseAmplitude
                    : 0f;

                vertices[idx] = new Vector3(lx, y, lz);
                uvs[idx] = new Vector2(lx / chunkSize, lz / chunkSize);
                normals[idx] = Vector3.up;
                colors[idx] = new Color32(
                    (byte)Mathf.RoundToInt(groundColor.r * 255f),
                    (byte)Mathf.RoundToInt(groundColor.g * 255f),
                    (byte)Mathf.RoundToInt(groundColor.b * 255f),
                    255);
            }

        // Triangles (two per quad)
        int quadCount = subdivisions * subdivisions;
        var triangles = new int[quadCount * 6];
        int t = 0;
        for (int z = 0; z < subdivisions; z++)
            for (int x = 0; x < subdivisions; x++)
            {
                int bl = z * verts1D + x;
                int br = bl + 1;
                int tl = bl + verts1D;
                int tr = tl + 1;

                triangles[t++] = bl; triangles[t++] = tl; triangles[t++] = tr;
                triangles[t++] = bl; triangles[t++] = tr; triangles[t++] = br;
            }

        var mesh = new Mesh { name = "GroundChunk" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.colors32 = colors;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();   // smooth normals for noise terrain

        return mesh;
    }

    // ─────────────────────────────────────────────────────────
    //  Chunk GameObject factory
    // ─────────────────────────────────────────────────────────

    private Chunk CreateChunk()
    {
        var go = new GameObject("Chunk");
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        // Collider — uses the same mesh, generated on demand by MeshCollider
        var mc = go.AddComponent<MeshCollider>();
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        mr.sharedMaterial = _mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;

        var chunk = new Chunk { go = go, mf = mf, mr = mr, mc = mc };
        return chunk;
    }

    // ─────────────────────────────────────────────────────────
    //  Coordinate helpers
    // ─────────────────────────────────────────────────────────

    private Vector2Int WorldToCoord(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / chunkSize),
            Mathf.FloorToInt(worldPos.z / chunkSize));
    }

    private Vector3 CoordToWorld(Vector2Int coord)
    {
        return new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);
    }

    // ─────────────────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_mat != null && groundMaterial == null)
            Destroy(_mat);
    }

}