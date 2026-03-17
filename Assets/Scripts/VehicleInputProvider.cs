using UnityEngine;

/// <summary>
/// VehicleInputProvider  v5
///
/// Key fix: joystick SLOT is now discovered dynamically.
/// Input.GetJoystickNames() returns an array where index 0 = slot 1,
/// index 1 = slot 2, etc.  An empty string means no pad in that slot.
/// We scan the array to find the Nth occupied slot for this player
/// (N = gamepadSlotRank, default 0 = first connected pad).
///
/// This means it doesn't matter whether the pad is physically on
/// USB port 1 or 2 — the code finds it.
///
/// Twin USB Gamepad / generic DirectInput HID axis layout:
///   axis 0 = left stick X   → steer       (-1 left … +1 right)
///   axis 1 = left stick Y   → throttle    (-1 up   … +1 down, inverted)
///   axis 6 = D-pad X        → steer fallback
///   axis 7 = D-pad Y        → throttle fallback (inverted)
///   button 0 = A/Cross      → drift
///   button 1 = B/Circle     → brake
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

    [Header("Gamepad")]
    [Tooltip("0 = first connected gamepad, 1 = second connected gamepad.\n" +
             "This is NOT the USB port number — it's the rank among connected pads.")]
    public int gamepadSlotRank = 0;

    [Tooltip("Axis index for steering (left stick X). Default 0.")]
    public int axisSteer = 0;
    [Tooltip("Axis index for throttle (left stick Y). Default 1.")]
    public int axisThrottle = 1;
    [Tooltip("Invert throttle axis (enable for DirectInput / Twin USB pads).")]
    public bool invertThrottle = true;
    [Tooltip("Analogue brake axis index. -1 = use button instead.")]
    public int axisBrake = -1;
    [Range(0f, 0.4f)]
    public float deadZone = 0.15f;

    [Header("Gamepad Buttons (0-based)")]
    public int btnDrift = 0;   // A / Cross
    public int btnBrake = 1;   // B / Circle

    // ──────────────────────────────────────────────────────────
    //  Output
    // ──────────────────────────────────────────────────────────

    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public float Brake { get; private set; }
    public bool Drift { get; private set; }

    // Resolved at runtime: "joystick N" where N is the actual Unity slot (1-based)
    private int _resolvedSlot = -1;   // -1 = not yet found
    private string _joyPrefix = "";
    private float _slotCheckTimer = 0f;

    // ──────────────────────────────────────────────────────────
    //  Awake / lifecycle
    // ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (playerIndex == 1 && keyForward == KeyCode.W)
        {
            keyForward = KeyCode.I;
            keyBack = KeyCode.K;
            keyLeft = KeyCode.J;
            keyRight = KeyCode.L;
            keyBrake = KeyCode.RightShift;
            keyDrift = KeyCode.RightControl;
        }
    }

    private void Update()
    {
        if (mode == InputMode.Gamepad)
        {
            // Re-scan slot every 2 seconds in case pad reconnects
            _slotCheckTimer -= Time.deltaTime;
            if (_slotCheckTimer <= 0f)
            {
                _slotCheckTimer = 2f;
                ResolveSlot();
            }

            if (_resolvedSlot > 0)
                ReadGamepad();
            // If slot not found yet, outputs stay at 0 (safe)
        }
        else
        {
            ReadKeyboard();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Slot resolution
    // ──────────────────────────────────────────────────────────

    /// Scan GetJoystickNames() and pick the Nth occupied slot (0-based rank).
    private void ResolveSlot()
    {
        string[] names = Input.GetJoystickNames();
        int rank = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i])) continue;

            if (rank == gamepadSlotRank)
            {
                int slot = i + 1;   // Unity slots are 1-based
                if (slot != _resolvedSlot)
                {
                    _resolvedSlot = slot;
                    _joyPrefix = $"joystick {slot}";
                    Debug.Log($"[VehicleInputProvider P{playerIndex}] " +
                              $"Gamepad rank {gamepadSlotRank} → slot {slot} " +
                              $"(\"{names[i].Trim()}\")  prefix=\"{_joyPrefix}\"");
                }
                return;
            }
            rank++;
        }

        // No pad found at this rank
        if (_resolvedSlot != -1)
        {
            Debug.LogWarning($"[VehicleInputProvider P{playerIndex}] " +
                             $"Gamepad rank {gamepadSlotRank} disconnected.");
            _resolvedSlot = -1;
            _joyPrefix = "";
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────

    public void SetMode(InputMode newMode)
    {
        mode = newMode;
        if (newMode == InputMode.Gamepad)
        {
            _slotCheckTimer = 0f;   // force immediate scan
            _resolvedSlot = -1;
        }
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
    //  Gamepad reader
    // ──────────────────────────────────────────────────────────

    private void ReadGamepad()
    {
        // ── Steering ───────────────────────────────────────────
        float steerRaw = RawAxis(axisSteer);
        if (Mathf.Abs(steerRaw) <= deadZone)
            steerRaw = RawAxis(6);              // D-pad X fallback
        Steer = DZ(steerRaw);

        // ── Throttle ───────────────────────────────────────────
        float throttleRaw = RawAxis(axisThrottle);
        if (invertThrottle) throttleRaw = -throttleRaw;
        if (Mathf.Abs(throttleRaw) <= deadZone)
            throttleRaw = -RawAxis(7);          // D-pad Y fallback (inverted)
        Throttle = DZ(throttleRaw);

        // ── Brake ──────────────────────────────────────────────
        if (axisBrake >= 0)
        {
            float raw = RawAxis(axisBrake);
            // Handle triggers that rest at -1 (DirectInput) or 0 (XInput)
            Brake = raw < -0.5f
                ? Mathf.Clamp01(raw + 1f)
                : Mathf.Clamp01(raw);
        }
        else
        {
            Brake = IsJoyBtn(btnBrake) ? 1f : 0f;
        }

        // ── Drift ──────────────────────────────────────────────
        Drift = IsJoyBtn(btnDrift);
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
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private float RawAxis(int idx)
    {
        if (string.IsNullOrEmpty(_joyPrefix)) return 0f;
        try { return Input.GetAxisRaw($"{_joyPrefix} axis {idx}"); }
        catch { return 0f; }
    }

    private float DZ(float v)
    {
        if (Mathf.Abs(v) < deadZone) return 0f;
        return Mathf.Sign(v) * (Mathf.Abs(v) - deadZone) / (1f - deadZone);
    }

    // Slot-aware button check: uses the resolved slot, not playerIndex arithmetic
    private bool IsJoyBtn(int btn)
    {
        if (_resolvedSlot < 1) return false;
        // KeyCode.Joystick1Button0 = 350, each slot adds 20
        return Input.GetKey((KeyCode)(350 + (_resolvedSlot - 1) * 20 + btn));
    }
}