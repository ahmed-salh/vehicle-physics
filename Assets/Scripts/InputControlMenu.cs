using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InputControllerMenu  v3
///
/// Full keyboard + gamepad navigation — no mouse required.
///
/// Navigation model:
///   The menu is a grid:  rows = players (0..N-1),  cols = modes (0=Keyboard, 1=Gamepad)
///   A cursor (_curRow, _curCol) tracks which cell is highlighted.
///
/// Keyboard controls:
///   Up / Down (or W/S)          → move cursor between player rows
///   Left / Right (or A/D)       → move cursor between mode columns
///   Enter / Space               → confirm (apply selection)
///   Tab / Escape                → close menu
///
/// Gamepad controls (any connected pad):
///   D-pad / Left stick          → move cursor
///   A / Cross  (button 0)       → confirm
///   Start      (button 7)       → close menu
///   B / Circle (button 1)       → close menu
///
/// Visual feedback:
///   Cursor cell gets a bright white border drawn around it.
///   Active (currently selected) cells stay green.
///   Cursor + Active cell gets a combined highlight.
/// </summary>
public class InputControllerMenu : MonoBehaviour
{
    [Header("Providers (auto-found if empty)")]
    public VehicleInputProvider[] providers;

    [Header("Toggle Key")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("Style")]
    public Color panelColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
    public Color activeColor = new Color(0.20f, 0.75f, 0.30f, 1.00f);
    public Color inactiveColor = new Color(0.25f, 0.25f, 0.30f, 1.00f);
    public Color cursorColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);
    public Color cursorActiveColor = new Color(0.40f, 1.00f, 0.55f, 1.00f);
    public Color headerColor = new Color(0.90f, 0.75f, 0.20f, 1.00f);

    // ── Runtime ─────────────────────────────────────────────────
    private bool _visible;
    private Canvas _canvas;
    private GameObject _panel;
    private Button[][] _modeButtons;
    private Image[][] _buttonImages;     // background image per cell
    private Image[][] _borderImages;     // cursor border overlay per cell

    // Cursor position in the grid
    private int _curRow = 0;
    private int _curCol = 0;

    // No auto-repeat — every navigation event is one-shot (press-down only).
    // _prevStick stores the last-frame axis values for edge detection on the stick.
    private float[] _prevStickX = new float[5];   // index = joystick slot 1-4
    private float[] _prevStickY = new float[5];
    private float[] _prevDpadX = new float[5];
    private float[] _prevDpadY = new float[5];
    private const float StickThreshold = 0.5f;

    private const int ModeCount = 2;
    private static readonly string[] ModeLabels = { "⌨  Keyboard", "🎮  Gamepad" };

    // ── Lifecycle ────────────────────────────────────────────────

    private void Start()
    {
        if (providers == null || providers.Length == 0)
            providers = FindObjectsByType<VehicleInputProvider>(FindObjectsSortMode.None);

        BuildCanvas();
        _panel.SetActive(false);
    }

    private void Update()
    {
        // ── Toggle open/close ────────────────────────────────────
        // Toggle is ONE-SHOT only — GetKeyDown / button-down, never GetKey.
        // This eliminates the flicker caused by re-triggering every frame while held.
        // Keyboard-only toggle. Gamepad toggle is routed exclusively via
        // SplitScreenManager → ToggleFromProvider() to avoid R2/button conflicts.
        bool togglePressed =
            Input.GetKeyDown(toggleKey) ||   // Tab
            Input.GetKeyDown(KeyCode.Escape);   // Escape

        if (togglePressed) { DoToggle(); return; }

        if (!_visible) return;

        // ── Directional navigation — ONE-SHOT only ──────────────
        // ReadNavDown fires only on the frame a key/stick FIRST crosses
        // the threshold. Holding has no effect — one press = one move.
        Vector2Int dir = ReadNavDown();
        if (dir != Vector2Int.zero) MoveCursor(dir);

        // ── Confirm ──────────────────────────────────────────────
        // Keyboard confirm. Gamepad confirm (X/Square) is handled via
        // AnyJoyBtnDown(2) only — no other gamepad buttons touch the menu.
        bool confirm =
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.Space) ||
            AnyJoyBtnDown(2);                    // X / Square / num3 only

        if (confirm) ApplySelection();
    }

    // ── Toggle helpers ──────────────────────────────────────

    /// Called by keyboard / gamepad Start button inside this script.
    private void DoToggle()
    {
        _visible = !_visible;
        _panel.SetActive(_visible);
        if (_visible)
        {
            _curRow = 0;
            _curCol = providers != null && providers.Length > 0
                ? (int)providers[0].mode : 0;
            RefreshVisuals();
        }
    }

    /// Called externally by SplitScreenManager when a provider's L1 fires.
    /// Identical to DoToggle but public so the manager can call it.
    public void ToggleFromProvider()
    {
        DoToggle();
    }

    // ── Cursor movement ──────────────────────────────────────────

    private void MoveCursor(Vector2Int dir)
    {
        int rows = providers.Length;

        _curRow = (_curRow - dir.y + rows) % rows;       // up = +y on screen = -row
        _curCol = (_curCol + dir.x + ModeCount) % ModeCount;

        RefreshVisuals();
    }

    // ── Apply current cursor selection ───────────────────────────

    private void ApplySelection()
    {
        if (providers == null || _curRow >= providers.Length) return;

        var mode = (VehicleInputProvider.InputMode)_curCol;

        if (mode == VehicleInputProvider.InputMode.Gamepad)
            providers[_curRow].gamepadSlotRank = providers[_curRow].playerIndex;

        providers[_curRow].SetMode(mode);
        RefreshVisuals();
    }

    // ── Visual refresh ───────────────────────────────────────────

    private void RefreshVisuals()
    {
        if (_modeButtons == null) return;

        for (int p = 0; p < providers.Length; p++)
        {
            int activeMode = (int)providers[p].mode;

            for (int m = 0; m < ModeCount; m++)
            {
                if (_buttonImages[p][m] == null) continue;

                bool isActive = (m == activeMode);
                bool isCursor = (p == _curRow && m == _curCol);

                // Background colour
                Color bg;
                if (isCursor && isActive) bg = cursorActiveColor;
                else if (isCursor) bg = Color.Lerp(inactiveColor, cursorColor, 0.35f);
                else if (isActive) bg = activeColor;
                else bg = inactiveColor;

                _buttonImages[p][m].color = bg;

                // Border visibility
                if (_borderImages[p][m] != null)
                    _borderImages[p][m].enabled = isCursor;
            }
        }
    }

    // ── Input helpers ────────────────────────────────────────────

    /// Returns a direction only on the frame a key or stick FIRST crosses the
    /// threshold — never while held. Uses per-slot edge detection for the stick.
    private Vector2Int ReadNavDown()
    {
        int dx = 0, dy = 0;

        // ── Keyboard: GetKeyDown = one event per press ──────────
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dx -= 1;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dx += 1;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dy += 1;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dy -= 1;

        // ── Gamepad: edge detection — only fire on the frame the axis
        //    crosses StickThreshold, not while it stays past it. ──
        for (int slot = 1; slot <= 4; slot++)
        {
            float ax = ReadRawAxis(slot, 0);   // left stick X
            float ay = ReadRawAxis(slot, 1);   // left stick Y
            float dpx = ReadRawAxis(slot, 6);   // D-pad X
            float dpy = ReadRawAxis(slot, 7);   // D-pad Y

            // Pick the larger magnitude source (stick vs D-pad)
            float sx = Mathf.Abs(ax) > Mathf.Abs(dpx) ? ax : dpx;
            float sy = Mathf.Abs(ay) > Mathf.Abs(dpy) ? -ay : -dpy; // invert Y

            float px = _prevStickX[slot], py = _prevStickY[slot];

            // Left crossing  : was >= -threshold, now < -threshold
            if (sx < -StickThreshold && px >= -StickThreshold) dx -= 1;
            // Right crossing : was <=  threshold, now >  threshold
            if (sx > StickThreshold && px <= StickThreshold) dx += 1;
            // Up crossing    : was <=  threshold, now >  threshold
            if (sy > StickThreshold && py <= StickThreshold) dy += 1;
            // Down crossing  : was >= -threshold, now < -threshold
            if (sy < -StickThreshold && py >= -StickThreshold) dy -= 1;

            _prevStickX[slot] = sx;
            _prevStickY[slot] = sy;
        }

        dx = Mathf.Clamp(dx, -1, 1);
        dy = Mathf.Clamp(dy, -1, 1);
        if (dx != 0) dy = 0;   // horizontal takes priority

        return new Vector2Int(dx, dy);
    }

    private static float ReadRawAxis(int slot, int axis)
    {
        try { return Input.GetAxisRaw($"j{slot}_axis{axis}"); }
        catch { return 0f; }
    }

    // Returns true while the button is held (for any connected joystick slot)
    private static bool AnyJoyBtn(int btn)
    {
        for (int slot = 1; slot <= 4; slot++)
            if (Input.GetKey((KeyCode)(350 + (slot - 1) * 20 + btn))) return true;
        return false;
    }

    // Returns true on the frame the button was first pressed
    private static bool AnyJoyBtnDown(int btn)
    {
        for (int slot = 1; slot <= 4; slot++)
            if (Input.GetKeyDown((KeyCode)(350 + (slot - 1) * 20 + btn))) return true;
        return false;
    }

    // ── Build UI ─────────────────────────────────────────────────

    private void BuildCanvas()
    {
        var cgo = new GameObject("InputMenu_Canvas");
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        cgo.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cgo.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        float panelH = 100f + providers.Length * 110f;
        _panel = MakePanel("Panel",
            _canvas.transform as RectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(380f, panelH), panelColor);

        var panelRt = _panel.GetComponent<RectTransform>();

        MakeText("Title", panelRt,
                 new Vector2(0f, 0.92f), new Vector2(1f, 1f),
                 "INPUT SETTINGS", 18, headerColor, bold: true);

        MakeText("Hint", panelRt,
                 new Vector2(0f, 0f), new Vector2(1f, 0.07f),
                 $"[{toggleKey}] / B / Start = close   ·   ← → select   ·   Enter / A = confirm",
                 10, new Color(0.4f, 0.4f, 0.45f), bold: false);

        _modeButtons = new Button[providers.Length][];
        _buttonImages = new Image[providers.Length][];
        _borderImages = new Image[providers.Length][];

        float rowH = 1f / (providers.Length + 1f);

        for (int p = 0; p < providers.Length; p++)
        {
            float rowTop = 1f - (p + 1) * rowH;

            MakeText($"P{p}Lbl", panelRt,
                     new Vector2(0.02f, rowTop),
                     new Vector2(0.28f, rowTop + rowH * 0.88f),
                     $"Player {p + 1}", 14,
                     new Color(0.85f, 0.85f, 0.90f), bold: true);

            _modeButtons[p] = new Button[ModeCount];
            _buttonImages[p] = new Image[ModeCount];
            _borderImages[p] = new Image[ModeCount];

            for (int m = 0; m < ModeCount; m++)
            {
                float bl = 0.28f + m * 0.355f;
                float br = bl + 0.34f;

                var (btn, bgImg, borderImg) = MakeButton(
                    ModeLabels[m], panelRt,
                    new Vector2(bl, rowTop + 0.04f),
                    new Vector2(br, rowTop + rowH * 0.86f),
                    inactiveColor);

                // Capture for closure
                int cp = p, cm = m;
                btn.onClick.AddListener(() =>
                {
                    _curRow = cp;
                    _curCol = cm;
                    ApplySelection();
                });

                _modeButtons[p][m] = btn;
                _buttonImages[p][m] = bgImg;
                _borderImages[p][m] = borderImg;
            }
        }

        RefreshVisuals();
    }

    // ── UI factory ───────────────────────────────────────────────

    private GameObject MakePanel(string name, RectTransform parent,
                                  Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
        return go;
    }

    private void MakeText(string name, RectTransform parent,
                           Vector2 amin, Vector2 amax,
                           string text, int size, Color col, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.color = col;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        var rt = txt.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// Returns (Button, backgroundImage, borderImage)
    private (Button, Image, Image) MakeButton(string label, RectTransform parent,
                                               Vector2 amin, Vector2 amax, Color color)
    {
        // Root container
        var go = new GameObject(label + "_btn");
        go.transform.SetParent(parent, false);
        var bgImg = go.AddComponent<Image>();
        bgImg.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        // Disable Unity's built-in colour transitions — we drive colour ourselves
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.colorMultiplier = 1f;
        btn.colors = cb;
        btn.transition = Selectable.Transition.None;

        // Label
        var tgo = new GameObject("Label");
        tgo.transform.SetParent(go.transform, false);
        var txt = tgo.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 13;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        var trt = txt.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        // Cursor border — a child Image with a hollow rectangle look (using outline trick)
        var borderGo = new GameObject("Border");
        borderGo.transform.SetParent(go.transform, false);
        var borderImg = borderGo.AddComponent<Image>();
        borderImg.color = cursorColor;
        var brt = borderImg.GetComponent<RectTransform>();
        // Stretch to fill parent with a 3-pixel inset to create border appearance
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(-3f, -3f);
        brt.offsetMax = new Vector2(3f, 3f);
        borderImg.enabled = false;

        // Add an Outline component to the border image for a hollow look
        var outline = borderGo.AddComponent<Outline>();
        outline.effectColor = cursorColor;
        outline.effectDistance = new Vector2(2f, 2f);

        // Make border transparent (just the outline is visible)
        borderImg.color = new Color(0f, 0f, 0f, 0f);

        return (btn, bgImg, borderImg);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}