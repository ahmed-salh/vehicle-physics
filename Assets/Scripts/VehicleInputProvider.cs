using UnityEngine;

/// <summary>
/// VehicleInputProvider  v8
///
/// New gamepad layout:
///   Left stick X / D-pad X  → Steer
///   R2 (axis 4 DirectInput / axis 5 XInput)  → Forward throttle
///   L2 (axis 5 DirectInput / axis 4 XInput)  → Reverse throttle
///   Button 3  (num4 / Y / Triangle)          → Brake
///   Button 2  (num3 / X / Square)            → Drift
///   Button 4  (L1 / LB)                      → Open/close Input Menu
///   Button 5  (R1 / RB)                      → Look-back (hold)
///
/// The old throttle-on-stick behaviour is REMOVED.
/// Left stick Y and D-pad Y are ignored for driving.
///
/// Triggers (R2/L2) on this pad (DirectInput / Twin USB):
///   axis 4 = R2   (rests at -1, pressed = +1)
///   axis 5 = L2   (rests at -1, pressed = +1)
/// Public bool LookBack is read by SplitScreenManager to flip the camera.
/// Public bool MenuToggle fires ONE frame when L1 is pressed (GetKeyDown).
/// </summary>
public class VehicleInputProvider : MonoBehaviour
{
    public enum InputMode { Keyboard, Gamepad }

    // ──────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────

    [Header("Identity")]
    public int playerIndex = 0;

    [Header("Active Mode")]
    public InputMode mode = InputMode.Keyboard;

    [Header("Keyboard Bindings")]
    public KeyCode keyForward = KeyCode.W;
    public KeyCode keyBack = KeyCode.S;
    public KeyCode keyLeft = KeyCode.A;
    public KeyCode keyRight = KeyCode.D;
    public KeyCode keyBrake = KeyCode.LeftShift;
    public KeyCode keyDrift = KeyCode.Space;

    [Header("Gamepad — Slot")]
    [Tooltip("0 = first connected pad, 1 = second. Defaults to playerIndex.")]
    public int gamepadSlotRank = 0;

    [Header("Gamepad — Axes")]
    [Tooltip("Left stick X axis index. Default 0.")]
    public int axisSteer = 0;
    [Tooltip("Left stick Y axis index. Default 1. Also used by D-pad up/down.")]
    public int axisThrottle = 1;
    [Tooltip("Invert throttle axis. Most pads report stick-up as -1 — enable to flip.")]
    public bool invertThrottle = true;
    [Range(0f, 0.35f)]
    public float deadZone = 0.12f;

    [Header("Gamepad — Buttons (0-based)")]
    [Tooltip("Brake.     Button 3 = Y / Triangle / num4.")]
    public int btnBrake = 3;
    [Tooltip("Drift.     Button 2 = X / Square   / num3.")]
    public int btnDrift = 2;
    [Tooltip("Look back. Button 5 = R1 / RB.")]
    public int btnLookBack = 5;
    [Tooltip("Menu open/close. Button 4 = L1 / LB.")]
    public int btnMenuToggle = 4;

    // ──────────────────────────────────────────────────────────
    //  Output  (read by VehicleController and SplitScreenManager)
    // ──────────────────────────────────────────────────────────

    public float Throttle { get; private set; }   // -1 reverse … +1 forward
    public float Steer { get; private set; }   // -1 left … +1 right
    public float Brake { get; private set; }   //  0 … 1
    public bool Drift { get; private set; }
    public bool LookBack { get; private set; }   // true while R1 held
    public bool MenuToggle { get; private set; }   // true for ONE frame on L1 down

    // ──────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────

    private int _slot = 1;
    private float _rescanTimer = 0f;

    // ──────────────────────────────────────────────────────────
    //  Awake
    // ──────────────────────────────────────────────────────────

    private void Awake()
    {
        // P2 keyboard defaults
        if (playerIndex == 1 && keyForward == KeyCode.W)
        {
            keyForward = KeyCode.I;
            keyBack = KeyCode.K;
            keyLeft = KeyCode.J;
            keyRight = KeyCode.L;
            keyBrake = KeyCode.RightShift;
            keyDrift = KeyCode.RightControl;
        }

        gamepadSlotRank = playerIndex;
        ResolveSlot();
    }

