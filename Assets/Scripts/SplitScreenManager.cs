using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SplitScreenManager  v1
///
/// Sets up a two-player split-screen session:
///   • Spawns / references two car prefabs
///   • Creates two cameras (top / bottom horizontal split, or left / right)
///   • Each camera follows its own car (smooth follow)
///   • Assigns a VehicleInputProvider to each car
///   • Adds a SpeedometerHUD anchored inside each viewport
///   • Adds a player-label overlay (P1 / P2) in each viewport corner
///   • Draws a thin dividing line between the two viewports
///
/// SETUP:
///   1. Add SplitScreenManager to any persistent GameObject (e.g. "GameManager").
///   2. Assign car1Prefab and car2Prefab (they need VehicleController on root).
///   3. Assign spawnPoint1 / spawnPoint2 Transforms, or leave null for defaults.
///   4. Press Play.
///
/// The InputControllerMenu is also started automatically so players can
/// switch input modes with [Tab] at any time.
/// </summary>
public class SplitScreenManager : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────

    [Header("Car Prefabs")]
    [Tooltip("Prefab with VehicleController on the root.")]
    public GameObject car1Prefab;
    public GameObject car2Prefab;

    [Header("Spawn Points")]
    public Transform spawnPoint1;
    public Transform spawnPoint2;

    [Header("Split Direction")]
    [Tooltip("Horizontal = top/bottom split. Vertical = left/right split.")]
    public SplitMode splitMode = SplitMode.Horizontal;

    [Header("Camera Follow")]
    public float followDistance = 7f;
    public float followHeight = 3.5f;
    public float followSmooth = 5f;

    [Header("Default Input Modes")]
    public VehicleInputProvider.InputMode player1Mode = VehicleInputProvider.InputMode.Keyboard;
    public VehicleInputProvider.InputMode player2Mode = VehicleInputProvider.InputMode.Keyboard;

    [Header("HUD")]
    public float speedometerDiameter = 160f;

    public enum SplitMode { Horizontal, Vertical }

    // ──────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────

    private GameObject _car1, _car2;
    private Camera _cam1, _cam2;
    private VehicleInputProvider _input1, _input2;
    private VehicleController _ctrl1, _ctrl2;

    // ──────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────

    private void Start()
    {
        SpawnCars();
        SetupCameras();
        SetupInputProviders();
        SetupHUDs();
        SetupInputMenu();
        SetupDividerLine();
        WireGroundGenerator();
    }

    private void LateUpdate()
    {
        FollowCar(_cam1, _ctrl1);
        FollowCar(_cam2, _ctrl2);
    }

    // ──────────────────────────────────────────────────────────
    //  Cars
    // ──────────────────────────────────────────────────────────

    private void SpawnCars()
    {
        // Default spawn positions if none assigned
        Vector3 p1 = spawnPoint1 != null ? spawnPoint1.position : new Vector3(-3f, 1f, 0f);
        Vector3 p2 = spawnPoint2 != null ? spawnPoint2.position : new Vector3(3f, 1f, 0f);
        Quaternion r1 = spawnPoint1 != null ? spawnPoint1.rotation : Quaternion.identity;
        Quaternion r2 = spawnPoint2 != null ? spawnPoint2.rotation : Quaternion.identity;

        _car1 = car1Prefab != null
            ? Instantiate(car1Prefab, p1, r1)
            : CreatePlaceholderCar("Car_P1", p1, r1, new Color(0.2f, 0.5f, 1f));

        _car2 = car2Prefab != null
            ? Instantiate(car2Prefab, p2, r2)
            : CreatePlaceholderCar("Car_P2", p2, r2, new Color(1f, 0.3f, 0.2f));

        _ctrl1 = _car1.GetComponentInChildren<VehicleController>();
        _ctrl2 = _car2.GetComponentInChildren<VehicleController>();
    }

    /// Minimal placeholder car (box mesh + Rigidbody + VehicleController) used when
    /// no prefab is assigned — lets you test split-screen without a full car asset.
    private GameObject CreatePlaceholderCar(string name, Vector3 pos, Quaternion rot, Color col)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.rotation = rot;

        // Body mesh
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(1.8f, 0.6f, 4f);
        body.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        body.GetComponent<Renderer>().material.color = col;
        Destroy(body.GetComponent<BoxCollider>());

        // Rigidbody
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.1f;

        // VehicleController — wheels will be empty so it won't drive, but at least
        // won't throw null refs
        go.AddComponent<VehicleController>();

        return go;
    }

    // ──────────────────────────────────────────────────────────
    //  Cameras
    // ──────────────────────────────────────────────────────────

    private void SetupCameras()
    {
        // Disable any existing main camera that might conflict
        if (Camera.main != null) Camera.main.gameObject.SetActive(false);

        _cam1 = CreateCamera("Cam_P1", 0);
        _cam2 = CreateCamera("Cam_P2", 1);

        ApplySplitViewports();
    }

    private Camera CreateCamera(string name, int playerIdx)
    {
        var go = new GameObject(name);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 500f;

        // Give each camera its own audio listener — only one may be enabled
        var al = go.AddComponent<AudioListener>();
        if (playerIdx > 0) al.enabled = false;

        return cam;
    }

    private void ApplySplitViewports()
    {
        if (splitMode == SplitMode.Horizontal)
        {
            // P1 = top half, P2 = bottom half
            _cam1.rect = new Rect(0f, 0.5f, 1f, 0.5f);
            _cam2.rect = new Rect(0f, 0.0f, 1f, 0.5f);
        }
        else
        {
            // P1 = left half, P2 = right half
            _cam1.rect = new Rect(0.0f, 0f, 0.5f, 1f);
            _cam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        }
    }

    private void FollowCar(Camera cam, VehicleController ctrl)
    {
        if (cam == null || ctrl == null) return;

        Transform t = ctrl.transform;
        Vector3 target = t.position
                        - t.forward * followDistance
                        + Vector3.up * followHeight;

        cam.transform.position = Vector3.Lerp(
            cam.transform.position, target,
            Time.deltaTime * followSmooth);

        cam.transform.LookAt(t.position + Vector3.up * 0.5f);
    }

    // ──────────────────────────────────────────────────────────
    //  Input providers
    // ──────────────────────────────────────────────────────────

    private void SetupInputProviders()
    {
        _input1 = EnsureProvider(_car1, 0, player1Mode);
        _input2 = EnsureProvider(_car2, 1, player2Mode);
    }

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

    private void SetupHUDs()
    {
        // SpeedometerHUD reads from VehicleController via property.
        // We use a separate camera-attached GO per player so each HUD
        // can be positioned relative to its own viewport.
        AttachSpeedometer(_cam1.gameObject, _ctrl1, 0);
        AttachSpeedometer(_cam2.gameObject, _ctrl2, 1);

        // Player labels
        CreatePlayerLabel(_cam1, "P1", new Color(0.3f, 0.6f, 1.0f));
        CreatePlayerLabel(_cam2, "P2", new Color(1.0f, 0.35f, 0.2f));
    }

    private void AttachSpeedometer(GameObject camGo, VehicleController ctrl, int playerIdx)
    {
        if (ctrl == null) return;

        var hud = camGo.AddComponent<SpeedometerHUD>();
        hud.vehicle = ctrl;
        hud.targetCamera = camGo.GetComponent<Camera>();   // bind to THIS camera
        hud.gaugeDiameter = speedometerDiameter;
        hud.margin = new Vector2(14f, 14f);
    }

    private void CreatePlayerLabel(Camera cam, string label, Color col)
    {
        // World-space UI canvas parented to camera
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
        rt.anchorMax = new Vector2(0.15f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ──────────────────────────────────────────────────────────
    //  Input menu
    // ──────────────────────────────────────────────────────────

    private void SetupInputMenu()
    {
        var menuGo = new GameObject("InputControllerMenu");
        var menu = menuGo.AddComponent<InputControllerMenu>();
        menu.providers = new[] { _input1, _input2 };
    }

    // ──────────────────────────────────────────────────────────
    //  Divider line
    // ──────────────────────────────────────────────────────────

    private void SetupDividerLine()
    {
        var go = new GameObject("SplitDivider");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        go.AddComponent<CanvasScaler>();

        var img = new GameObject("Line").AddComponent<Image>();
        img.transform.SetParent(canvas.transform, false);
        img.color = new Color(0.05f, 0.05f, 0.06f, 1f);

        var rt = img.GetComponent<RectTransform>();
        if (splitMode == SplitMode.Horizontal)
        {
            // Thin horizontal bar at centre
            rt.anchorMin = new Vector2(0f, 0.499f);
            rt.anchorMax = new Vector2(1f, 0.501f);
        }
        else
        {
            // Thin vertical bar at centre
            rt.anchorMin = new Vector2(0.499f, 0f);
            rt.anchorMax = new Vector2(0.501f, 1f);
        }
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ── Ground generator: register both cars as targets ──────────
    private void WireGroundGenerator()
    {
        var gen = FindFirstObjectByType<ProceduralGroundGenerator>();
        if (gen == null) return;

        var list = new System.Collections.Generic.List<Transform>();
        if (_ctrl1 != null) list.Add(_ctrl1.transform);
        if (_ctrl2 != null) list.Add(_ctrl2.transform);
        gen.targets = list.ToArray();
    }
}