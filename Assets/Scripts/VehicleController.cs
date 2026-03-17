using UnityEngine;

/// <summary>
/// Raycast-based Vehicle Controller  v5.1
///
/// Bug fixed from v5:
///   The WheelSkidEffect GO was parented to wheelTransform AFTER the spin-pivot
///   was cached, so at runtime GetChild(0) returned the SkidEffect GO (invisible)
///   instead of the wheel mesh. Front wheels appeared frozen because spin was
///   applied to the wrong transform.
///
/// Fix:
///   • _spinPivots[] caches the actual spin-pivot Transform at Awake time
///     (before any child GOs are appended).
///   • Skid effects are now parented to rayOrigin, not wheelTransform, so
///     they never appear in the wheel mesh's child list.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────
    //  ENUMS & NESTED TYPES
    // ──────────────────────────────────────────────────────────

    public enum VehiclePreset { Road, OffRoad, Custom }

    [System.Serializable]
    public class WheelConfig
    {
        [Header("Transforms")]
        [Tooltip("Steer pivot transform (parent of the spinning mesh). " +
                 "For non-steerable wheels this IS the spinning mesh.")]
        public Transform wheelTransform;
        [Tooltip("Empty GameObject at the top of suspension travel, Y-axis pointing UP.")]
        public Transform rayOrigin;

        [Header("Role")]
        public bool isSteerable = false;
        public bool isDriven = false;

        [Header("Ackermann")]
        [Tooltip("Is this wheel on the left side of the car?")]
        public bool isLeftSide = false;

        [Header("Per-Wheel Suspension Override  (0 = use global)")]
        [Range(0f, 1f)] public float springOverride = 0f;
        [Range(0f, 1f)] public float damperOverride = 0f;
    }

    [System.Serializable]
    public class SuspensionSettings
    {
        public float restLength = 0.35f;
        public float springStiffness = 35000f;
        public float damperStiffness = 3500f;
        public float wheelRadius = 0.35f;
    }

    [System.Serializable]
    public class TractionSettings
    {
        public float lateralGripMax = 15000f;
        public float peakSlipAngle = 8f;
        [Range(0f, 1f)] public float gripFalloff = 0.75f;
        public float tractionForce = 3500f;
        public float engineBraking = 800f;
        public float brakeForce = 6000f;
        public float stillSpeedThreshold = 0.4f;
    }

    [System.Serializable]
    public class DriftSettings
    {
        public bool driftEnabled = true;
        public KeyCode driftKey = KeyCode.Space;
        [Range(0f, 1f)] public float driftGripMultiplier = 0.30f;
        public float driftEntrySpeed = 4f;
        [Range(0f, 1f)] public float counterSteerAssist = 0.3f;
        public float driftAngleThreshold = 15f;
    }

    [System.Serializable]
    public class SteeringSettings
    {
        public float maxSteerAngle = 32f;
        public float steerSpeedReduction = 15f;
        public float steerLerpSpeed = 180f;

        [Header("Ackermann Geometry")]
        public float wheelbase = 2.6f;
        public float trackWidth = 1.5f;
    }

    [System.Serializable]
    public class EngineSettings
    {
        public AnimationCurve torqueCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public float maxSpeed = 28f;
    }

    [System.Serializable]
    public class WheelSpinSettings
    {
        public float brakeSpinDecay = 0.25f;
        public float drivenLockDecay = 0.08f;
        public float lateralSkidThreshold = 1.2f;
        [Range(0f, 1f)] public float lockUpThreshold = 0.85f;
    }

    [System.Serializable]
    public class StabilitySettings
    {
        public float yawDampingTorque = 4500f;
        public float yawDampingSpeedFull = 12f;
        [Range(0f, 1f)] public float yawDampingDriftFraction = 0.15f;
    }

    public struct WheelState
    {
        public bool isGrounded;
        public bool isSkidding;
        public float compression;
        public float spinDeg;
    }

    // ──────────────────────────────────────────────────────────
    //  INSPECTOR FIELDS
    // ──────────────────────────────────────────────────────────

    [Header("Vehicle Preset")]
    public VehiclePreset preset = VehiclePreset.Road;

    [Header("Wheels")]
    public WheelConfig[] wheels;

    [Header("Suspension")]
    public SuspensionSettings suspension = new SuspensionSettings();

    [Header("Traction")]
    public TractionSettings traction = new TractionSettings();
    public float markWidth;


    [Header("Drift")]
    public DriftSettings drift = new DriftSettings();

    [Header("Steering")]
    public SteeringSettings steering = new SteeringSettings();

    [Header("Engine")]
    public EngineSettings engine = new EngineSettings();

    [Header("Wheel Spin")]
    public WheelSpinSettings wheelSpin = new WheelSpinSettings();

    [Header("Stability")]
    public StabilitySettings stability = new StabilitySettings();

    [Header("Centre of Mass Offset")]
    public Vector3 centreOfMassOffset = new Vector3(0f, -0.35f, 0f);

    [Header("Debug")]
    public bool drawGizmos = true;

    // ──────────────────────────────────────────────────────────
    //  RUNTIME STATE
    // ──────────────────────────────────────────────────────────

    private Rigidbody _rb;
    private VehicleInputProvider _inputProvider;

    private float _steerInput;
    private float _throttleInput;
    private float _brakeInput;
    private bool _driftInput;
    private bool _isDrifting;
    private float _driftAngle;

    private bool[] _wheelGrounded;
    private float[] _wheelCompression;
    private float[] _currentSteerAngle;
    private float[] _wheelSpinSpeed;
    private float[] _wheelSpinAngle;
    private bool[] _wheelSkidding;
    private Quaternion[] _steerPivotBaseRot;
    private Quaternion[] _spinPivotBaseRot;

    // ─────────────────────────────────────────────────────────
    //  Cached spin-pivot references.
    //
    //  WHY THIS EXISTS:
    //  For steerable wheels the spin pivot is child[0] of wheelTransform.
    //  In Awake we also add a WheelSkidEffect GO as a child of rayOrigin
    //  (moved from wheelTransform to keep the wheel mesh hierarchy clean).
    //  By storing the Transform reference here at Awake — BEFORE any
    //  child GOs are appended — we are immune to GetChild(0) returning
    //  the wrong object later.
    // ─────────────────────────────────────────────────────────
    private Transform[] _spinPivots;

    private Vector3[] _contactPoints;
    private WheelSkidEffect[] _skidEffects;

    public WheelState[] WheelStates { get; private set; }

    // Public read-only for SpeedometerHUD
    public float SpeedMPS => _rb != null ? _rb.linearVelocity.magnitude : 0f;
    public float SpeedKPH => SpeedMPS * 3.6f;
    public bool IsDrifting => _isDrifting;
    public bool IsBraking => _brakeInput > 0.01f;

    // ──────────────────────────────────────────────────────────
    //  PRESETS
    // ──────────────────────────────────────────────────────────

    private void ApplyPreset()
    {
        switch (preset)
        {
            case VehiclePreset.Road:
                suspension.springStiffness = 38000f;
                suspension.damperStiffness = 4000f;
                suspension.restLength = 0.30f;
                traction.lateralGripMax = 6000f;
                traction.peakSlipAngle = 7f;
                traction.gripFalloff = 0.80f;
                traction.tractionForce = 9000f;
                break;

            case VehiclePreset.OffRoad:
                suspension.springStiffness = 22000f;
                suspension.damperStiffness = 2500f;
                suspension.restLength = 0.50f;
                suspension.wheelRadius = 0.40f;
                traction.lateralGripMax = 4000f;
                traction.peakSlipAngle = 14f;
                traction.gripFalloff = 0.55f;
                traction.tractionForce = 7500f;
                break;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.centerOfMass = centreOfMassOffset;
        _inputProvider = GetComponent<VehicleInputProvider>();

        ApplyPreset();

        int n = wheels.Length;
        _wheelGrounded = new bool[n];
        _wheelCompression = new float[n];
        _currentSteerAngle = new float[n];
        _wheelSpinSpeed = new float[n];
        _wheelSpinAngle = new float[n];
        _wheelSkidding = new bool[n];
        _contactPoints = new Vector3[n];
        WheelStates = new WheelState[n];
        _steerPivotBaseRot = new Quaternion[n];
        _spinPivotBaseRot = new Quaternion[n];
        _spinPivots = new Transform[n];

        for (int i = 0; i < n; i++)
            _wheelCompression[i] = 0.5f;

        // ── Step 1: Cache transforms & base rotations BEFORE any child GOs
        //   are added. This is the critical ordering fix.
        // ────────────────────────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            WheelConfig w = wheels[i];

            // Steer pivot base (wheelTransform itself for steerable wheels)
            _steerPivotBaseRot[i] = w.wheelTransform != null
                ? w.wheelTransform.localRotation : Quaternion.identity;

            // Spin pivot: child[0] of wheelTransform for steerable wheels,
            // or wheelTransform itself for non-steerable wheels.
            // Resolved NOW — before skid-effect children are appended below.
            Transform sp = ResolveSpinPivot(w);
            _spinPivots[i] = sp;
            _spinPivotBaseRot[i] = sp != null ? sp.localRotation : Quaternion.identity;
        }

        // ── Step 2: Attach skid effects to rayOrigin (NOT wheelTransform).
        //   Parenting to rayOrigin keeps the wheel mesh hierarchy pristine and
        //   avoids any future GetChild(0) confusion.
        // ────────────────────────────────────────────────────────────────
        _skidEffects = new WheelSkidEffect[n];
        for (int i = 0; i < n; i++)
        {
            WheelConfig w = wheels[i];
            // Prefer rayOrigin as parent; fall back to wheelTransform if no rayOrigin
            Transform effectParent = w.rayOrigin != null ? w.rayOrigin
                                   : w.wheelTransform;
            if (effectParent == null) continue;

            _skidEffects[i] = effectParent.GetComponentInChildren<WheelSkidEffect>();
            if (_skidEffects[i] == null)
            {
                var go = new GameObject($"SkidEffect_W{i}");
                go.transform.SetParent(effectParent, false);
                go.AddComponent<LineRenderer>();
                _skidEffects[i] = go.AddComponent<WheelSkidEffect>();
                _skidEffects[i].markWidth = markWidth;
                
            }
        }
    }

    private void Update()
    {
        GatherInput();
        UpdateDriftState();
    }

    private void FixedUpdate()
    {
        for (int i = 0; i < wheels.Length; i++)
            ProcessWheel(i);

        ApplyYawDamping();
    }

    private void LateUpdate()
    {
        UpdateSteering();
        UpdateWheelVisuals();
    }

    // ──────────────────────────────────────────────────────────
    //  INPUT
    // ──────────────────────────────────────────────────────────

    private void GatherInput()
    {
        if (_inputProvider != null)
        {
            // Delegate entirely to whichever input mode the player chose
            _throttleInput = _inputProvider.Throttle;
            _steerInput = _inputProvider.Steer;
            _brakeInput = _inputProvider.Brake;
            _driftInput = drift.driftEnabled && _inputProvider.Drift;
        }
        else
        {
            // Fallback: direct keyboard (no provider attached)
            _throttleInput = Input.GetAxis("Vertical");
            _steerInput = Input.GetAxis("Horizontal");
            _brakeInput = Input.GetKey(KeyCode.LeftShift) ? 1f : 0f;
            _driftInput = drift.driftEnabled && Input.GetKey(drift.driftKey);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  DRIFT STATE
    // ──────────────────────────────────────────────────────────

    private void UpdateDriftState()
    {
        Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
        _driftAngle = Mathf.Abs(Mathf.Atan2(localVel.x, localVel.z) * Mathf.Rad2Deg);
        _isDrifting = (_driftInput && _rb.linearVelocity.magnitude > drift.driftEntrySpeed)
                   || (_driftAngle > drift.driftAngleThreshold);
    }

    // ──────────────────────────────────────────────────────────
    //  PER-WHEEL PHYSICS
    // ──────────────────────────────────────────────────────────

    private void ProcessWheel(int i)
    {
        WheelConfig w = wheels[i];
        if (w.rayOrigin == null) return;

        float springK = w.springOverride > 0f ? w.springOverride * 80000f : suspension.springStiffness;
        float damperK = w.damperOverride > 0f ? w.damperOverride * 8000f : suspension.damperStiffness;

        Ray ray = new Ray(w.rayOrigin.position, -w.rayOrigin.up);
        float maxDist = suspension.restLength + suspension.wheelRadius;

        if (Physics.Raycast(ray, out RaycastHit hit, maxDist))
        {
            _wheelGrounded[i] = true;
            _contactPoints[i] = hit.point;

            float compression = 1f - ((hit.distance - suspension.wheelRadius) / suspension.restLength);
            compression = Mathf.Clamp01(compression);
            _wheelCompression[i] = compression;

            float springForce = compression * springK;
            float suspVelocity = Vector3.Dot(_rb.GetPointVelocity(hit.point), w.rayOrigin.up);
            float damperForce = -suspVelocity * damperK;
            _rb.AddForceAtPosition(hit.normal * (springForce + damperForce), hit.point);

            ApplyLateralForce(i, w, hit);
            ApplyLongitudinalForce(i, w, hit);

            Vector3 wheelVel = _rb.GetPointVelocity(hit.point);
            float lateralSpeed = Mathf.Abs(Vector3.Dot(wheelVel, w.rayOrigin.right));
            bool brakeSkid = w.isDriven && _brakeInput >= wheelSpin.lockUpThreshold;
            bool lateralSkid = lateralSpeed > wheelSpin.lateralSkidThreshold;
            _wheelSkidding[i] = brakeSkid || lateralSkid || _isDrifting;

            UpdateWheelSpinSpeed(i, w, wheelVel);

            _skidEffects[i]?.Tick(
                isGrounded: true,
                isSkidding: brakeSkid,
                isDrifting: _isDrifting || lateralSkid,
                contactPoint: hit.point,
                wheelRadius: suspension.wheelRadius);
        }
        else
        {
            _wheelGrounded[i] = false;
            _wheelCompression[i] = 0f;
            _wheelSkidding[i] = false;
            _wheelSpinSpeed[i] = Mathf.MoveTowards(_wheelSpinSpeed[i], 0f, 360f * Time.fixedDeltaTime);
            _skidEffects[i]?.Tick(false, false, false, Vector3.zero, suspension.wheelRadius);
        }

        WheelStates[i] = new WheelState
        {
            isGrounded = _wheelGrounded[i],
            isSkidding = _wheelSkidding[i],
            compression = _wheelCompression[i],
        };
    }

    // ──────────────────────────────────────────────────────────
    //  WHEEL SPIN SPEED
    // ──────────────────────────────────────────────────────────

    private void UpdateWheelSpinSpeed(int i, WheelConfig w, Vector3 wheelVel)
    {
        // Use the car's own forward (not the steered rayOrigin.forward) for
        // computing roll speed on all wheels. This ensures that even when
        // steering input rotates rayOrigin, the non-driven front wheels track
        // the car's actual forward velocity and spin at the correct rate.
        float forwardSpeed = Vector3.Dot(wheelVel, transform.forward);
        float rollSpeed = (forwardSpeed / (2f * Mathf.PI * suspension.wheelRadius)) * 360f;
        bool fullBrake = _brakeInput >= wheelSpin.lockUpThreshold;

        if (w.isDriven && fullBrake)
        {
            _wheelSpinSpeed[i] = Mathf.MoveTowards(
                _wheelSpinSpeed[i], 0f,
                Mathf.Abs(_wheelSpinSpeed[i]) / Mathf.Max(wheelSpin.drivenLockDecay, 0.001f)
                * Time.fixedDeltaTime);
        }
        else if (!w.isDriven && _brakeInput > 0.01f)
        {
            _wheelSpinSpeed[i] = Mathf.MoveTowards(
                _wheelSpinSpeed[i], 0f,
                Mathf.Abs(_wheelSpinSpeed[i]) / Mathf.Max(wheelSpin.brakeSpinDecay, 0.001f)
                * Time.fixedDeltaTime);
        }
        else
        {
            _wheelSpinSpeed[i] = Mathf.Lerp(_wheelSpinSpeed[i], rollSpeed, Time.fixedDeltaTime * 12f);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  LATERAL FORCE
    // ──────────────────────────────────────────────────────────

    private void ApplyLateralForce(int i, WheelConfig w, RaycastHit hit)
    {
        Vector3 wheelVel = _rb.GetPointVelocity(hit.point);
        float lateralSpeed = Vector3.Dot(wheelVel, w.rayOrigin.right);

        int groundedCount = 0;
        foreach (bool g in _wheelGrounded) if (g) groundedCount++;
        groundedCount = Mathf.Max(groundedCount, 1);

        float desiredForce = (-lateralSpeed * _rb.mass) / (groundedCount * Time.fixedDeltaTime);
        float maxGrip = traction.lateralGripMax;

        if (_driftInput && !w.isSteerable
            && _rb.linearVelocity.magnitude > drift.driftEntrySpeed)
        {
            maxGrip *= drift.driftGripMultiplier;
            if (Mathf.Sign(_steerInput) != 0 && Mathf.Sign(_steerInput) == Mathf.Sign(lateralSpeed))
                maxGrip *= (1f - drift.counterSteerAssist);
        }

        _rb.AddForceAtPosition(w.rayOrigin.right * Mathf.Clamp(desiredForce, -maxGrip, maxGrip), hit.point);
    }

    // ──────────────────────────────────────────────────────────
    //  LONGITUDINAL FORCE
    // ──────────────────────────────────────────────────────────

    private void ApplyLongitudinalForce(int i, WheelConfig w, RaycastHit hit)
    {
        if (!w.isDriven && _brakeInput == 0f) return;

        Vector3 wheelVel = _rb.GetPointVelocity(hit.point);
        float forwardSpeed = Vector3.Dot(wheelVel, w.rayOrigin.forward);
        float absSpeed = Mathf.Abs(forwardSpeed);
        float speedRatio = Mathf.Clamp01(absSpeed / engine.maxSpeed);
        float torqueMult = engine.torqueCurve.Evaluate(speedRatio);
        float force = 0f;

        if (w.isDriven)
        {
            if (_brakeInput > 0f)
                force = -Mathf.Sign(forwardSpeed) * traction.brakeForce * _brakeInput;
            else if (Mathf.Abs(_throttleInput) > 0.01f)
                force = _throttleInput * traction.tractionForce * torqueMult;
            else if (absSpeed > traction.stillSpeedThreshold)
                force = -Mathf.Sign(forwardSpeed) * traction.engineBraking;
        }
        else if (_brakeInput > 0f)
        {
            force = -Mathf.Sign(forwardSpeed) * traction.brakeForce * _brakeInput * 0.5f;
        }

        _rb.AddForceAtPosition(w.rayOrigin.forward * force, hit.point);
    }

    // ──────────────────────────────────────────────────────────
    //  YAW DAMPING
    // ──────────────────────────────────────────────────────────

    private void ApplyYawDamping()
    {
        if (stability.yawDampingTorque <= 0f) return;

        float yawRate = Vector3.Dot(_rb.angularVelocity, transform.up);
        float speed = _rb.linearVelocity.magnitude;
        float speedFactor = Mathf.Clamp01(speed / Mathf.Max(stability.yawDampingSpeedFull, 0.1f));
        float driftFactor = _driftInput ? stability.yawDampingDriftFraction : 1f;

        _rb.AddTorque(transform.up * (-yawRate * stability.yawDampingTorque * speedFactor * driftFactor),
                      ForceMode.Force);
    }

    // ──────────────────────────────────────────────────────────
    //  STEERING
    // ──────────────────────────────────────────────────────────

    private void UpdateSteering()
    {
        float speed = _rb.linearVelocity.magnitude;
        float speedFactor = Mathf.Clamp01(speed / steering.steerSpeedReduction);
        float baseAngle = Mathf.Abs(_steerInput)
                            * Mathf.Lerp(steering.maxSteerAngle, steering.maxSteerAngle * 0.4f, speedFactor);
        float steerSign = Mathf.Sign(_steerInput);

        float innerDeg = baseAngle, outerDeg = baseAngle;
        if (baseAngle > 0.5f)
        {
            float baseRad = baseAngle * Mathf.Deg2Rad;
            float R = steering.wheelbase / Mathf.Tan(baseRad);
            float halfTrack = steering.trackWidth * 0.5f;
            innerDeg = Mathf.Atan(steering.wheelbase / Mathf.Max(R - halfTrack, 0.1f)) * Mathf.Rad2Deg;
            outerDeg = Mathf.Atan(steering.wheelbase / (R + halfTrack)) * Mathf.Rad2Deg;
        }

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelConfig w = wheels[i];
            if (!w.isSteerable) continue;

            float targetAngle = 0f;
            if (Mathf.Abs(_steerInput) >= 0.01f)
            {
                bool turningRight = steerSign > 0f;
                bool isInner = turningRight ? !w.isLeftSide : w.isLeftSide;
                targetAngle = steerSign * (isInner ? innerDeg : outerDeg);
            }

            _currentSteerAngle[i] = Mathf.MoveTowards(
                _currentSteerAngle[i], targetAngle,
                steering.steerLerpSpeed * Time.deltaTime);

            if (w.rayOrigin != null)
            {
                Quaternion parentRot = w.rayOrigin.parent != null
                    ? w.rayOrigin.parent.rotation : Quaternion.identity;
                w.rayOrigin.rotation = parentRot * Quaternion.Euler(0f, _currentSteerAngle[i], 0f);
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  VISUAL WHEEL SPIN + SUSPENSION TRAVEL
    // ──────────────────────────────────────────────────────────

    private void UpdateWheelVisuals()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelConfig w = wheels[i];
            if (w.wheelTransform == null) continue;

            // Accumulate spin — never read back from Transform (avoids gimbal corruption)
            _wheelSpinAngle[i] += _wheelSpinSpeed[i] * Time.deltaTime;
            _wheelSpinAngle[i] %= 360f;

            // Left-side wheels have a 180° Y flip in their base rotation which
            // reverses the local X axis → negate spin so they roll forward.
            float spinAngle = w.isLeftSide ? -_wheelSpinAngle[i] : _wheelSpinAngle[i];

            float travel = _wheelGrounded[i]
                ? (1f - _wheelCompression[i]) * suspension.restLength
                : suspension.restLength;

            Transform spinPivot = _spinPivots[i];   // safe cached reference

            if (w.isSteerable)
            {
                float steerAngle = _currentSteerAngle[i];

                if (spinPivot != null && spinPivot != w.wheelTransform)
                {
                    // Two-transform hierarchy: steer pivot → spin mesh child
                    w.wheelTransform.localRotation =
                        _steerPivotBaseRot[i] * Quaternion.Euler(0f, steerAngle, 0f);

                    spinPivot.localRotation =
                        _spinPivotBaseRot[i] * Quaternion.Euler(spinAngle, 0f, 0f);

                    Vector3 lp = spinPivot.localPosition;
                    lp.y = -travel;
                    spinPivot.localPosition = lp;
                }
                else
                {
                    // Single-transform: wheelTransform is both steer and spin pivot
                    w.wheelTransform.localRotation =
                        _steerPivotBaseRot[i]
                        * Quaternion.Euler(0f, steerAngle, 0f)
                        * Quaternion.Euler(spinAngle, 0f, 0f);

                    Vector3 lp = w.wheelTransform.localPosition;
                    lp.y = -travel;
                    w.wheelTransform.localPosition = lp;
                }
            }
            else
            {
                // Non-steerable: spin pivot IS wheelTransform
                if (spinPivot == null) spinPivot = w.wheelTransform;

                spinPivot.localRotation =
                    _spinPivotBaseRot[i] * Quaternion.Euler(spinAngle, 0f, 0f);

                Vector3 lp = spinPivot.localPosition;
                lp.y = -travel;
                spinPivot.localPosition = lp;
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the transform that should receive the X-spin rotation.
    /// For steerable wheels with a mesh child: that child.
    /// For all other wheels: wheelTransform itself.
    /// MUST be called before any new child GOs are appended.
    /// </summary>
    private Transform ResolveSpinPivot(WheelConfig w)
    {
        if (w.wheelTransform == null) return null;
        if (w.isSteerable && w.wheelTransform.childCount > 0)
            return w.wheelTransform.GetChild(0);
        return w.wheelTransform;
    }

    // ──────────────────────────────────────────────────────────
    //  EDITOR GIZMOS
    // ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos || wheels == null) return;

        foreach (var w in wheels)
        {
            if (w.rayOrigin == null) continue;
            float maxDist = suspension.restLength + suspension.wheelRadius;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(w.rayOrigin.position,
                            w.rayOrigin.position - w.rayOrigin.up * maxDist);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(w.rayOrigin.position - w.rayOrigin.up * maxDist,
                                  suspension.wheelRadius * 0.5f);

            if (w.isSteerable)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(w.rayOrigin.position, w.rayOrigin.forward * 0.6f);
            }
        }

        if (_rb != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.TransformPoint(_rb.centerOfMass), 0.08f);
        }

        if (Application.isPlaying && _wheelSkidding != null)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!_wheelSkidding[i] || wheels[i].rayOrigin == null) continue;
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_contactPoints[i], 0.12f);
            }
        }
    }
#endif
}