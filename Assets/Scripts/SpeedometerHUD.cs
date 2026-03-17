using UnityEngine;

/// <summary>
/// SpeedometerHUD  v4 — split-screen correct, single Y convention throughout
///
/// Root cause of misalignment in v3:
///   FillCircle/DrawRing used +cosA (Y-up), while DrawArc/DrawNeedle/DrawRadialLine
///   used -cosA (Y-down). The GL matrix was Y-up (LoadPixelMatrix 0,W,0,H) so
///   circles rendered correctly but the arc, needle and ticks were vertically
///   mirrored relative to the background disc.
///
/// Fix in v4:
///   GL matrix is now Y-DOWN: LoadPixelMatrix(0, W, H, 0).
///   Every primitive consistently uses  +sinA for X,  -cosA for Y (clock-angle math).
///   Gauge centre (cx, cy) in GL == (cx, cy) in OnGUI  ← same coordinate space.
///   OnGUI just needs:  gui_cx = vp.x + vp.width - margin - r
///                      gui_cy = (Screen.height - vp.y - vp.height) + margin + r
///   which is the mirror of the GL position across the screen's horizontal midline.
/// </summary>
public class SpeedometerHUD : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────

    [Header("References")]
    public VehicleController vehicle;
    [Tooltip("Camera whose viewport this HUD belongs to. Auto-found if null.")]
    public Camera targetCamera;

    [Header("Layout")]
    public float gaugeDiameter = 220f;
    public Vector2 margin = new Vector2(24f, 24f);

    [Header("Speed Range")]
    public float maxDisplayKPH = 220f;

    [Header("Arc")]
    public float arcStartDeg = 215f;
    public float arcSweepDeg = 250f;
    public float needleLerpSpeed = 10f;

    [Header("Colours")]
    public Color colBackground = new Color(0.06f, 0.06f, 0.08f, 0.92f);
    public Color colRimOuter = new Color(0.55f, 0.55f, 0.60f, 1.00f);
    public Color colRimInner = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    public Color colArcTrack = new Color(0.16f, 0.16f, 0.20f, 1.00f);
    public Color colArcFill = new Color(0.92f, 0.28f, 0.10f, 1.00f);
    public Color colArcFillHigh = new Color(0.98f, 0.80f, 0.08f, 1.00f);
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
    //  Runtime
    // ─────────────────────────────────────────────────────────────

    private float _needleDeg;
    private Material _mat;

    // ─────────────────────────────────────────────────────────────
    //  Awake
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (vehicle == null)
            vehicle = GetComponentInParent<VehicleController>()
                   ?? FindFirstObjectByType<VehicleController>();

        if (targetCamera == null)
            targetCamera = GetComponent<Camera>()
                        ?? GetComponentInParent<Camera>()
                        ?? Camera.main;

        Shader sh = Shader.Find("Hidden/Internal-Colored")
                 ?? Shader.Find("Unlit/Color");
        _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull", 0);
        _mat.SetInt("_ZWrite", 0);
        _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void OnDestroy()
    {
        if (_mat != null) DestroyImmediate(_mat);
    }

    private void Update()
    {
        if (vehicle == null) return;
        float tgt = arcStartDeg + (vehicle.SpeedKPH / maxDisplayKPH) * arcSweepDeg;
        _needleDeg = Mathf.Lerp(_needleDeg, tgt, Time.deltaTime * needleLerpSpeed);
    }

    // ─────────────────────────────────────────────────────────────
    //  Coordinate helpers
    //
    //  We use a single coordinate space for both GL and OnGUI:
    //    Origin = TOP-LEFT of the screen
    //    X increases RIGHT
    //    Y increases DOWN   (same as OnGUI, same as LoadPixelMatrix(0,W,H,0))
    //
    //  Gauge centre in this space:
    //    cx = vp.x  + vp.width  - margin.x - r          (right edge, inset)
    //    cy = vpTop + vp.height  - margin.y - r          (bottom edge of viewport, inset)
    //
    //  where  vpTop  = Screen.height - vp.y - vp.height
    //         (pixelRect.y is from the bottom; vpTop converts to top-down)
    // ─────────────────────────────────────────────────────────────

    private void CalcCentre(out float cx, out float cy, out float r)
    {
        Rect vp = targetCamera.pixelRect;  // Y-up coords (Unity convention)
        float vpTop = Screen.height - vp.y - vp.height; // convert to Y-down
        r = gaugeDiameter * 0.5f;
        cx = vp.x + vp.width - margin.x - r;
        cy = vpTop + vp.height - margin.y - r;
    }

    // ─────────────────────────────────────────────────────────────
    //  OnRenderObject
    // ─────────────────────────────────────────────────────────────

    private void OnRenderObject()
    {
        if (_mat == null || vehicle == null || targetCamera == null) return;
        if (Camera.current != targetCamera) return;

        CalcCentre(out float cx, out float cy, out float r);

        GL.PushMatrix();
        _mat.SetPass(0);
        // Y-DOWN pixel matrix: origin top-left, matches OnGUI and CalcCentre
        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

        // 1. Background disc
        FillDisc(cx, cy, r * 0.98f, colBackground, 72);

        // 2. Chrome rim rings
        DrawRing(cx, cy, r, r * 0.955f, colRimOuter, 72);
        DrawRing(cx, cy, r * 0.955f, r * 0.920f, colRimInner, 72);

        // 3. Arc track + fill
        float thick = r * 0.065f;
        DrawArc(cx, cy, r * 0.80f, arcStartDeg, arcSweepDeg, thick, colArcTrack, 180);

        float frac = Mathf.Clamp01(vehicle.SpeedKPH / maxDisplayKPH);
        if (frac > 0.001f)
        {
            Color fc = frac > 0.80f
                ? Color.Lerp(colArcFill, colArcFillHigh, (frac - 0.80f) / 0.20f)
                : colArcFill;
            DrawArc(cx, cy, r * 0.80f, arcStartDeg, arcSweepDeg * frac, thick, fc, 180);
        }

        // 4. Tick marks
        int minorCount = Mathf.RoundToInt(maxDisplayKPH / 10f);
        for (int t = 0; t <= minorCount; t++)
        {
            float tf = (float)t / minorCount;
            float adeg = arcStartDeg + tf * arcSweepDeg;
            bool major = (t % 2 == 0);
            DrawRadialLine(cx, cy, adeg,
                major ? r * 0.685f : r * 0.745f,
                r * 0.875f,
                major ? 2.2f : 1.2f,
                major ? colTick : colTickMinor);
        }

        // 5. Needle + shadow
        DrawNeedle(cx + 1.5f, cy + 2f, _needleDeg, r, colNeedleShadow);
        DrawNeedle(cx, cy, _needleDeg, r, colNeedle);

        // 6. Centre hub
        DrawRing(cx, cy, r * 0.115f, r * 0.070f, colHubRing, 32);
        FillDisc(cx, cy, r * 0.070f, colHub, 32);

        // 7. Indicator dots
        float dotR = r * 0.055f;
        float dotCy = cy + r * 0.58f;
        FillDisc(cx - r * 0.22f, dotCy, dotR,
                 vehicle.IsBraking ? colBrakeOn : colIndicatorOff, 24);
        FillDisc(cx + r * 0.22f, dotCy, dotR,
                 vehicle.IsDrifting ? colDriftOn : colIndicatorOff, 24);

        GL.PopMatrix();
    }

    // ─────────────────────────────────────────────────────────────
    //  OnGUI — text, same Y-down space as GL
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (vehicle == null || targetCamera == null) return;

        // CalcCentre gives us Y-down coords — identical to OnGUI space, no conversion needed
        CalcCentre(out float cx, out float cy, out float r);

        // Digital speed
        DrawLabel(cx, cy + r * 0.20f,
                  Mathf.RoundToInt(vehicle.SpeedKPH).ToString(),
                  Mathf.RoundToInt(r * 0.28f),
                  new Color(1f, 1f, 1f, 0.96f), bold: true);

        DrawLabel(cx, cy + r * 0.40f, "km/h",
                  Mathf.RoundToInt(r * 0.115f),
                  new Color(0.55f, 0.55f, 0.60f, 1f));

        // Tick labels — same trig as DrawArc/DrawRadialLine: +sinA for X, -cosA for Y
        int labelCount = Mathf.RoundToInt(maxDisplayKPH / 20f);
        for (int l = 0; l <= labelCount; l++)
        {
            float lf = (float)l / labelCount;
            float angDeg = arcStartDeg + lf * arcSweepDeg;
            float rad = angDeg * Mathf.Deg2Rad;
            float lx = cx + Mathf.Sin(rad) * r * 0.575f;
            float ly = cy - Mathf.Cos(rad) * r * 0.575f;   // -cos = Y-down clockwise
            DrawLabel(lx, ly, (l * 20).ToString(),
                      Mathf.RoundToInt(r * 0.095f),
                      new Color(0.80f, 0.80f, 0.84f, 1f));
        }

        // Indicator labels
        float dotCy = cy + r * 0.58f;
        DrawLabel(cx - r * 0.22f, dotCy + r * 0.115f, "BRK",
                  Mathf.RoundToInt(r * 0.085f),
                  vehicle.IsBraking ? new Color(1f, 0.5f, 0.2f, 1f)
                                      : new Color(0.30f, 0.30f, 0.33f, 1f));
        DrawLabel(cx + r * 0.22f, dotCy + r * 0.115f, "DFT",
                  Mathf.RoundToInt(r * 0.085f),
                  vehicle.IsDrifting ? new Color(0.3f, 0.85f, 1f, 1f)
                                      : new Color(0.30f, 0.30f, 0.33f, 1f));
    }

    // ─────────────────────────────────────────────────────────────
    //  GL primitives — all use Y-DOWN convention (+sinA, -cosA)
    // ─────────────────────────────────────────────────────────────

    /// Filled disc (triangle fan). Y-down: angle 0 = top, increases clockwise.
    private void FillDisc(float cx, float cy, float radius, Color col, int segs)
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

    /// Annular ring. Y-down convention.
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

    /// Thick arc (annular sector). startDeg=0 = top, clockwise. Y-down.
    private void DrawArc(float cx, float cy, float radius,
                          float startDeg, float sweepDeg,
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
            float a0 = startRad + s * step, a1 = startRad + (s + 1) * step;
            float ox0 = cx + Mathf.Sin(a0) * outerR, oy0 = cy - Mathf.Cos(a0) * outerR;
            float ox1 = cx + Mathf.Sin(a1) * outerR, oy1 = cy - Mathf.Cos(a1) * outerR;
            float ix0 = cx + Mathf.Sin(a0) * innerR, iy0 = cy - Mathf.Cos(a0) * innerR;
            float ix1 = cx + Mathf.Sin(a1) * innerR, iy1 = cy - Mathf.Cos(a1) * innerR;
            GL.Vertex3(ox0, oy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix0, iy0, 0);
            GL.Vertex3(ix0, iy0, 0); GL.Vertex3(ox1, oy1, 0); GL.Vertex3(ix1, iy1, 0);
        }
        GL.End();
    }

    /// Thick radial line at a clock angle. Y-down.
    private void DrawRadialLine(float cx, float cy, float angleDeg,
                                 float innerR, float outerR, float width, Color col)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(rad), cosA = Mathf.Cos(rad);
        // Along-needle direction (Y-down): (sinA, -cosA)
        // Perpendicular:                  (cosA,  sinA)
        float px = cosA * width * 0.5f, py = sinA * width * 0.5f;
        float x1 = cx + sinA * innerR, y1 = cy - cosA * innerR;
        float x2 = cx + sinA * outerR, y2 = cy - cosA * outerR;
        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        GL.Vertex3(x1 - px, y1 - py, 0); GL.Vertex3(x1 + px, y1 + py, 0); GL.Vertex3(x2 - px, y2 - py, 0);
        GL.Vertex3(x1 + px, y1 + py, 0); GL.Vertex3(x2 + px, y2 + py, 0); GL.Vertex3(x2 - px, y2 - py, 0);
        GL.End();
    }

    /// Tapered needle. Y-down.
    private void DrawNeedle(float cx, float cy, float angleDeg, float r, Color col)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(rad), cosA = Mathf.Cos(rad);
        // Forward (tip direction in Y-down):  (sinA, -cosA)
        // Perpendicular:                      (cosA,  sinA)
        float fwdX = sinA, fwdY = -cosA;
        float perX = cosA, perY = sinA;
        float hw = r * 0.028f;
        float tx = cx + fwdX * r * 0.78f, ty = cy + fwdY * r * 0.78f;   // tip
        float bx = cx - fwdX * r * 0.18f, by = cy - fwdY * r * 0.18f;   // tail
        float lx = cx + perX * hw, ly = cy + perY * hw;           // base L
        float rx2 = cx - perX * hw, ry = cy - perY * hw;           // base R
        GL.Begin(GL.TRIANGLES);
        GL.Color(col);
        GL.Vertex3(lx, ly, 0); GL.Vertex3(rx2, ry, 0); GL.Vertex3(tx, ty, 0);
        GL.Vertex3(lx, ly, 0); GL.Vertex3(rx2, ry, 0); GL.Vertex3(bx, by, 0);
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
}