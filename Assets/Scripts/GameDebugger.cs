using UnityEngine;
using System.Text;

/// <summary>
/// Attach to any GO. Press Play, then squeeze R2 and L2 fully.
/// Look for axes that change from their resting value — those are your triggers.
/// The Console prints every 0.5 seconds so it doesn't spam.
/// </summary>
public class GamepadDebugger : MonoBehaviour
{
    public int joystickSlot = 1;

    private float _timer;
    private float[] _restValues = new float[12];
    private bool _calibrated = false;
    private float _calibTimer = 1.5f; // wait 1.5s before recording rest

    private void Update()
    {
        // Calibrate rest values first (don't touch any buttons/sticks during this)
        if (!_calibrated)
        {
            _calibTimer -= Time.deltaTime;
            if (_calibTimer <= 0f)
            {
                for (int a = 0; a < 12; a++)
                    _restValues[a] = ReadAxis(a);
                _calibrated = true;
                Debug.Log("[GamepadDebugger] Rest values calibrated. Now press R2 and L2.");
            }
            return;
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = 0.3f;

        var sb = new StringBuilder();
        sb.AppendLine($"=== Slot {joystickSlot} — axes that DIFFER from rest ===");

        bool anyActive = false;
        for (int a = 0; a < 12; a++)
        {
            float v = ReadAxis(a);
            float diff = v - _restValues[a];
            if (Mathf.Abs(diff) > 0.05f)
            {
                sb.AppendLine($"  axis {a,2}  now={v:F3}  rest={_restValues[a]:F3}  delta={diff:+F3;-F3}  ← ACTIVE");
                anyActive = true;
            }
        }

        if (!anyActive) sb.AppendLine("  (no axis moving — press R2 or L2)");

        // Also show buttons
        sb.Append("  buttons: ");
        for (int b = 0; b < 20; b++)
        {
            if (Input.GetKey((KeyCode)(350 + (joystickSlot - 1) * 20 + b)))
                sb.Append($"[{b}] ");
        }

        Debug.Log(sb.ToString());
    }

    private float ReadAxis(int a)
    {
        try { return Input.GetAxisRaw($"j{joystickSlot}_axis{a}"); }
        catch { return 0f; }
    }
}