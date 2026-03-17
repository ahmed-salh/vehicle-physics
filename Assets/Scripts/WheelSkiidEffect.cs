using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WheelSkidEffect  v3
///
/// Fixes vs v2:
///   - Each segment gets its OWN Material instance. Fade works by setting
///     mat.color.a directly — no vertex-color gradient trick, no shader-keyword
///     guesswork.  "Sprites/Default" is used as the base because it supports
///     alpha blending in all built-in RP configurations.
///   - Hard pool cap (maxSegments).  When the cap is reached the oldest
///     finished segment is force-recycled instead of spawning a new GO.
///     This puts a firm ceiling on scene object count.
///   - Active segments are also subject to the cap: if everything is active
///     the oldest active segment is closed and reused.
///   - Positions come straight from hit.point (world-space ground). A tiny
///     Y-lift prevents z-fighting. Marks stay pinned to the scene root so
///     they don't move with the car.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class WheelSkidEffect : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────

    [Header("Appearance")]
    [Tooltip("Width of each tyre mark in metres.")]
    public float markWidth = 0.18f;

    [Tooltip("Tiny Y-lift above surface to prevent z-fighting (metres).")]
    public float groundOffset = 0.012f;

    [Tooltip("Seconds for a finished segment to fully fade out and be removed.")]
    public float fadeDuration = 6f;

    [Header("Sampling")]
    [Tooltip("Minimum distance (m) between consecutive mark points. Lower = smoother but more verts.")]
    public float minPointDistance = 0.09f;

    [Tooltip("Points per segment before rolling over to a fresh one.")]
    public int maxPointsPerSegment = 200;

    [Header("Pool")]
    [Tooltip("Maximum LineRenderer objects kept alive for this wheel. " +
             "Oldest finished segment is recycled when the cap is hit.")]
    public int maxSegments = 12;

    // ─────────────────────────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────────────────────────

    private class SkidSegment
    {
        public GameObject go;
        public LineRenderer lr;
        public Material mat;          // own instance — safe to change color
        public List<Vector3> points = new List<Vector3>();
        public bool active;      // currently being written to
        public float endTime;     // Time.time when closed
        public int poolIndex;   // position in _segments list (for sorting)
    }

    private readonly List<SkidSegment> _segments = new List<SkidSegment>();
    private SkidSegment _current = null;

    // Shared base shader — "Sprites/Default" always supports alpha blending
    private Shader _shader;

    // Near-black mark colour (RGB only; alpha is set per-segment)
    private static readonly Color kMarkRGB = new Color(0.06f, 0.06f, 0.06f, 1f);

    private Vector3 _lastPos = Vector3.positiveInfinity;

    // ─────────────────────────────────────────────────────────────
    //  Awake
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Disable the stub LR that [RequireComponent] added
        GetComponent<LineRenderer>().enabled = false;

        // "Sprites/Default" supports alpha-blend in all built-in RP setups.
        // If on URP the fallback "Universal Render Pipeline/Particles/Unlit" is better,
        // but Sprites/Default will still render (just unlit).
        _shader = Shader.Find("Sprites/Default");
        if (_shader == null) _shader = Shader.Find("Unlit/Transparent");
        if (_shader == null) _shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
    }

    // ─────────────────────────────────────────────────────────────
    //  Update — fade & recycle finished segments
    // ─────────────────────────────────────────────────────────────

    private void Update()
    {
        float now = Time.time;

        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            SkidSegment seg = _segments[i];
            if (seg.active) continue;                       // still being written — leave alone

            float age = now - seg.endTime;
            float t = Mathf.Clamp01(age / Mathf.Max(fadeDuration, 0.01f));

            if (t >= 1f)
            {
                // Fully faded — destroy and remove from list
                DestroySegment(seg);
                _segments.RemoveAt(i);
            }
            else
            {
                // Update material alpha
                SetAlpha(seg, 1f - t);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API  (VehicleController calls every FixedUpdate)
    // ─────────────────────────────────────────────────────────────

    public void Tick(bool isGrounded, bool isSkidding, bool isDrifting,
                     Vector3 contactPoint, float wheelRadius)
    {
        bool shouldMark = isGrounded && (isSkidding || isDrifting);

        if (!shouldMark)
        {
            CloseCurrentSegment();
            return;
        }

        // Stamp directly on the ground + tiny lift
        Vector3 pos = new Vector3(contactPoint.x,
                                  contactPoint.y + groundOffset,
                                  contactPoint.z);

        // Distance gate
        if (Vector3.Distance(pos, _lastPos) < minPointDistance) return;
        _lastPos = pos;

        // Roll over if current segment is full
        if (_current != null && _current.points.Count >= maxPointsPerSegment)
            CloseCurrentSegment();

        if (_current == null)
            _current = AllocateSegment();

        _current.points.Add(pos);
        _current.lr.positionCount = _current.points.Count;
        _current.lr.SetPositions(_current.points.ToArray());
    }

    // ─────────────────────────────────────────────────────────────
    //  Allocation
    // ─────────────────────────────────────────────────────────────

    private SkidSegment AllocateSegment()
    {
        // 1. Try to reuse the oldest fully-faded segment (already removed in Update,
        //    but if Update hasn't run yet this frame, check here too)
        for (int i = 0; i < _segments.Count; i++)
        {
            SkidSegment s = _segments[i];
            if (!s.active && (Time.time - s.endTime) >= fadeDuration)
            {
                ReuseSegment(s);
                return s;
            }
        }

        // 2. Under cap — spawn a brand new segment
        if (_segments.Count < maxSegments)
        {
            SkidSegment s = CreateNewSegment();
            _segments.Add(s);
            return s;
        }

        // 3. Cap hit — force-recycle the oldest FINISHED (non-active) segment
        for (int i = 0; i < _segments.Count; i++)
        {
            if (!_segments[i].active)
            {
                ReuseSegment(_segments[i]);
                return _segments[i];
            }
        }

        // 4. All segments are still active — close and recycle the oldest one
        SkidSegment oldest = _segments[0];
        oldest.active = false;
        oldest.endTime = Time.time;
        if (oldest == _current) _current = null;
        ReuseSegment(oldest);
        return oldest;
    }

    private SkidSegment CreateNewSegment()
    {
        var go = new GameObject("SkidMark");
        go.transform.SetParent(null, true);          // scene root — never moves with the car

        var mat = new Material(_shader) { color = kMarkRGB };
        mat.renderQueue = 3000;

        var lr = go.AddComponent<LineRenderer>();
        ConfigureLR(lr, mat);

        return new SkidSegment
        {
            go = go,
            lr = lr,
            mat = mat,
            active = true,
        };
    }

    private void ReuseSegment(SkidSegment seg)
    {
        seg.points.Clear();
        seg.active = true;
        seg.endTime = 0f;
        seg.lr.positionCount = 0;
        seg.lr.enabled = true;
        SetAlpha(seg, 1f);
    }

    // ─────────────────────────────────────────────────────────────
    //  Close / destroy
    // ─────────────────────────────────────────────────────────────

    private void CloseCurrentSegment()
    {
        if (_current == null) return;
        _current.active = false;
        _current.endTime = Time.time;
        _current = null;
        _lastPos = Vector3.positiveInfinity;
    }

    private void DestroySegment(SkidSegment seg)
    {
        if (seg.mat != null) Destroy(seg.mat);
        if (seg.go != null) Destroy(seg.go);
    }

    private void OnDestroy()
    {
        CloseCurrentSegment();
        foreach (var seg in _segments) DestroySegment(seg);
        _segments.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  LineRenderer setup
    // ─────────────────────────────────────────────────────────────

    private void ConfigureLR(LineRenderer lr, Material mat)
    {
        lr.material = mat;           // instance, not shared
        lr.useWorldSpace = true;
        lr.widthMultiplier = markWidth;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;
        lr.positionCount = 0;
        // Solid gradient — fade handled through mat.color, not vertex colours
        lr.colorGradient = SolidGradient(Color.white);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private void SetAlpha(SkidSegment seg, float alpha)
    {
        seg.mat.color = new Color(kMarkRGB.r, kMarkRGB.g, kMarkRGB.b, alpha);
    }

    private static Gradient SolidGradient(Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        foreach (var seg in _segments)
        {
            if (seg.go == null) continue;
            Gizmos.color = seg.active ? Color.red : Color.grey;
            foreach (var pt in seg.points)
                Gizmos.DrawSphere(pt, 0.03f);
        }
    }
#endif
}