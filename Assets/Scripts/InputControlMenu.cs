using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InputControllerMenu  v1
///
/// Renders a compact in-game overlay (toggled with Tab or Start button) that
/// lets each player independently switch between Keyboard, Gamepad, and Touch.
///
/// SETUP:
///   • Add to any persistent GameObject in the scene.
///   • Assign each player's VehicleInputProvider in the Inspector,
///     OR leave the array empty — it auto-finds all providers at Start.
/// </summary>
public class InputControllerMenu : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────

    [Header("Providers (auto-found if empty)")]
    public VehicleInputProvider[] providers;

    [Header("Toggle Key")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("Style")]
    public Color panelColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
    public Color activeColor = new Color(0.20f, 0.75f, 0.30f, 1.00f);
    public Color inactiveColor = new Color(0.25f, 0.25f, 0.30f, 1.00f);
    public Color headerColor = new Color(0.90f, 0.75f, 0.20f, 1.00f);

    // ──────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────

    private bool _visible = false;
    private Canvas _canvas;
    private GameObject _panel;

    // Per-provider button rows: [providerIndex][modeIndex] = Button
    private Button[][] _modeButtons;

    private static readonly string[] ModeLabels =
        { "⌨  Keyboard", "🎮  Gamepad", "👆  Touch" };

    // ──────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (providers == null || providers.Length == 0)
            providers = FindObjectsByType<VehicleInputProvider>(FindObjectsSortMode.None);

        BuildCanvas();
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)
            || Input.GetKeyDown(KeyCode.JoystickButton7))   // Start button
        {
            _visible = !_visible;
            _panel.SetActive(_visible);
            if (_visible) RefreshButtonColors();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Build UI
    // ──────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("InputMenu_Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        // ── Backdrop panel ───────────────────────────────────
        _panel = MakePanel("Panel",
            _canvas.transform as RectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(440f, 80f + providers.Length * 130f),
            panelColor);

        // ── Title ────────────────────────────────────────────
        MakeText("Controls", _panel.GetComponent<RectTransform>(),
                 new Vector2(0f, 0.95f), new Vector2(1f, 1f),
                 "INPUT SETTINGS", 18, headerColor, true);

        // ── Close hint ───────────────────────────────────────
        MakeText("Hint", _panel.GetComponent<RectTransform>(),
                 new Vector2(0f, 0f), new Vector2(1f, 0.07f),
                 $"[{toggleKey}] to close", 11,
                 new Color(0.5f, 0.5f, 0.55f), false);

        // ── Per-player rows ───────────────────────────────────
        _modeButtons = new Button[providers.Length][];
        float rowH = 1f / (providers.Length + 1f);

        for (int p = 0; p < providers.Length; p++)
        {
            int capturedP = p;
            float rowTop = 1f - (p + 1) * rowH;

            // Player label
            MakeText($"P{p}Label", _panel.GetComponent<RectTransform>(),
                     new Vector2(0.02f, rowTop),
                     new Vector2(0.28f, rowTop + rowH * 0.9f),
                     $"Player {p + 1}", 14,
                     new Color(0.85f, 0.85f, 0.90f), true);

            // Three mode buttons per player
            _modeButtons[p] = new Button[3];
            for (int m = 0; m < 3; m++)
            {
                int capturedM = m;
                float bLeft = 0.28f + m * 0.235f;
                float bRight = bLeft + 0.22f;

                var btn = MakeButton(
                    ModeLabels[m],
                    _panel.GetComponent<RectTransform>(),
                    new Vector2(bLeft, rowTop + 0.02f),
                    new Vector2(bRight, rowTop + rowH * 0.88f),
                    inactiveColor);

                btn.onClick.AddListener(() =>
                {
                    providers[capturedP].SetMode((VehicleInputProvider.InputMode)capturedM);
                    RefreshButtonColors();
                });

                _modeButtons[p][m] = btn;
            }
        }
    }

    private void RefreshButtonColors()
    {
        for (int p = 0; p < providers.Length; p++)
        {
            int active = (int)providers[p].mode;
            for (int m = 0; m < 3; m++)
            {
                if (_modeButtons[p][m] == null) continue;
                var colors = _modeButtons[p][m].colors;
                colors.normalColor = m == active ? activeColor : inactiveColor;
                colors.highlightedColor = m == active ? activeColor : new Color(0.35f, 0.35f, 0.40f);
                colors.selectedColor = colors.normalColor;
                _modeButtons[p][m].colors = colors;

                var img = _modeButtons[p][m].GetComponent<Image>();
                if (img != null) img.color = colors.normalColor;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  UI factory helpers
    // ──────────────────────────────────────────────────────────

    private GameObject MakePanel(string name, RectTransform parent,
                                  Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 sizeDelta, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = color;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = Vector2.zero;
        return go;
    }

    private void MakeText(string name, RectTransform parent,
                           Vector2 anchorMin, Vector2 anchorMax,
                           string text, int size, Color color, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;

        var rt = txt.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Button MakeButton(string label, RectTransform parent,
                               Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(label + "_btn");
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = color;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = color; btn.colors = cb;

        // Label
        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(go.transform, false);
        var txt = txtGo.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 12;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        var trt = txt.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        return btn;
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