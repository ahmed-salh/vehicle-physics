using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// TouchInputOverlay  v1
///
/// Creates a full-screen Canvas with virtual on-screen controls for one player.
/// Instantiated automatically by VehicleInputProvider when mode = Touch.
///
/// Layout (anchored so it respects split-screen viewports):
///
///   LEFT HALF                    RIGHT HALF
///   ┌────────────────────────────────────────┐
///   │                                        │
///   │  [Steering wheel joystick]  [GAS] [BRK]│
///   │                                        │
///   │                            [DRIFT]     │
///   └────────────────────────────────────────┘
///
/// • Steering joystick: drag thumb inside a round pad → steer value
/// • GAS / BRK : hold-buttons for throttle and brake
/// • DRIFT : momentary button
///
/// Each control is rendered using Unity UI Image components —
/// no sprites required (uses solid colour rounded rects generated at runtime).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class TouchInputOverlay : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  Factory
    // ──────────────────────────────────────────────────────────

    /// <summary>Creates a TouchInputOverlay for the given provider and returns it.</summary>
    public static TouchInputOverlay CreateFor(VehicleInputProvider provider)
    {
        var go = new GameObject($"TouchOverlay_P{provider.playerIndex}");
        DontDestroyOnLoad(go);

        // Canvas
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100 + provider.playerIndex;

        go.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.AddComponent<GraphicRaycaster>();

        // Ensure EventSystem exists
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var overlay = go.AddComponent<TouchInputOverlay>();
        overlay._provider = provider;
        return overlay;
    }

    // ──────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────

    private VehicleInputProvider _provider;
    private Canvas _canvas;

    // Joystick state
    private RectTransform _joystickBg;
    private RectTransform _joystickThumb;
    private int _joystickTouchId = -1;
    private Vector2 _joystickCenter;
    private float _joystickRadius;

    // Button state
    private bool _gasDown, _brakeDown, _driftDown;
    private int _gasTouchId = -1, _brakeTouchId = -1, _driftTouchId = -1;

    // ──────────────────────────────────────────────────────────
    //  Awake — build UI
    // ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        BuildUI();
    }

    private void BuildUI()
    {
        float sw = Screen.width, sh = Screen.height;
        float pad = sh * 0.05f;

        // ── Joystick (bottom-left quadrant) ─────────────────
        float joyR = sh * 0.14f;
        float joyX = sw * 0.18f;
        float joyY = sh - pad - joyR;

        _joystickBg = MakeCircle("JoyBg",
            new Vector2(joyX, joyY), joyR * 2f,
            new Color(1f, 1f, 1f, 0.15f));

        _joystickThumb = MakeCircle("JoyThumb",
            new Vector2(joyX, joyY), joyR * 0.55f,
            new Color(1f, 1f, 1f, 0.55f),
            _joystickBg);

        _joystickCenter = new Vector2(joyX, joyY);
        _joystickRadius = joyR;

        // ── Buttons (bottom-right) ───────────────────────────
        float btnSize = sh * 0.12f;
        float btnPad = btnSize * 0.25f;
        float btnBaseX = sw - pad - btnSize;
        float btnBaseY = sh - pad - btnSize;

        MakeButton("GAS", new Vector2(btnBaseX, btnBaseY),
                   btnSize, new Color(0.20f, 0.85f, 0.30f, 0.75f),
                   OnGasDown, OnGasUp);

        MakeButton("BRK", new Vector2(btnBaseX - btnSize - btnPad, btnBaseY),
                   btnSize, new Color(0.90f, 0.25f, 0.15f, 0.75f),
                   OnBrakeDown, OnBrakeUp);

        MakeButton("DRIFT", new Vector2(btnBaseX, btnBaseY - btnSize - btnPad),
                   btnSize, new Color(0.20f, 0.55f, 1.00f, 0.75f),
                   OnDriftDown, OnDriftUp);
    }

    // ──────────────────────────────────────────────────────────
    //  Update — poll touches
    // ──────────────────────────────────────────────────────────

    private void Update()
    {
        ProcessJoystickTouches();
        WriteToProvider();
    }

    private void ProcessJoystickTouches()
    {
        // Touch began
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            if (t.phase == TouchPhase.Began)
            {
                // Joystick area: left half, lower third of screen
                if (t.position.x < Screen.width * 0.45f &&
                    t.position.y < Screen.height * 0.50f &&
                    _joystickTouchId < 0)
                {
                    _joystickTouchId = t.fingerId;
                    _joystickCenter = t.position;   // re-anchor on press
                    UpdateJoystick(t.position);
                }
            }

            if (t.fingerId == _joystickTouchId)
            {
                if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                    UpdateJoystick(t.position);

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    _joystickTouchId = -1;
                    _provider.touchSteer = 0f;
                    _provider.touchThrottle = 0f;
                    RecentreThumb();
                }
            }
        }

        // Mouse fallback (editor)
        if (Application.isEditor)
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mp = Input.mousePosition;
                if (mp.x < Screen.width * 0.45f && mp.y < Screen.height * 0.50f)
                {
                    _joystickCenter = mp;
                    _joystickTouchId = 999;
                    UpdateJoystick(mp);
                }
            }
            if (_joystickTouchId == 999)
            {
                if (Input.GetMouseButton(0)) UpdateJoystick(Input.mousePosition);
                if (Input.GetMouseButtonUp(0)) { _joystickTouchId = -1; _provider.touchSteer = 0f; _provider.touchThrottle = 0f; RecentreThumb(); }
            }
        }
    }

    private void UpdateJoystick(Vector2 touchPos)
    {
        Vector2 delta = touchPos - _joystickCenter;
        float clamped = Mathf.Min(delta.magnitude, _joystickRadius);
        Vector2 dir = delta.normalized;
        Vector2 pos = _joystickCenter + dir * clamped;

        _provider.touchSteer = Mathf.Clamp(delta.x / _joystickRadius, -1f, 1f);
        _provider.touchThrottle = Mathf.Clamp(delta.y / _joystickRadius, -1f, 1f);

        // Move thumb visual (convert screen to local canvas coords)
        if (_joystickThumb != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _joystickBg, pos, null, out Vector2 local);
            _joystickThumb.anchoredPosition = local;
        }
    }

    private void RecentreThumb()
    {
        if (_joystickThumb != null)
            _joystickThumb.anchoredPosition = Vector2.zero;
    }

    private void WriteToProvider()
    {
        _provider.touchThrottle = _gasDown ? 1f : _provider.touchThrottle;
        _provider.touchBrake = _brakeDown ? 1f : 0f;
        _provider.touchDrift = _driftDown;
    }

    // ──────────────────────────────────────────────────────────
    //  Button callbacks
    // ──────────────────────────────────────────────────────────

    private void OnGasDown() { _gasDown = true; }
    private void OnGasUp() { _gasDown = false; }
    private void OnBrakeDown() { _brakeDown = true; }
    private void OnBrakeUp() { _brakeDown = false; }
    private void OnDriftDown() { _driftDown = true; }
    private void OnDriftUp() { _driftDown = false; }

    // ──────────────────────────────────────────────────────────
    //  UI Factories
    // ──────────────────────────────────────────────────────────

    private RectTransform MakeCircle(string name, Vector2 screenPos, float size,
                                      Color col, RectTransform parent = null)
    {
        var go = new GameObject(name);
        var img = go.AddComponent<Image>();
        img.color = col;
        img.sprite = MakeCircleSprite();
        img.type = Image.Type.Simple;

        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent != null ? parent : _canvas.transform as RectTransform, false);
        rt.sizeDelta = new Vector2(size, size);

        // Convert screen position to canvas anchored position
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform, screenPos, null, out Vector2 local);
        rt.anchoredPosition = local;
        return rt;
    }

    private void MakeButton(string label, Vector2 screenPos, float size, Color col,
                             UnityEngine.Events.UnityAction onDown,
                             UnityEngine.Events.UnityAction onUp)
    {
        var go = new GameObject(label + "Btn");
        var img = go.AddComponent<Image>();
        img.color = col;
        img.sprite = MakeCircleSprite();

        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_canvas.transform, false);
        rt.sizeDelta = new Vector2(size, size);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform, screenPos, null, out Vector2 local);
        rt.anchoredPosition = local;

        // Label
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var txt = textGo.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = Mathf.RoundToInt(size * 0.30f);
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        var txtRt = txt.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;

        // EventTrigger for pointer down/up
        var et = go.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => onDown());
        et.triggers.Add(downEntry);

        var upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        upEntry.callback.AddListener(_ => onUp());
        et.triggers.Add(upEntry);
    }

    // Creates a simple 64×64 circle sprite procedurally
    private static Sprite _circleSprite;
    private static Sprite MakeCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;

        int res = 64;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float r = res * 0.5f;
        float r2 = r * r;

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float alpha = Mathf.Clamp01((r2 - (dx * dx + dy * dy)) / (r * 2f));
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        tex.Apply();

        _circleSprite = Sprite.Create(tex, new Rect(0, 0, res, res),
                                      new Vector2(0.5f, 0.5f), 100f);
        return _circleSprite;
    }
}