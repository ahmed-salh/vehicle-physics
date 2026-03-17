using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InputControllerMenu  v2
///
/// Changes from v1:
///   • Touch option removed — only Keyboard and Gamepad buttons shown.
///   • Tab toggles the panel open/closed.
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
    public Color headerColor = new Color(0.90f, 0.75f, 0.20f, 1.00f);

    private bool _visible;
    private Canvas _canvas;
    private GameObject _panel;
    private Button[][] _modeButtons;

    private static readonly string[] ModeLabels = { "⌨  Keyboard", "🎮  Gamepad" };

    private void Start()
    {
        if (providers == null || providers.Length == 0)
            providers = FindObjectsByType<VehicleInputProvider>(FindObjectsSortMode.None);

        BuildCanvas();
        _panel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.JoystickButton7))
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
        var cgo = new GameObject("InputMenu_Canvas");
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        cgo.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cgo.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        float panelH = 80f + providers.Length * 110f;
        _panel = MakePanel("Panel",
            _canvas.transform as RectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(360f, panelH), panelColor);

        MakeText("Title", _panel.GetComponent<RectTransform>(),
                 new Vector2(0f, 0.92f), new Vector2(1f, 1f),
                 "INPUT SETTINGS", 18, headerColor, bold: true);

        MakeText("Hint", _panel.GetComponent<RectTransform>(),
                 new Vector2(0f, 0f), new Vector2(1f, 0.08f),
                 $"[{toggleKey}] toggle", 11,
                 new Color(0.4f, 0.4f, 0.45f), bold: false);

        _modeButtons = new Button[providers.Length][];
        float rowH = 1f / (providers.Length + 1f);

        for (int p = 0; p < providers.Length; p++)
        {
            int cp = p;
            float rowTop = 1f - (p + 1) * rowH;

            MakeText($"P{p}Lbl", _panel.GetComponent<RectTransform>(),
                     new Vector2(0.02f, rowTop),
                     new Vector2(0.30f, rowTop + rowH * 0.88f),
                     $"Player {p + 1}", 14,
                     new Color(0.85f, 0.85f, 0.90f), bold: true);

            _modeButtons[p] = new Button[2];
            for (int m = 0; m < 2; m++)
            {
                int cm = m;
                float bl = 0.30f + m * 0.34f;
                float br = bl + 0.32f;

                var btn = MakeButton(ModeLabels[m],
                    _panel.GetComponent<RectTransform>(),
                    new Vector2(bl, rowTop + 0.04f),
                    new Vector2(br, rowTop + rowH * 0.86f),
                    inactiveColor);

                btn.onClick.AddListener(() =>
                {
                    providers[cp].SetMode((VehicleInputProvider.InputMode)cm);
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
            for (int m = 0; m < 2; m++)
            {
                if (_modeButtons[p][m] == null) continue;
                bool on = (m == active);
                Color c = on ? activeColor : inactiveColor;

                var img = _modeButtons[p][m].GetComponent<Image>();
                if (img) img.color = c;

                var cb = _modeButtons[p][m].colors;
                cb.normalColor = c;
                cb.highlightedColor = on ? c : new Color(0.32f, 0.32f, 0.36f);
                cb.selectedColor = c;
                _modeButtons[p][m].colors = cb;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  UI factory helpers
    // ──────────────────────────────────────────────────────────

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

    private Button MakeButton(string label, RectTransform parent,
                               Vector2 amin, Vector2 amax, Color color)
    {
        var go = new GameObject(label + "_btn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        var cb = btn.colors; cb.normalColor = color; btn.colors = cb;

        var tgo = new GameObject("Label");
        tgo.transform.SetParent(go.transform, false);
        var txt = tgo.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 12;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        var trt = txt.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
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