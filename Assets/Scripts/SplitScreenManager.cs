using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SplitScreenManager  v2
///
/// Behaviour:
///   • Starts in single-player mode — one car, one full-screen camera,
///     P1 uses Keyboard by default.
///   • Every update it polls Input.GetJoystickNames().  The first time a
///     gamepad appears (count goes from 0 → 1+) it automatically:
///       – Spawns Car P2
///       – Splits the screen vertically (left/right)
///       – Assigns the gamepad to P2, keeps keyboard for P1
///       – Adds P2's speedometer and label
///       – Draws the divider line
///       – Registers P2 with the ground generator
///   • If the gamepad is removed (count drops to 0) it reverts to
///     single-player: P2 car is hidden (deactivated), camera goes full-screen,
///     divider removed.  Re-plugging the same gamepad re-activates P2.
///
/// SETUP:
///   1. Add to any persistent GameObject (e.g. "GameManager").
///   2. Optionally assign car1Prefab / car2Prefab and spawn points.
///   3. Press Play — single-player starts immediately.
/// </summary>
public class SplitScreenManager : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────

    [Header("Car Prefabs  (placeholder box-car used if null)")]
    public GameObject car1Prefab;
    public GameObject car2Prefab;

    [Header("Spawn Points")]
    public Transform spawnPoint1;
    public Transform spawnPoint2;

    [Header("Camera Follow")]
    public float followDistance = 7f;
    public float followHeight = 3.5f;
    public float followSmooth = 5f;

    [Header("HUD")]
    public float speedometerDiameter = 180f;

    [Header("Gamepad Detection")]
    [Tooltip("Seconds between polling Input.GetJoystickNames().")]
    public float pollInterval = 1.0f;

    // ──────────────────────────────────────────────────────────
    //  Runtime state
    // ──────────────────────────────────────────────────────────

    private GameObject _car1, _car2;
    private Camera _cam1, _cam2;
    private VehicleInputProvider _input1, _input2;
    private VehicleController _ctrl1, _ctrl2;
    private SpeedometerHUD _hud1, _hud2;

    private bool _splitActive = false;
    private float _pollTimer = 0f;
    private int _lastPadCount = 0;

    // Divider line image reference so we can show/hide it
    private GameObject _dividerGo;

    // P2 label canvas
    private GameObject _labelP2;

    // ──────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────

    private void Start()
    {
        // ── Single-player setup ───────────────────────────────
        SpawnCar1();
        SetupCamera1();

        _input1 = EnsureProvider(_car1, 0, VehicleInputProvider.InputMode.Keyboard);

        _hud1 = AttachSpeedometer(_cam1.gameObject, _ctrl1);
        CreatePlayerLabel(_cam1, "P1", new Color(0.3f, 0.6f, 1.0f));

        // Input menu (only keyboard / gamepad toggles)
        SetupInputMenu();

        // Wire the single car to the ground generator
        WireGroundGenerator();

        // Force an immediate poll so we don't wait a full second
        _lastPadCount = VehicleInputProvider.ConnectedGamepadCount();
        if (_lastPadCount > 0)
            EnableSplitScreen();
    }

    private void Update()
    {
        // Camera follow
        FollowCar(_cam1, _ctrl1);
        if (_splitActive) FollowCar(_cam2, _ctrl2);

        // Poll for gamepad changes
        _pollTimer += Time.deltaTime;
        if (_pollTimer < pollInterval) return;
        _pollTimer = 0f;

        int padCount = VehicleInputProvider.ConnectedGamepadCount();

        if (!_splitActive && padCount > 0)
            EnableSplitScreen();
        else if (_splitActive && padCount == 0)
            DisableSplitScreen();

        _lastPadCount = padCount;
    }

    // ──────────────────────────────────────────────────────────
    //  Split-screen enable / disable
    // ──────────────────────────────────────────────────────────

    private void EnableSplitScreen()
    {
        if (_splitActive) return;
        _splitActive = true;

        // ── Spawn / activate Car 2 ────────────────────────────
        if (_car2 == null)
        {
            Vector3 p2 = spawnPoint2 != null ? spawnPoint2.position : new Vector3(3f, 1f, 0f);
            Quaternion r2 = spawnPoint2 != null ? spawnPoint2.rotation : Quaternion.identity;

            _car2 = car2Prefab != null
                ? Instantiate(car2Prefab, p2, r2)
                : CreatePlaceholderCar("Car_P2", p2, r2, new Color(1f, 0.3f, 0.2f));
            _ctrl2 = _car2.GetComponentInChildren<VehicleController>();
        }
        else
        {
            _car2.SetActive(true);
        }

        // ── Assign gamepad to P2 ──────────────────────────────
        // gamepadSlotRank = 0 means "use the first physically connected pad"
        // regardless of which USB port it is plugged into.
        _input2 = EnsureProvider(_car2, 1, VehicleInputProvider.InputMode.Gamepad);
        _input2.gamepadSlotRank = 0;

        // ── Camera 2 ─────────────────────────────────────────
        if (_cam2 == null) _cam2 = CreateCamera("Cam_P2", 1);
        _cam2.gameObject.SetActive(true);

        // Apply vertical split
        _cam1.rect = new Rect(0f, 0f, 0.5f, 1f);
        _cam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);

        // ── HUD ───────────────────────────────────────────────
        if (_hud2 == null)
            _hud2 = AttachSpeedometer(_cam2.gameObject, _ctrl2);
        _cam2.gameObject.SetActive(true);

        if (_labelP2 == null)
            _labelP2 = CreatePlayerLabel(_cam2, "P2", new Color(1.0f, 0.35f, 0.2f));
        _labelP2.SetActive(true);

        // ── Divider line ──────────────────────────────────────
        if (_dividerGo == null) BuildDivider();
        _dividerGo.SetActive(true);

        // ── Update input menu ─────────────────────────────────
        var menu = FindFirstObjectByType<InputControllerMenu>();
        if (menu != null)
            menu.providers = new[] { _input1, _input2 };

        // ── Ground generator ──────────────────────────────────
        WireGroundGenerator();

        Debug.Log("[SplitScreenManager] Gamepad detected — split-screen ON");
    }

    private void DisableSplitScreen()
    {
        if (!_splitActive) return;
        _splitActive = false;

        // Full-screen for P1
        _cam1.rect = new Rect(0f, 0f, 1f, 1f);

        // Hide P2 camera, car, HUD, label, divider
        if (_cam2 != null) _cam2.gameObject.SetActive(false);
        if (_car2 != null) _car2.SetActive(false);
        if (_dividerGo != null) _dividerGo.SetActive(false);
        if (_labelP2 != null) _labelP2.SetActive(false);

        // Update input menu to only show P1
        var menu = FindFirstObjectByType<InputControllerMenu>();
        if (menu != null)
            menu.providers = new[] { _input1 };

        // Remove P2 from ground generator
        WireGroundGenerator();

        Debug.Log("[SplitScreenManager] Gamepad removed — single-player mode");
    }

    // ──────────────────────────────────────────────────────────
    //  Car spawning
    // ──────────────────────────────────────────────────────────

    private void SpawnCar1()
    {
        Vector3 p1 = spawnPoint1 != null ? spawnPoint1.position : new Vector3(-3f, 1f, 0f);
        Quaternion r1 = spawnPoint1 != null ? spawnPoint1.rotation : Quaternion.identity;

        _car1 = car1Prefab != null
            ? Instantiate(car1Prefab, p1, r1)
            : CreatePlaceholderCar("Car_P1", p1, r1, new Color(0.2f, 0.5f, 1f));
        _ctrl1 = _car1.GetComponentInChildren<VehicleController>();
    }

    private GameObject CreatePlaceholderCar(string name, Vector3 pos,
                                             Quaternion rot, Color col)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.rotation = rot;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(1.8f, 0.6f, 4f);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.GetComponent<Renderer>().material.color = col;
        Destroy(body.GetComponent<BoxCollider>());

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.1f;
        go.AddComponent<VehicleController>();
        return go;
    }

    // ──────────────────────────────────────────────────────────
    //  Cameras
    // ──────────────────────────────────────────────────────────

    private void SetupCamera1()
    {
        if (Camera.main != null) Camera.main.gameObject.SetActive(false);
        _cam1 = CreateCamera("Cam_P1", 0);
        _cam1.rect = new Rect(0f, 0f, 1f, 1f);
    }

    private Camera CreateCamera(string name, int playerIdx)
    {
        var go = new GameObject(name);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 500f;
        var al = go.AddComponent<AudioListener>();
        if (playerIdx > 0) al.enabled = false;
        return cam;
    }

    private void FollowCar(Camera cam, VehicleController ctrl)
    {
        if (cam == null || ctrl == null) return;
        Transform t = ctrl.transform;
        Vector3 target = t.position - t.forward * followDistance + Vector3.up * followHeight;
        cam.transform.position = Vector3.Lerp(cam.transform.position, target,
                                              Time.deltaTime * followSmooth);
        cam.transform.LookAt(t.position + Vector3.up * 0.5f);
    }

    // ──────────────────────────────────────────────────────────
    //  Input providers
    // ──────────────────────────────────────────────────────────

    private VehicleInputProvider EnsureProvider(GameObject car, int idx,
                                                VehicleInputProvider.InputMode mode)
    {
        var p = car.GetComponent<VehicleInputProvider>();
        if (p == null) p = car.AddComponent<VehicleInputProvider>();
        p.playerIndex = idx;
        p.SetMode(mode);
        return p;
    }

    // ──────────────────────────────────────────────────────────
    //  HUDs
    // ──────────────────────────────────────────────────────────

    private SpeedometerHUD AttachSpeedometer(GameObject camGo, VehicleController ctrl)
    {
        if (ctrl == null) return null;
        var hud = camGo.AddComponent<SpeedometerHUD>();
        hud.vehicle = ctrl;
        hud.targetCamera = camGo.GetComponent<Camera>();
        hud.gaugeDiameter = speedometerDiameter;
        hud.margin = new Vector2(14f, 14f);
        return hud;
    }

    private GameObject CreatePlayerLabel(Camera cam, string label, Color col)
    {
        var go = new GameObject($"Label_{label}");
        go.transform.SetParent(cam.transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 50;
        go.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(canvas.transform, false);
        var txt = txtGo.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 28;
        txt.fontStyle = FontStyle.Bold;
        txt.color = col;
        var rt = txt.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.88f);
        rt.anchorMax = new Vector2(0.2f, 1.0f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    // ──────────────────────────────────────────────────────────
    //  Divider
    // ──────────────────────────────────────────────────────────

    private void BuildDivider()
    {
        _dividerGo = new GameObject("SplitDivider");
        var canvas = _dividerGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        _dividerGo.AddComponent<CanvasScaler>();

        var img = new GameObject("Line").AddComponent<Image>();
        img.transform.SetParent(canvas.transform, false);
        img.color = new Color(0.05f, 0.05f, 0.06f, 1f);

        var rt = img.GetComponent<RectTransform>();
        // Vertical divider for left/right split
        rt.anchorMin = new Vector2(0.499f, 0f);
        rt.anchorMax = new Vector2(0.501f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ──────────────────────────────────────────────────────────
    //  Input menu
    // ──────────────────────────────────────────────────────────

    private void SetupInputMenu()
    {
        var menuGo = new GameObject("InputControllerMenu");
        var menu = menuGo.AddComponent<InputControllerMenu>();
        menu.providers = new[] { _input1 };
    }

    // ──────────────────────────────────────────────────────────
    //  Ground generator
    // ──────────────────────────────────────────────────────────

    private void WireGroundGenerator()
    {
        var gen = FindFirstObjectByType<ProceduralGroundGenerator>();
        if (gen == null) return;

        var list = new List<Transform>();
        if (_ctrl1 != null && _car1 != null && _car1.activeSelf)
            list.Add(_ctrl1.transform);
        if (_splitActive && _ctrl2 != null && _car2 != null && _car2.activeSelf)
            list.Add(_ctrl2.transform);
        gen.targets = list.ToArray();
    }
}