/// <summary>
/// GamepadAxisRegistrar
///
/// THIS IS THE MISSING PIECE.
///
/// Input.GetAxisRaw("joystick 1 axis 0") throws a silent exception and
/// returns 0 unless that exact string exists in Input Manager.
/// Unity ships with NONE of these registered by default.
///
/// This script writes them into ProjectSettings/InputManager.asset via
/// the SerializedObject API — a one-time operation that persists in the
/// project permanently. It runs both on Editor load (InitializeOnLoad)
/// and at runtime Awake so Play mode always has the axes.
///
/// Axes registered: j1_axis0 … j2_axis19  (40 total)
/// These names are what VehicleInputProvider reads.
/// </summary>
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;

public class GamepadAxisRegistrar : MonoBehaviour
{
    private void Awake()
    {
#if UNITY_EDITOR
        RegisterViaEditor();
#endif
        VerifyAxes();
    }

#if UNITY_EDITOR
    // Runs automatically when the project loads in the Editor —
    // before you press Play, so axes are ready immediately.
    [InitializeOnLoadMethod]
    private static void OnEditorLoad() => RegisterViaEditor();

    public static void RegisterViaEditor()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
        if (assets == null || assets.Length == 0) return;

        var obj = new SerializedObject(assets[0]);
        var axesArr = obj.FindProperty("m_Axes");

        var existing = new HashSet<string>();
        for (int i = 0; i < axesArr.arraySize; i++)
            existing.Add(axesArr.GetArrayElementAtIndex(i)
                                .FindPropertyRelative("m_Name").stringValue);

        bool dirty = false;
        for (int slot = 1; slot <= 2; slot++)
        {
            for (int axis = 0; axis <= 19; axis++)
            {
                string name = $"j{slot}_axis{axis}";
                if (existing.Contains(name)) continue;

                axesArr.InsertArrayElementAtIndex(axesArr.arraySize);
                var el = axesArr.GetArrayElementAtIndex(axesArr.arraySize - 1);

                el.FindPropertyRelative("m_Name").stringValue = name;
                el.FindPropertyRelative("descriptiveName").stringValue = "";
                el.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
                el.FindPropertyRelative("negativeButton").stringValue = "";
                el.FindPropertyRelative("positiveButton").stringValue = "";
                el.FindPropertyRelative("altNegativeButton").stringValue = "";
                el.FindPropertyRelative("altPositiveButton").stringValue = "";
                el.FindPropertyRelative("gravity").floatValue = 0f;
                el.FindPropertyRelative("dead").floatValue = 0.001f; // tiny dead-zone so raw values pass through
                el.FindPropertyRelative("sensitivity").floatValue = 1f;
                el.FindPropertyRelative("snap").boolValue = false;
                el.FindPropertyRelative("invert").boolValue = false;
                el.FindPropertyRelative("type").intValue = 2;    // Joystick Axis
                el.FindPropertyRelative("axis").intValue = axis; // 0-based in InputManager
                el.FindPropertyRelative("joyNum").intValue = slot; // 1 = joystick 1, 2 = joystick 2

                existing.Add(name);
                dirty = true;
            }
        }

        if (dirty)
        {
            obj.ApplyModifiedProperties();
            Debug.Log("[GamepadAxisRegistrar] Registered j1_axis0…j2_axis19 in InputManager. " +
                      "Axes are now available for GetAxisRaw.");
        }
    }
#endif

    private static void VerifyAxes()
    {
        int ok = 0, fail = 0;
        for (int slot = 1; slot <= 2; slot++)
            for (int axis = 0; axis <= 5; axis++)
            {
                try { Input.GetAxisRaw($"j{slot}_axis{axis}"); ok++; }
                catch { fail++; }
            }

        if (fail > 0)
            Debug.LogError($"[GamepadAxisRegistrar] {fail} axes not found in InputManager! " +
                           "Make sure GamepadAxisRegistrar is in the scene and ran in the Editor before building.");
        else
            Debug.Log($"[GamepadAxisRegistrar] All {ok} axes verified OK.");
    }
}