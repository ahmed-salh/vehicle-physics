using UnityEngine;

/// <summary>
/// VehicleInputProvider  v1
///
/// Decouples input source from VehicleController.
/// One instance is attached per car. It reads from whichever input mode
/// is currently selected (Keyboard, Gamepad, or Touch) and exposes
/// four normalised floats that VehicleController polls every Update.
///
/// INPUT MODES
/// ───────────
/// Keyboard  : WASD / Arrow keys + LShift (brake) + Space (drift)
///             Player 2 uses IJKL + RShift + RCtrl by default
/// Gamepad   : Left-stick steer/throttle, LT brake, RB drift
///             Player index maps to Unity's joystick number
/// Touch     : Virtual on-screen buttons managed by TouchInputOverlay
///             (created automatically when mode = Touch)
/// </summary>
public class VehicleInputProvider : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  Public enum
    // ──────────────────────────────────────────────────────────

    public enum InputMode { Keyboard, Gamepad, Touch }

    // ──────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("0 = Player 1, 1 = Player 2. Controls default key sets and gamepad index.")]
    public int playerIndex = 0;

    [Header("Active Mode")]
    public InputMode mode = InputMode.Keyboard;

    [Header("Keyboard Bindings  (Player 1 defaults)")]
    public KeyCode keyForward = KeyCode.W;
    public KeyCode keyBack = KeyCode.S;
    public KeyCode keyLeft = KeyCode.A;
    public KeyCode keyRight = KeyCode.D;
    public KeyCode keyBrake = KeyCode.LeftShift;
    public KeyCode keyDrift = KeyCode.Space;

    [Header("Gamepad")]
    [Tooltip("'Joystick1' for player 1, 'Joystick2' for player 2, etc.")]
    public string gamepadName = "Joystick1";

    // ──────────────────────────────────────────────────────────
    //  Output values  (read by VehicleController)
    // ──────────────────────────────────────────────────────────

    public float Throttle { get; private set; }   // -1..1  (negative = reverse)
    public float Steer { get; private set; }   // -1..1
    public float Brake { get; private set; }   //  0..1
    public bool Drift { get; private set; }

    // ──────────────────────────────────────────────────────────
    //  Touch state (written by TouchInputOverlay)
    // ──────────────────────────────────────────────────────────

    [HideInInspector] public float touchThrottle;
    [HideInInspector] public float touchSteer;
    [HideInInspector] public float touchBrake;
    [HideInInspector] public bool touchDrift;

    // ──────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────

    private TouchInputOverlay _touchOverlay;

    // ──────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────

    private void Awake()
    {
        // Apply player-2 default key bindings automatically
        if (playerIndex == 1 && keyForward == KeyCode.W)
        {
            keyForward = KeyCode.I;
            keyBack = KeyCode.K;
            keyLeft = KeyCode.J;
            keyRight = KeyCode.L;
            keyBrake = KeyCode.RightShift;
            keyDrift = KeyCode.RightControl;
            gamepadName = "Joystick2";
        }
    }

    private void Update()
    {
        switch (mode)
        {
            case InputMode.Keyboard: ReadKeyboard(); break;
            case InputMode.Gamepad: ReadGamepad(); break;
            case InputMode.Touch: ReadTouch(); break;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Mode switching  (called from InputControllerMenu)
    // ──────────────────────────────────────────────────────────

    public void SetMode(InputMode newMode)
    {
        mode = newMode;

        // Create / destroy the touch overlay as needed
        if (mode == InputMode.Touch)
        {
            if (_touchOverlay == null)
                _touchOverlay = TouchInputOverlay.CreateFor(this);
        }
        else
        {
            if (_touchOverlay != null)
            {
                Destroy(_touchOverlay.gameObject);
                _touchOverlay = null;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Readers
    // ──────────────────────────────────────────────────────────

    private void ReadKeyboard()
    {
        float fwd = Input.GetKey(keyForward) ? 1f : 0f;
        float back = Input.GetKey(keyBack) ? -1f : 0f;
        float left = Input.GetKey(keyLeft) ? -1f : 0f;
        float right = Input.GetKey(keyRight) ? 1f : 0f;

        Throttle = Mathf.Clamp(fwd + back, -1f, 1f);
        Steer = Mathf.Clamp(left + right, -1f, 1f);
        Brake = Input.GetKey(keyBrake) ? 1f : 0f;
        Drift = Input.GetKey(keyDrift);
    }

    private void ReadGamepad()
    {
        // Unity's legacy Input system names gamepad axes like
        // "Joystick1 Axis 1", "Joystick1 Axis 2", etc.
        // Left-stick X = Axis 1, Left-stick Y = Axis 2 (inverted on many pads)
        // Left-trigger  = Axis 9, Right-bumper = Joystick button 5 (XInput mapping)

        string prefix = gamepadName + " ";

        float steerRaw = GetAxis(prefix + "Axis 1");
        float throttleRaw = -GetAxis(prefix + "Axis 2");  // up = forward on most pads
        float triggerLeft = Mathf.Clamp01(GetAxis(prefix + "Axis 9"));

        Throttle = throttleRaw;
        Steer = steerRaw;
        Brake = triggerLeft;
        Drift = Input.GetKey(GamepadButton(5));   // RB
    }

    private void ReadTouch()
    {
        Throttle = touchThrottle;
        Steer = touchSteer;
        Brake = touchBrake;
        Drift = touchDrift;
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static float GetAxis(string name)
    {
        try { return Input.GetAxis(name); }
        catch { return 0f; }
    }

    /// Returns the KeyCode for button N on gamepadName
    private KeyCode GamepadButton(int btn)
    {
        // KeyCode.Joystick1Button0 = 350, each joystick is +20 buttons apart
        int joystickOffset = playerIndex * 20;
        return (KeyCode)(350 + joystickOffset + btn);
    }
}