    // ──────────────────────────────────────────────────────────
    //  Update
    // ──────────────────────────────────────────────────────────

    private void Update()
    {
        // Always reset one-shot outputs
        MenuToggle = false;

        if (mode == InputMode.Gamepad)
        {
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f) { _rescanTimer = 2f; ResolveSlot(); }
            ReadGamepad();
        }
        else
        {
            ReadKeyboard();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────

    public void SetMode(InputMode newMode)
    {
        mode = newMode;
        if (newMode == InputMode.Gamepad) { _rescanTimer = 0f; ResolveSlot(); }
    }

    public static int ConnectedGamepadCount()
    {
        int n = 0;
        foreach (var s in Input.GetJoystickNames())
            if (!string.IsNullOrWhiteSpace(s)) n++;
        return n;
    }

    public static bool IsGamepadConnected() => ConnectedGamepadCount() > 0;

    // ──────────────────────────────────────────────────────────
    //  Slot resolution
    // ──────────────────────────────────────────────────────────

    private void ResolveSlot()
    {
        string[] names = Input.GetJoystickNames();
        int rank = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i])) continue;
            if (rank == gamepadSlotRank)
            {
                _slot = i + 1;
                Debug.Log($"[InputProvider P{playerIndex}] slot={_slot} \"{names[i].Trim()}\"");
                return;
            }
            rank++;
        }
        _slot = gamepadSlotRank + 1;
    }

    // ──────────────────────────────────────────────────────────
    //  Gamepad reader
    // ──────────────────────────────────────────────────────────

    private void ReadGamepad()
    {
        // ── Steer: left stick X + D-pad X fallback ────────────
        float steer = Axis(axisSteer);
        if (Mathf.Abs(steer) < deadZone) steer = Axis(6);   // D-pad X
        Steer = DZ(steer);

        // ── Throttle: left stick Y + D-pad up/down ────────────
        // Stick Y is inverted on most pads (up = -1), so negate it.
        float throttle = Axis(axisThrottle);
        if (invertThrottle) throttle = -throttle;
        if (Mathf.Abs(throttle) < deadZone) throttle = -Axis(7);  // D-pad Y (also inverted)
        Throttle = DZ(throttle);

        // ── Brake: button 3 (Y / Triangle / num4) ─────────────
        Brake = Btn(btnBrake) ? 1f : 0f;

        // ── Drift: button 2 (X / Square / num3) ───────────────
        Drift = Btn(btnDrift);

        // ── Look back: R1 held ─────────────────────────────────
        LookBack = Btn(btnLookBack);

        // ── Menu toggle: L1 — one-shot on button DOWN ─────────
        MenuToggle = BtnDown(btnMenuToggle);
    }

    // ──────────────────────────────────────────────────────────
    //  Keyboard reader
    // ──────────────────────────────────────────────────────────

    private void ReadKeyboard()
    {
        Throttle = Mathf.Clamp(
            (Input.GetKey(keyForward) ? 1f : 0f) +
            (Input.GetKey(keyBack) ? -1f : 0f), -1f, 1f);
        Steer = Mathf.Clamp(
            (Input.GetKey(keyLeft) ? -1f : 0f) +
            (Input.GetKey(keyRight) ? 1f : 0f), -1f, 1f);
        Brake = Input.GetKey(keyBrake) ? 1f : 0f;
        Drift = Input.GetKey(keyDrift);
        LookBack = false;   // keyboard has no look-back by default
        // MenuToggle for keyboard is handled by InputControllerMenu directly (Tab key)
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private float Axis(int idx)
    {
        try { return Input.GetAxisRaw($"j{_slot}_axis{idx}"); }
        catch { return 0f; }
    }

    private float DZ(float v)
    {
        if (Mathf.Abs(v) < deadZone) return 0f;
        return Mathf.Sign(v) * (Mathf.Abs(v) - deadZone) / (1f - deadZone);
    }

    /// Button held
    private bool Btn(int btn)
        => Input.GetKey((KeyCode)(350 + (_slot - 1) * 20 + btn));

    /// Button down this frame only (one-shot)
    private bool BtnDown(int btn)
        => Input.GetKeyDown((KeyCode)(350 + (_slot - 1) * 20 + btn));
}