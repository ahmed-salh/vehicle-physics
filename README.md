<div align="center">

# Vehicle Physics — Unity

**A from-scratch raycast vehicle physics system built as a personal research project.**  
*Ackermann steering · Lateral force modelling · Drift mechanics · Split-screen · Procedural world*

---

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Unity](https://img.shields.io/badge/Unity-2022%2B-black.svg?logo=unity)
![Language](https://img.shields.io/badge/language-C%23-purple.svg)
![Status](https://img.shields.io/badge/status-active%20development-brightgreen.svg)

</div>

---

## Showcase

<table>
<tr>
<td align="center" width="50%">

### Drifting & Skid Marks

![Drifting demo](Docs/Vehicle_Types.gif)

*Rear grip reduction on drift key · LineRenderer tyre marks · Fade-out over time*

</td>
<td align="center" width="50%">

### Speedometer HUD

![Ackermann steering demo](Docs/Drifting.gif)

*Inner wheel tighter radius · Speed-sensitive angle reduction · Smooth lerp per wheel*

</td>
</tr>
<tr>
<td align="center" width="50%">

### Split-Screen Multiplayer

![Split screen demo](Docs/split_screen.gif)

*Auto-activates on gamepad connect · Vertical split · Independent cameras & HUDs*

</td>
<td align="center" width="50%">

### Suspension & Wheel Spin

![Suspension demo](Docs/Suspensions.gif)

*Per-wheel raycast suspension · Brake lock-up visual · Left/right wheel flip preserved*

</td>
</tr>
</table>

---

## Physics Overview

This project implements a **full raycast-based vehicle physics stack** from first principles — no Unity WheelCollider, no third-party physics asset.

### Suspension Model

Each wheel fires a downward ray from its rest position. Spring force is proportional to compression; damping uses `GetPointVelocity` projected onto the suspension axis (eliminating the frame-0 compression-delta spike that launches cars off the ground on the first physics step).

```
F_spring = compression × k_spring
F_damper = −(v_contact · ŷ_suspension) × k_damper
F_total  = hit.normal × (F_spring + F_damper)
```

### Lateral Force Model

The lateral correction is an **impulse-cancellation model** rather than a Pacejka slip-angle curve. Each grounded wheel computes the exact impulse needed to zero its lateral velocity in the current timestep, then clamps it to the physical grip limit:

```
desiredForce = (−lateralSpeed × mass) / (groundedWheels × fixedDeltaTime)
lateralForce = Clamp(desiredForce, −maxGrip, +maxGrip)
```

This approach is numerically stable at all speeds, avoids the `atan2` blowup at zero velocity, and requires no magic threshold to prevent stationary ice-slide.

### Drift Mechanics

Drift is **deliberately gated on explicit player input** rather than automatic slip-angle detection. This decouples grip reduction from the `isDrifting` visual flag, breaking the positive-feedback spin loop:

```
Grip reduction  →  only when drift key is held (_driftInput)
Skid marks / VFX  →  whenever |bodySlipAngle| > threshold  OR  drift key held
```

This means a car can produce skid marks through a fast corner without losing grip — exactly like real tyres operating near (but not over) the limit.

### Stability Control (Yaw Damping)

A counter-torque opposes angular velocity around the car's world-up axis, scaling with forward speed:

```
counterTorque = −yawRate × yawDampingTorque × speedFactor × driftFactor
```

`driftFactor` drops to 15 % when the drift key is held, so the player can still initiate and hold a controlled slide. This replaces magic steer-angle clamping with something physically motivated.

### Ackermann Steering Geometry
<img width="480" height="400" alt="A-front-wheel-steering-vehicle-and-steer-angles-of-the-inner-and-outer-wheels" src="https://github.com/user-attachments/assets/0b09fee2-5267-4c82-b44c-0ff21f8972f4" />

The inner and outer front wheels steer at different angles computed from the turn radius:

```
R      = wheelbase / tan(baseAngle)
innerR = R − trackWidth/2
outerR = R + trackWidth/2

innerDeg = atan(wheelbase / innerR)
outerDeg = atan(wheelbase / outerR)
```

At low steering angles the difference is small; at full lock the inner wheel turns noticeably tighter — visible in the GIF above.

---

## Vehicle Presets

| Preset | Spring | Damper | Lateral Grip | Drive Force | Character |
|---|---|---|---|---|---|
| **Road** | 38 000 N/m | 4 000 N·s/m | 18 000 N | 9 000 N | Grippy, stable, quick |
| **Off-Road** | 22 000 N/m | 2 500 N·s/m | 12 000 N | 7 500 N | Floaty, loose, long travel |
| **Custom** | — | — | — | — | Full inspector control |

---

## Input System

### Modes

| Mode | Description |
|---|---|
| **Keyboard** | WASD + LShift (brake) + Space (drift) |
| **Gamepad** | Left stick steer/throttle · B/Circle brake · A/Cross drift |

### Auto Split-Screen

The game **starts in single-player**. The moment a gamepad is detected via `Input.GetJoystickNames()`, a second car spawns, the screen splits vertically, and P2 gets the gamepad. Unplugging the gamepad reverts to single-player immediately.

```
Gamepad connected  →  EnableSplitScreen()
Gamepad removed    →  DisableSplitScreen()
```

### Key Bindings

| Action | Player 1 | Player 2 |
|---|---|---|
| Throttle / Reverse | W / S | I / K |
| Steer | A / D | J / L |
| Brake | Left Shift | Right Shift |
| Drift | Space | Right Ctrl |
| Input Menu | Tab | Tab |

---

## Project Structure

```
Assets/
├── Scripts/
│   ├── Physics/
│   │   └── VehicleController.cs          # Core physics — suspension, traction, steering
│   ├── Input/
│   │   ├── VehicleInputProvider.cs       # Keyboard / Gamepad abstraction
│   │   ├── InputControllerMenu.cs        # In-game input mode switcher (Tab)
│   │   └── GamepadAxisRegistrar.cs       # Registers joystick axes in InputManager at runtime
│   ├── Effects/
│   │   └── WheelSkidEffect.cs            # LineRenderer skid marks, pooled segments, fade-out
│   ├── HUD/
│   │   └── SpeedometerHUD.cs             # GL-rendered analogue gauge, viewport-aware
│   └── World/
│       ├── ProceduralGroundGenerator.cs  # Endless chunk-pool terrain, multi-target
│       └── SplitScreenManager.cs         # Camera split, car spawn, auto P2 detection
└── Docs/
    └── gifs/                             # ← Upload your GIFs here
```

---

## Features at a Glance

- **Raycast suspension** — spring + velocity-based damper, no WheelCollider
- **Impulse-cancellation lateral model** — stable from 0 km/h to top speed
- **Ackermann geometry** — mathematically correct inner/outer wheel angles
- **Explicit drift gate** — grip only drops when the player asks for it
- **Yaw damping** — speed-scaled counter-torque, respects drift input
- **Brake lock-up** — driven wheels freeze visually under hard braking
- **Skid marks** — per-wheel LineRenderer pool, fade out and are destroyed over time
- **GL vector speedometer** — hardware-accelerated, no pixelation, split-screen aware
- **Auto split-screen** — vertical split activates/deactivates on gamepad connect
- **Procedural endless world** — chunk pool follows all cars simultaneously, MeshCollider per tile
- **Universal HID gamepad support** — axis names registered at runtime, works with any DirectInput pad

---

## Setup

### Requirements

- Unity **2022.3 LTS** or newer
- No additional packages required (all rendering uses Unity's built-in GL and legacy Input system)

### Quick Start

1. Clone or download the repository
2. Open in Unity 2022.3+
3. Open `Assets/Scenes/Main.unity`
4. Press **Play** — single-player starts immediately with keyboard (WASD)
5. Plug in a gamepad — split-screen activates automatically

### Gamepad Axis Registration

The first time you open the project, **`GamepadAxisRegistrar`** (attached to the GameManager prefab) writes joystick axis entries into `ProjectSettings/InputManager.asset` via an `[InitializeOnLoad]` Editor hook. You'll see log messages confirming registration. This is a one-time step that persists in the project.

---

##  Technical Notes

### Why No WheelCollider?

Unity's `WheelCollider` is a black box — you can't inspect or modify the slip models, the suspension integration, or the force application points. A raycast wheel exposes everything: you can tune the spring curve, swap the lateral model, add per-axle weight transfer, or implement a full Pacejka tyre model — all in readable C# with no hidden state.

### Why Impulse Cancellation Instead of Pacejka?

The Pacejka "Magic Formula" produces realistic slip-angle curves but requires careful numerical integration and a well-tuned peak slip angle per vehicle. The impulse-cancellation model gives identical *feel* for arcade/semi-sim games without the tuning overhead: at zero lateral slip it applies zero force; at maximum grip it applies the exact force needed to correct the slip. The result is a car that sticks to the road naturally without any threshold magic.

### Wheel Rotation — the Gimbal Problem

Steerable front wheels have two rotation axes: **Y** (steer) and **X** (spin). If you read `localEulerAngles.x` back from a quaternion that has a non-zero Y, Unity's Euler decomposition picks one of infinitely many valid solutions — almost never the spin angle you stored. This project solves it by:
- Accumulating spin in a `_wheelSpinAngle[]` float array (never reading it back from a Transform)
- Composing `baseRotation × Euler(0, steer, 0)` on the steer pivot
- Composing `baseRotation × Euler(spin, 0, 0)` on the spin pivot (a child transform)
- Negating spin for left-side wheels to correct for their 180° Y flip

---

## License

MIT — see [LICENSE](LICENSE) for details.  
Free to use, modify, and distribute. Attribution appreciated but not required.

### See the full showcase on my YouTube channel

[![Video Title](https://img.youtube.com/vi/lB94Q1YQLDs/0.jpg)](https://www.youtube.com/watch?v=lB94Q1YQLDs)
