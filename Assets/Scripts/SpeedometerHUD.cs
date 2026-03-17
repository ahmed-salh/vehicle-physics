using UnityEngine;

/// <summary>
/// SpeedometerHUD  v2  — HD GL-rendered analogue speedometer
///
/// All geometry (arcs, rings, needle, tick marks) is drawn with Unity's GL
/// immediate-mode renderer, which produces true hardware-accelerated vector
/// graphics at native screen resolution — no pixelation regardless of DPI or
/// scale.  Text labels use OnGUI which is always sharp.
///
/// SETUP:
///   1. Drop this component on any GameObject (e.g. the car root).
///   2. Assign the VehicleController reference, or leave null to auto-find.
///   3. Requires a camera tagged "MainCamera" (standard Unity default).
///
/// No Canvas, no prefabs, no external assets required.
/// </summary>
[RequireComponent(typeof(Camera))]   // needs a camera on the same GO, OR uses Camera.main
public class SpeedometerHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────

    [Header("References")]
    public VehicleController vehicle;

    [Header("Layout")]
    [Tooltip("Diameter of the gauge in screen pixels.")]
    public float gaugeDiameter = 220f;

    [Tooltip("Gap from the screen bottom-right edge.")]
    public Vector2 margin = new Vector2(24f, 24f);

    [Header("Speed Range")]
    public float maxDisplayKPH = 220f;

    [Header("Arc")]
    [Tooltip("Angle (°) where the arc starts — measured clockwise from 12 o'clock.")]
    public float arcStartDeg = 215f;
    [Tooltip("Total arc sweep in degrees.")]
    public float arcSweepDeg = 250f;
    [Tooltip("Smoothing speed for the needle.")]
    public float needleLerpSpeed = 10f;

    [Header("Colours")]
    public Color colBackground = new Color(0.06f, 0.06f, 0.08f, 0.92f);
    public Color colRimOuter = new Color(0.55f, 0.55f, 0.60f, 1.00f);
    public Color colRimInner = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    public Color colArcTrack = new Color(0.16f, 0.16f, 0.20f, 1.00f);
    public Color colArcFill = new Color(0.92f, 0.28f, 0.10f, 1.00f);
    public Color colArcFillHigh = new Color(0.98f, 0.80f, 0.08f, 1.00f);  // above 80% speed
    public Color colTick = new Color(0.78f, 0.78f, 0.82f, 1.00f);
    public Color colTickMinor = new Color(0.38f, 0.38f, 0.42f, 1.00f);
    public Color colNeedle = new Color(0.95f, 0.22f, 0.08f, 1.00f);
    public Color colNeedleShadow = new Color(0.00f, 0.00f, 0.00f, 0.45f);
    public Color colHub = new Color(0.14f, 0.14f, 0.16f, 1.00f);
    public Color colHubRing = new Color(0.55f, 0.55f, 0.60f, 1.00f);
    public Color colBrakeOn = new Color(1.00f, 0.32f, 0.08f, 1.00f);
    public Color colDriftOn = new Color(0.15f, 0.72f, 1.00f, 1.00f);
    public Color colIndicatorOff = new Color(0.15f, 0.15f, 0.18f, 1.00f);

    // ─────────────────────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────────────────────

    private float _needleDeg;       // smoothed needle angle (degrees, clockwise from 12)
    private Material _mat;             // Hidden/Internal-Colored — always present in Unity
    private Camera _cam;

    // Cached centre & radius (recalculated if screen size changes)
    private float _cx, _cy, _r;
    private int _cachedW, _cachedH;
    private float _cachedDiam;

    // ─────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (vehicle == null)
            vehicle = GetComponentInParent<VehicleController>()
                   ?? FindFirstObjectByType<VehicleController>();

        // "Hidden/Internal-Colored" is a Unity built-in that supports vertex colours
        // and is always available. We use it for all GL drawing.
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull", 0);
        _mat.SetInt("_ZWrite", 0);
        _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        _cam = Camera.main;
        RecalcLayout();
    }

    private void OnDestroy()
    {
        if (_mat != null) DestroyImmediate(_mat);
    }

    private void Update()
    {
        if (vehicle == null) return;
        float targetDeg = arcStartDeg + (vehicle.SpeedKPH / maxDisplayKPH) * arcSweepDeg;
        _needleDeg = Mathf.Lerp(_needleDeg, targetDeg, Time.deltaTime * needleLerpSpeed);
    }

    // ─────────────────────────────────────────────────────────────
    //  GL rendering  — called once per frame AFTER the scene renders
    // ─────────────────────────────────────────────────────────────

    private void OnRenderObject()
    {
        if (_mat == null || vehicle == null) return;
        if (Camera.current != (_cam != null ? _cam : Camera.main)) return;

        // Recalculate layout if screen or gauge size changed
        if (Screen.width != _cachedW || Screen.height != _cachedH
            || !Mathf.Approximately(gaugeDiameter, _cachedDiam))
            RecalcLayout();

        // Set up a pixel-space projection matrix so we can work in screen pixels
        GL.PushMatrix();
        _mat.SetPass(0);
        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

        float r = _r;
        float cx = _cx, cy = _cy;

        // ── 1. Dark background disc ──────────────────────────────
        FillCircle(cx, cy, r * 0.98f, colBackground, 72);

        // ── 2. Outer chrome rim (ring) ───────────────────────────
        DrawRing(cx, cy, r, r * 0.955f, colRimOuter, 72);
        DrawRing(cx, cy, r * 0.955f, r * 0.92f, colRimInner, 72);

        // ── 3. Arc track (grey full arc) ─────────────────────────
        float arcThick = r * 0.065f;
        DrawArc(cx, cy, r * 0.80f, arcStartDeg, arcSweepDeg, arcThick, colArcTrack, 180);

        // ── 4. Arc fill (coloured speed fill) ────────────────────
        float speedFrac = Mathf.Clamp01(vehicle.SpeedKPH / maxDisplayKPH);
        if (speedFrac > 0.001f)
        {
            Color fillCol = speedFrac > 0.80f
                ? Color.Lerp(colArcFill, colArcFillHigh, (speedFrac - 0.80f) / 0.20f)
                : colArcFill;
            DrawArc(cx, cy, r * 0.80f, arcStartDeg, arcSweepDeg * speedFrac, arcThick, fillCol, 180);
        }

        // ── 5. Tick marks ─────────────────────────────────────────
        int majorCount = Mathf.RoundToInt(maxDisplayKPH / 20f);
        int minorCount = Mathf.RoundToInt(maxDisplayKPH / 10f);

        for (int t = 0; t <= minorCount; t++)
        {
            float frac = (float)t / minorCount;
            float angleDeg = arcStartDeg + frac * arcSweepDeg;
            bool major = (t % 2 == 0);
            float inner = major ? r * 0.685f : r * 0.745f;
            float outer = r * 0.875f;
            float thick = major ? 2.2f : 1.2f;
            DrawRadialLine(cx, cy, angleDeg, inner, outer, thick,
                           major ? colTick : colTickMinor);
        }

        // ── 6. Needle shadow (slightly offset) ───────────────────
        DrawNeedle(cx + 1.5f, cy + 2f, _needleDeg, r, colNeedleShadow);

        // ── 7. Needle ─────────────────────────────────────────────
        DrawNeedle(cx, cy, _needleDeg, r, colNeedle);

        // ── 8. Centre hub ─────────────────────────────────────────
        DrawRing(cx, cy, r * 0.115f, r * 0.070f, colHubRing, 32);
        FillCircle(cx, cy, r * 0.070f, colHub, 32);

        // ── 9. Indicator dots (brake / drift) ────────────────────
        float dotR = r * 0.055f;
        float dotCy = cy + r * 0.58f;

        FillCircle(cx - r * 0.22f, dotCy, dotR,
                   vehicle.IsBraking ? colBrakeOn : colIndicatorOff, 24);
        FillCircle(cx + r * 0.22f, dotCy, dotR,
                   vehicle.IsDrifting ? colDriftOn : colIndicatorOff, 24);

        GL.PopMatrix();
    }

    // ─────────────────────────────────────────────────────────────
    //  OnGUI — text only (always crisp)
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (vehicle == null) return;

        float cx = _cx, cy = _cy, r = _r;

        // Digital KPH value — large bold number
        DrawLabel(cx, cy + r * 0.20f,
                  Mathf.RoundToInt(vehicle.SpeedKPH).ToString(),
                  Mathf.RoundToInt(r * 0.28f),
                  new Color(1f, 1f, 1f, 0.96f), bold: true);

        // "km/h" unit
        DrawLabel(cx, cy + r * 0.40f, "km/h",
                  Mathf.RoundToInt(r * 0.115f),
                  new Color(0.55f, 0.55f, 0.60f, 1f));

        // Tick labels every 20 km/h
        int labelStep = 20;
        int labelCount = Mathf.RoundToInt(maxDisplayKPH / labelStep);
        for (int l = 0; l <= labelCount; l++)
        {
            float frac = (float)l / labelCount;
            float angDeg = arcStartDeg + frac * arcSweepDeg;
            float rad = angDeg * Mathf.Deg2Rad;
            float lx = cx + Mathf.Sin(rad) * r * 0.575f;
            float ly = cy - Mathf.Cos(rad) * r * 0.575f;
            DrawLabel(lx, ly, (l * labelStep).ToString(),
                      Mathf.RoundToInt(r * 0.095f),
                      new Color(0.80f, 0.80f, 0.84f, 1f));
        }

        // Indicator text
        float dotCy = cy + r * 0.58f;
        DrawLabel(cx - r * 0.22f, dotCy + r * 0.115f, "BRK",
                  Mathf.RoundToInt(r * 0.085f),
                  vehicle.IsBraking
                      ? new Color(1f, 0.5f, 0.2f, 1f)
                      : new Color(0.30f, 0.30f, 0.33f, 1f));
        DrawLabel(cx + r * 0.22f, dotCy + r * 0.115f, "DFT",
                  Mathf.RoundToInt(r * 0.085f),
                  vehicle.IsDrifting
                      ? new Color(0.3f, 0.85f, 1f, 1f)
                      : new Color(0.30f, 0.30f, 0.33f, 1f));
    }

    // ─────────────────────────────────────────────────────────────
    //  GL primitives
    // ─────────────────────────────────────────────────────────────

    /// <summary>Filled disc using a triangle fan.</summary>
    private void FillCircle(float cx, float cy, float radius, Color col, int segs)
    {
        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        float step = Mathf.PI * 2f / segs;
        for (int s = 0; s < segs; s++)
        {
            float a0 = s * step, a1 = (s + 1) * step;
            GL.Vertex3(cx, cy, 0);
            GL.Vertex3(cx + Mathf.Sin(a0) * radius, cy - Mathf.Cos(a0) * radius, 0);
            GL.Vertex3(cx + Mathf.Sin(a1) * radius, cy - Mathf.Cos(a1) * radius, 0);
        }
        GL.End();
    }

    /// <summary>Filled annular ring (outer radius → inner radius).</summary>
    private void DrawRing(float cx, float cy, float outerR, float innerR, Color col, int segs)
    {
        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        float step = Mathf.PI * 2f / segs;
        for (int s = 0; s < segs; s++)
        {
            float a0 = s * step, a1 = (s + 1) * step;
            float ox0 = cx + Mathf.Sin(a0) * outerR, oy0 = cy - Mathf.Cos(a0) * outerR;
            float ox1 = cx + Mathf.Sin(a1) * outerR, oy1 = cy - Mathf.Cos(a1) * outerR;
            float ix0 = cx + Mathf.Sin(a0) * innerR, iy0 = cy - Mathf.Cos(a0) * innerR;
            float ix1 = cx + Mathf.Sin(a1) * innerR, iy1 = cy - Mathf.Cos(a1) * innerR;
            GL.Vertex3(ox0, oy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix0, iy0, 0);
            GL.Vertex3(ix0, iy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix1, iy1, 0);
        }
        GL.End();
    }

    /// <summary>Thick arc drawn as a quad strip (annular sector).</summary>
    private void DrawArc(float cx, float cy, float radius, float startDeg, float sweepDeg,
                          float thickness, Color col, int segs)
    {
        float outerR = radius + thickness * 0.5f;
        float innerR = radius - thickness * 0.5f;
        float startRad = startDeg * Mathf.Deg2Rad;
        float sweepRad = sweepDeg * Mathf.Deg2Rad;
        float step = sweepRad / segs;

        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        for (int s = 0; s < segs; s++)
        {
            float a0 = startRad + s * step;
            float a1 = startRad + (s + 1) * step;
            float ox0 = cx + Mathf.Sin(a0) * outerR, oy0 = cy - Mathf.Cos(a0) * outerR;
            float ox1 = cx + Mathf.Sin(a1) * outerR, oy1 = cy - Mathf.Cos(a1) * outerR;
            float ix0 = cx + Mathf.Sin(a0) * innerR, iy0 = cy - Mathf.Cos(a0) * innerR;
            float ix1 = cx + Mathf.Sin(a1) * innerR, iy1 = cy - Mathf.Cos(a1) * innerR;
            GL.Vertex3(ox0, oy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix0, iy0, 0);
            GL.Vertex3(ix0, iy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix1, iy1, 0);
        }
        GL.End();
    }

    /// <summary>Single anti-width line along a radial direction.</summary>
    private void DrawRadialLine(float cx, float cy, float angleDeg,
                                 float innerR, float outerR, float width, Color col)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(rad), cosA = Mathf.Cos(rad);
        // Perpendicular direction for line width
        float px = -cosA * width * 0.5f, py = -sinA * width * 0.5f;

        float x1 = cx + sinA * innerR, y1 = cy - cosA * innerR;
        float x2 = cx + sinA * outerR, y2 = cy - cosA * outerR;

        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        GL.Vertex3(x1 + px, y1 + py, 0); GL.Vertex3(x1 - px, y1 - py, 0);
        GL.Vertex3(x2 + px, y2 + py, 0);
        GL.Vertex3(x1 - px, y1 - py, 0); GL.Vertex3(x2 - px, y2 - py, 0);
        GL.Vertex3(x2 + px, y2 + py, 0);
        GL.End();
    }

    /// <summary>Tapered needle: wide at base, pointed at tip, drawn as a filled quad.</summary>
    private void DrawNeedle(float cx, float cy, float angleDeg, float r, Color col)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(rad), cosA = Mathf.Cos(rad);
        // Forward (along needle) and perpendicular
        float fx = sinA, fy = -cosA;     // tip direction
        float px = cosA, py = sinA;      // perpendicular

        float baseHalfW = r * 0.028f;    // width at hub
        float tipOffset = r * 0.78f;     // how far the tip extends
        float tailBack = r * 0.18f;     // counterweight behind hub

        // Tip point
        float tx = cx + fx * tipOffset, ty = cy + fy * tipOffset;
        // Tail point
        float bx = cx - fx * tailBack, by = cy - fy * tailBack;
        // Base left / right
        float lx = cx + px * baseHalfW, ly = cy + py * baseHalfW;
        float rx = cx - px * baseHalfW, ry = cy - py * baseHalfW;

        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        // Main needle body (lx,ly → rx,ry → tip)
        GL.Vertex3(lx, ly, 0); GL.Vertex3(rx, ry, 0); GL.Vertex3(tx, ty, 0);
        // Counterweight (lx,ly → rx,ry → tail)
        GL.Vertex3(lx, ly, 0); GL.Vertex3(rx, ry, 0); GL.Vertex3(bx, by, 0);
        GL.End();
    }

    // ─────────────────────────────────────────────────────────────
    //  GUI text helper
    // ─────────────────────────────────────────────────────────────

    private void DrawLabel(float cx, float cy, string text, int size,
                            Color col, bool bold = false)
    {
        size = Mathf.Max(size, 8);
        var style = new GUIStyle
        {
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
        };
        style.normal.textColor = col;
        float w = size * text.Length * 0.85f + 8f;
        float h = size + 4f;
        GUI.Label(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h), text, style);
    }

    // ─────────────────────────────────────────────────────────────
    //  Layout helper
    // ─────────────────────────────────────────────────────────────

    private void RecalcLayout()
    {
        _r = gaugeDiameter * 0.5f;
        _cx = Screen.width - margin.x - _r;
        _cy = Screen.height - margin.y - _r;
        _cachedW = Screen.width;
        _cachedH = Screen.height;
        _cachedDiam = gaugeDiameter;
    }
}