using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// Maps XR controller steering to a -1…+1 steering value. By default it reads
/// the position vector between the left and right controllers (used by car mode).
/// Rotation-only mode reads controller rotations instead, which avoids relying on
/// controller position tracking (used by bike mode).
/// Attach to each ego vehicle alongside the UserControl script.
/// CarUserControl / BikeUserControl will use <see cref="SteerValue"/> when
/// this component is present, enabled, and both controllers provide data.
/// </summary>
public class TiltSteeringProvider : MonoBehaviour
{
    public enum ControllerTrackingMode { PositionVector, RotationOnly }
    public enum SteerAxis { Roll, Yaw, Pitch }

    [Header("Controller Tracking")]
    [Tooltip("Position Vector uses the line between controllers. Rotation Only uses controller rotations and ignores controller positions.")]
    public ControllerTrackingMode controllerTrackingMode = ControllerTrackingMode.PositionVector;

    [Header("Controller Wheel")]
    [Tooltip("Optional tracked left controller transform. In Position Vector mode this uses world-space positions; in Rotation Only mode this uses world-space rotations.")]
    public Transform leftControllerTransform;

    [Tooltip("Optional tracked right controller transform. In Position Vector mode this uses world-space positions; in Rotation Only mode this uses world-space rotations.")]
    public Transform rightControllerTransform;

    [Tooltip("Optional transform that converts raw XR devicePosition/deviceRotation values from tracking space to world space when controller transforms are not assigned.")]
    public Transform devicePositionSpace;

    [Tooltip("Minimum distance between controllers before the controller vector is considered valid.")]
    [Min(0.01f)]
    public float minControllerSeparation = 0.15f;

    [Header("Vehicle Axis")]
    [Tooltip("Transform whose orientation defines the steering axis. Leave empty to use this vehicle transform.")]
    public Transform steeringReference;

    [Tooltip("Vehicle-local axis that the virtual wheel rotates around.\n" +
             "Roll (local Z/forward) = upright car steering wheel.\n" +
             "Yaw (local Y/up) = horizontal bike handlebars.\n" +
             "Pitch (local X/right) = side-mounted/custom setup.")]
    public SteerAxis steerAxis = SteerAxis.Roll;

    [Header("Steering Range")]
    [Tooltip("Controller-wheel degrees from center to full steering lock. Real car wheels are commonly around 450°-540° each way; bike handlebars are usually much smaller, often around 30°-60°.")]
    [Min(1f)]
    public float maxSteerAngle = 450f;

    [Tooltip("How much extra physical rotation past the software steering lock can be remembered. 360° means about one extra full turn past lock; 0° means no extra turn is saved.")]
    [Min(0f)]
    public float savedAnglePastSteeringLock = 360f;

    [Tooltip("Negate the steering direction. Enable if rotating right steers left.")]
    public bool invertSteering = true;

    [Header("Keyboard Fallback")]
    [Tooltip("Optional input actions asset. When the Steer action has a non-zero value the keyboard path bypasses the tilt angle accumulation and maps the value directly to SteerValue.")]
    public InputActionAsset inputActions;

    [Tooltip("Action map name containing the Steer action used for keyboard fallback.")]
    public string actionMapName = "Driving";

    [Header("Headset Detection")]
    [Tooltip("When enabled, tilt steering is suppressed unless a real XR headset is detected. Disable this when testing on desktop without a headset so keyboard steering still works.")]
    public bool requireHeadset = true;

    [Header("Calibration")]
    [Tooltip("Auto-calibrate center the first time valid controller steering input arrives.")]
    public bool calibrateOnEnable = true;

    [Header("Controller Vector Debug")]
    [Tooltip("Draw the controller vector projected onto the active steering plane and centered between the controllers. This is the vector actually used for steering.")]
    public bool drawProjectedControllerLine = true;

    [Tooltip("Optional LineRenderer to use for the projected controller vector. If empty, one is created at runtime.")]
    public LineRenderer projectedControllerLineRenderer;

    [Tooltip("Also draw the raw and projected vectors using Debug.DrawLine for Scene view debugging.")]
    public bool drawDebugLine = true;

    // Visual style constants for the debug lines (not exposed in Inspector).
    private static readonly Color ProjectedLineColor = Color.black;
    private const float ProjectedLineWidth = 0.004f;

    /// <summary>Current steering value from -1 (full left) to +1 (full right).</summary>
    public float SteerValue { get; private set; }

    /// <summary>True when both XR controllers are detected and the configured steering input is usable.</summary>
    public bool HasController { get; private set; }

    /// <summary>Clamped physical controller/cradle angle in degrees relative to the active center, before invertSteering is applied.</summary>
    public float ControllerWheelAngle { get; private set; }

    /// <summary>Clamped physical controller/cradle angle in degrees after invertSteering is applied. This is the angle used to calculate SteerValue.</summary>
    public float SteeringWheelAngle { get; private set; }

    /// <summary>Current unwrapped controller-wheel steering angle in degrees after inversion.</summary>
    public float SteeringAngle => SteeringWheelAngle;

    private const float MinProjectedVectorSqrMagnitude = 0.0001f;

    private InputAction _keyboardSteerAction;
    private bool _headsetConnected;
    private bool _simulatorActive;
    // Reused buffer to avoid GC allocations during headset state refresh.
    private static readonly List<UnityEngine.XR.InputDevice> _headsetCheckBuffer = new List<UnityEngine.XR.InputDevice>();

    private float _centerAngle;
    private float _currentUnwrappedAngle;
    private float _lastWrappedAngle;
    private bool _hasLastWrappedAngle;
    private bool _calibrated;
    private Quaternion _leftCenterRotation = Quaternion.identity;
    private Quaternion _rightCenterRotation = Quaternion.identity;
    private LineRenderer _runtimeProjectedLineRenderer;

    private void Awake()
    {
        // Auto-detect the input asset from a sibling vehicle control script if not assigned.
        if (inputActions == null)
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                var f = mb.GetType().GetField(
                    "inputActions",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (f?.FieldType == typeof(InputActionAsset))
                {
                    inputActions = f.GetValue(mb) as InputActionAsset;
                    if (inputActions != null) break;
                }
            }
        }

        if (inputActions != null)
        {
            var map = inputActions.FindActionMap(actionMapName, false);
            _keyboardSteerAction = map?.FindAction("Steer", false);
        }
    }

    private void OnEnable()
    {
        // Capture headset/simulator state immediately and subscribe for future changes.
        RefreshHeadsetState();
        InputDevices.deviceConnected += OnXRDeviceChanged;
        InputDevices.deviceDisconnected += OnXRDeviceChanged;
        // The simulator presence doesn't change at runtime, so one check suffices.
        _simulatorActive = FindFirstObjectByType<XRInteractionSimulator>() != null;
        _calibrated = false;
        _currentUnwrappedAngle = 0f;
        _centerAngle = 0f;
        ClearSteeringState(true);
        _keyboardSteerAction?.Enable();
    }

    private void OnDisable()
    {
        InputDevices.deviceConnected -= OnXRDeviceChanged;
        InputDevices.deviceDisconnected -= OnXRDeviceChanged;
        ClearSteeringState(false);
        _keyboardSteerAction?.Disable();
    }

    private void OnXRDeviceChanged(UnityEngine.XR.InputDevice device)
    {
        // Re-check headset state only when the changed device is an HMD.
        if ((device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0)
            RefreshHeadsetState();
    }

    private void RefreshHeadsetState()
    {
        _headsetCheckBuffer.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, _headsetCheckBuffer);
        _headsetConnected = _headsetCheckBuffer.Count > 0 && _headsetCheckBuffer[0].isValid;
    }

    private void Update()
    {
        // Tilt steering is only valid when a real headset is present and the
        // XR Interaction Simulator is not running. The simulator repositions
        // virtual controllers whenever you look around, corrupting the tilt
        // center. When either condition fails, CarUserControl falls back to
        // keyboard steering.
        if (requireHeadset && (_simulatorActive || !_headsetConnected))
        {
            ClearSteeringState(false);
            return;
        }

        if (controllerTrackingMode == ControllerTrackingMode.RotationOnly)
            UpdateFromControllerRotations();
        else
            UpdateFromControllerPositions();
    }

    private void UpdateFromControllerPositions()
    {
        if (!TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition) ||
            !TryCalculateWrappedAngle(leftPosition, rightPosition, out float wrappedAngle))
        {
            ClearSteeringState(true);
            // Keyboard fallback: no controller vector, but keyboard may still steer.
            TryApplyKeyboardSteering();
            return;
        }

        HasController = true;
        DrawControllerVector(leftPosition, rightPosition);

        // Keyboard override: intercept before angle accumulation runs.
        // When keyboard is active, output the direct value without accumulating
        // the unwrapped angle. _lastWrappedAngle is still updated so that
        // returning to tilt mode is seamless (no sudden jump in angle).
        if (TryApplyKeyboardSteering())
        {
            // First-time calibration: anchor the tilt center even while keyboard
            // is held so tilt steering starts centered when keyboard is released.
            if (!_calibrated && calibrateOnEnable)
            {
                float initAngle = UpdateUnwrappedAngle(wrappedAngle);
                SetCenter(initAngle);
            }
            else
            {
                _lastWrappedAngle = wrappedAngle;
                _hasLastWrappedAngle = true;
            }
            return;
        }

        float currentAngle = UpdateUnwrappedAngle(wrappedAngle);

        // Auto-calibrate on first valid two-controller vector after enable.
        if (!_calibrated && calibrateOnEnable)
            SetCenter(currentAngle);

        ApplyTrackedSteeringAngle(currentAngle);
    }

    private void UpdateFromControllerRotations()
    {
        if (!TryReadControllerRotations(out Quaternion leftRotation, out Quaternion rightRotation))
        {
            ClearSteeringState(true);
            // Keyboard fallback: no controller rotations, but keyboard may still steer.
            TryApplyKeyboardSteering();
            return;
        }

        HasController = true;
        SetLineVisible(false);

        // Keyboard override: keep the same behavior as position-based steering.
        if (TryApplyKeyboardSteering())
        {
            if (!_calibrated && calibrateOnEnable)
                SetRotationCenter(leftRotation, rightRotation);
            return;
        }

        // Rotation-only steering has no meaningful absolute neutral angle; the
        // first valid rotation pair seeds the neutral pose. Manual calibration
        // can recenter this at any time.
        if (!_calibrated)
            SetRotationCenter(leftRotation, rightRotation);

        if (!TryCalculateWrappedAngle(leftRotation, rightRotation, out float wrappedAngle))
        {
            ClearSteeringState(true);
            TryApplyKeyboardSteering();
            return;
        }

        ApplyTrackedSteeringAngle(UpdateUnwrappedAngle(wrappedAngle));
    }

    /// <summary>Set the current controller steering input as the steering center.</summary>
    public bool Calibrate()
    {
        if (controllerTrackingMode == ControllerTrackingMode.RotationOnly)
        {
            if (!TryReadControllerRotations(out Quaternion leftRotation, out Quaternion rightRotation))
                return false;

            SetRotationCenter(leftRotation, rightRotation);
            return true;
        }

        if (!TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition) ||
            !TryCalculateWrappedAngle(leftPosition, rightPosition, out float wrappedAngle))
        {
            return false;
        }

        float currentAngle = UpdateUnwrappedAngle(wrappedAngle);
        SetCenter(currentAngle);
        return true;
    }

    /// <summary>
    /// Keyboard shortcut path: maps the steer action value directly to SteerValue without
    /// angle accumulation. Returns true and sets all steer properties when keyboard is active.
    /// </summary>
    private bool TryApplyKeyboardSteering()
    {
        float keyboardSteer = _keyboardSteerAction?.ReadValue<float>() ?? 0f;
        if (Mathf.Abs(keyboardSteer) < 0.001f)
            return false;

        HasController = true;
        SteerValue = Mathf.Clamp(keyboardSteer, -1f, 1f);
        float lockAngle = Mathf.Max(1f, maxSteerAngle);
        // Both angles mirror the steer value so visual wheels track correctly.
        SteeringWheelAngle = SteerValue * lockAngle;
        ControllerWheelAngle = SteeringWheelAngle;
        return true;
    }

    private void ApplyTrackedSteeringAngle(float currentAngle)
    {
        currentAngle = ClampTrackedAnglePastSteeringLock(currentAngle);
        _currentUnwrappedAngle = currentAngle;

        float lockAngle = Mathf.Max(1f, maxSteerAngle);
        float rawControllerWheelAngle = currentAngle - _centerAngle;
        float rawSteeringWheelAngle = invertSteering ? -rawControllerWheelAngle : rawControllerWheelAngle;

        // These are the angles used by vehicle input and wheel/handlebar visuals.
        // They stop at the software steering lock even if the physical cradle keeps rotating.
        ControllerWheelAngle = Mathf.Clamp(rawControllerWheelAngle, -lockAngle, lockAngle);
        SteeringWheelAngle = Mathf.Clamp(rawSteeringWheelAngle, -lockAngle, lockAngle);

        SteerValue = SteeringWheelAngle / lockAngle;
    }

    private void ClearSteeringState(bool resetAngleTracking)
    {
        HasController = false;
        SteerValue = 0f;
        ControllerWheelAngle = 0f;
        SteeringWheelAngle = 0f;
        if (resetAngleTracking)
            _hasLastWrappedAngle = false;
        SetLineVisible(false);
    }

    private void SetCenter(float angle)
    {
        _centerAngle = angle;
        _calibrated = true;
    }

    private void SetRotationCenter(Quaternion leftRotation, Quaternion rightRotation)
    {
        _leftCenterRotation = NormalizeQuaternion(leftRotation);
        _rightCenterRotation = NormalizeQuaternion(rightRotation);
        _centerAngle = 0f;
        _currentUnwrappedAngle = 0f;
        _lastWrappedAngle = 0f;
        _hasLastWrappedAngle = true;
        ControllerWheelAngle = 0f;
        SteeringWheelAngle = 0f;
        SteerValue = 0f;
        _calibrated = true;
    }

    private float ClampTrackedAnglePastSteeringLock(float angle)
    {
        float lockAngle = Mathf.Max(1f, maxSteerAngle);
        float savedOverflowAngle = Mathf.Max(0f, savedAnglePastSteeringLock);
        float trackingLimit = lockAngle + savedOverflowAngle;
        return Mathf.Clamp(angle, _centerAngle - trackingLimit, _centerAngle + trackingLimit);
    }

    private bool TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition)
    {
        leftPosition = default;
        rightPosition = default;

        if (leftControllerTransform != null && rightControllerTransform != null)
        {
            leftPosition = leftControllerTransform.position;
            rightPosition = rightControllerTransform.position;
            return HasUsableControllerVector(leftPosition, rightPosition);
        }

        XRController leftController = XRController.leftHand;
        XRController rightController = XRController.rightHand;

        if (leftController == null || rightController == null ||
            leftController.devicePosition == null || rightController.devicePosition == null)
        {
            return false;
        }

        leftPosition = leftController.devicePosition.ReadValue();
        rightPosition = rightController.devicePosition.ReadValue();

        if (devicePositionSpace != null)
        {
            leftPosition = devicePositionSpace.TransformPoint(leftPosition);
            rightPosition = devicePositionSpace.TransformPoint(rightPosition);
        }

        return HasUsableControllerVector(leftPosition, rightPosition);
    }

    private bool TryReadControllerRotations(out Quaternion leftRotation, out Quaternion rightRotation)
    {
        leftRotation = Quaternion.identity;
        rightRotation = Quaternion.identity;

        if (leftControllerTransform != null && rightControllerTransform != null)
        {
            leftRotation = leftControllerTransform.rotation;
            rightRotation = rightControllerTransform.rotation;
        }
        else
        {
            XRController leftController = XRController.leftHand;
            XRController rightController = XRController.rightHand;

            if (leftController == null || rightController == null ||
                leftController.deviceRotation == null || rightController.deviceRotation == null)
            {
                return false;
            }

            leftRotation = leftController.deviceRotation.ReadValue();
            rightRotation = rightController.deviceRotation.ReadValue();

            if (devicePositionSpace != null)
            {
                leftRotation = devicePositionSpace.rotation * leftRotation;
                rightRotation = devicePositionSpace.rotation * rightRotation;
            }
        }

        if (!IsUsableRotation(leftRotation) || !IsUsableRotation(rightRotation))
        {
            return false;
        }

        ConvertControllerRotationsToSteeringReferenceSpace(ref leftRotation, ref rightRotation);
        return IsUsableRotation(leftRotation) && IsUsableRotation(rightRotation);
    }

    private void ConvertControllerRotationsToSteeringReferenceSpace(ref Quaternion leftRotation, ref Quaternion rightRotation)
    {
        // Compare rotations in the vehicle/steering-reference frame so simply
        // turning the bike or car in world space does not look like steering.
        Transform reference = steeringReference != null ? steeringReference : transform;
        Quaternion inverseReferenceRotation = Quaternion.Inverse(reference.rotation);
        leftRotation = inverseReferenceRotation * leftRotation;
        rightRotation = inverseReferenceRotation * rightRotation;
    }

    private bool HasUsableControllerVector(Vector3 leftPosition, Vector3 rightPosition)
    {
        if (!IsFinite(leftPosition) || !IsFinite(rightPosition))
        {
            return false;
        }

        return (rightPosition - leftPosition).sqrMagnitude >= minControllerSeparation * minControllerSeparation;
    }

    private bool TryCalculateWrappedAngle(Vector3 leftPosition, Vector3 rightPosition, out float wrappedAngle)
    {
        wrappedAngle = 0f;

        Vector3 axis = GetSteeringAxis();
        if (axis.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }
        axis.Normalize();

        Vector3 controllerVector = rightPosition - leftPosition;
        Vector3 projectedControllerVector = Vector3.ProjectOnPlane(controllerVector, axis);
        if (projectedControllerVector.sqrMagnitude < MinProjectedVectorSqrMagnitude)
        {
            return false;
        }
        projectedControllerVector.Normalize();

        Vector3 zeroDirection = GetZeroDirection(axis);
        if (zeroDirection.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }
        zeroDirection.Normalize();

        wrappedAngle = Vector3.SignedAngle(zeroDirection, projectedControllerVector, axis);
        return true;
    }

    private bool TryCalculateWrappedAngle(Quaternion leftRotation, Quaternion rightRotation, out float wrappedAngle)
    {
        wrappedAngle = 0f;

        Vector3 axis = GetLocalSteeringAxis();
        if (axis.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }
        axis.Normalize();

        if (!TryCalculateSignedTwistAngle(_leftCenterRotation, leftRotation, axis, out float leftAngle) ||
            !TryCalculateSignedTwistAngle(_rightCenterRotation, rightRotation, axis, out float rightAngle))
        {
            return false;
        }

        wrappedAngle = AverageWrappedAngles(leftAngle, rightAngle);
        return true;
    }

    private float UpdateUnwrappedAngle(float wrappedAngle)
    {
        if (!_hasLastWrappedAngle)
        {
            // Keep continuity with the current unwrapped angle when tracking resumes.
            float currentWrappedAngle = Mathf.DeltaAngle(0f, _currentUnwrappedAngle);
            _currentUnwrappedAngle += Mathf.DeltaAngle(currentWrappedAngle, wrappedAngle);
        }
        else
        {
            _currentUnwrappedAngle += Mathf.DeltaAngle(_lastWrappedAngle, wrappedAngle);
        }

        _lastWrappedAngle = wrappedAngle;
        _hasLastWrappedAngle = true;
        return _currentUnwrappedAngle;
    }

    private Vector3 GetSteeringAxis()
    {
        Transform reference = steeringReference != null ? steeringReference : transform;

        return steerAxis switch
        {
            SteerAxis.Roll => reference.forward,
            SteerAxis.Yaw => reference.up,
            SteerAxis.Pitch => reference.right,
            _ => reference.forward
        };
    }

    private Vector3 GetLocalSteeringAxis()
    {
        return steerAxis switch
        {
            SteerAxis.Roll => Vector3.forward,
            SteerAxis.Yaw => Vector3.up,
            SteerAxis.Pitch => Vector3.right,
            _ => Vector3.forward
        };
    }

    private Vector3 GetZeroDirection(Vector3 axis)
    {
        Transform reference = steeringReference != null ? steeringReference : transform;

        Vector3 zeroDirection = steerAxis switch
        {
            SteerAxis.Pitch => reference.up,
            _ => reference.right
        };

        zeroDirection = Vector3.ProjectOnPlane(zeroDirection, axis);
        if (zeroDirection.sqrMagnitude >= Mathf.Epsilon)
        {
            return zeroDirection;
        }

        // Fallback for unusual reference transforms where the preferred zero
        // direction is parallel to the steering axis.
        zeroDirection = Vector3.Cross(axis, Vector3.up);
        if (zeroDirection.sqrMagnitude >= Mathf.Epsilon)
        {
            return zeroDirection;
        }

        return Vector3.Cross(axis, Vector3.right);
    }

    private void DrawControllerVector(Vector3 leftPosition, Vector3 rightPosition)
    {
        Vector3 rawVector = rightPosition - leftPosition;
        Vector3 midpoint = (leftPosition + rightPosition) * 0.5f;
        bool hasProjectedVector = TryGetProjectedVector(rawVector, out Vector3 projectedVector);

        // The black line is drawn along the raw controller direction, scaled by the
        // projection magnitude, so its length reflects how well the vector is in the steering plane.
        Vector3 projectedStart = midpoint;
        Vector3 projectedEnd = midpoint;
        if (hasProjectedVector)
        {
            Vector3 rawDir = rawVector.sqrMagnitude > Mathf.Epsilon ? rawVector.normalized : Vector3.right;
            float halfLen = projectedVector.magnitude * 0.5f;
            projectedStart = midpoint - rawDir * halfLen;
            projectedEnd = midpoint + rawDir * halfLen;
        }

        if (drawDebugLine && hasProjectedVector)
        {
            Debug.DrawLine(projectedStart, projectedEnd, ProjectedLineColor);
        }

        DrawLine(GetOrCreateProjectedLineRenderer(), drawProjectedControllerLine && hasProjectedVector, projectedStart, projectedEnd, ProjectedLineColor, ProjectedLineWidth);
    }

    private bool TryGetProjectedVector(Vector3 rawVector, out Vector3 projectedVector)
    {
        projectedVector = Vector3.zero;

        Vector3 axis = GetSteeringAxis();
        if (axis.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }

        projectedVector = Vector3.ProjectOnPlane(rawVector, axis.normalized);
        return projectedVector.sqrMagnitude >= MinProjectedVectorSqrMagnitude;
    }

    private void DrawLine(LineRenderer line, bool visible, Vector3 start, Vector3 end, Color color, float width)
    {
        if (line == null)
        {
            return;
        }

        if (!visible)
        {
            line.enabled = false;
            return;
        }

        line.enabled = true;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        if (line.material != null)
        {
            line.material.color = color;
        }
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    private LineRenderer GetOrCreateProjectedLineRenderer()
    {
        if (projectedControllerLineRenderer != null)
        {
            return projectedControllerLineRenderer;
        }

        if (_runtimeProjectedLineRenderer == null)
        {
            _runtimeProjectedLineRenderer = CreateRuntimeLineRenderer("Projected Steering Vector", ProjectedLineColor);
            // Render on top of the raw controller line.
            _runtimeProjectedLineRenderer.sortingOrder = 1;
        }

        return _runtimeProjectedLineRenderer;
    }

    private LineRenderer CreateRuntimeLineRenderer(string lineName, Color color)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.useWorldSpace = true;
        line.positionCount = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            line.material = new Material(shader)
            {
                color = color
            };
        }

        return line;
    }

    private void SetLineVisible(bool visible)
    {
        if (projectedControllerLineRenderer != null)
        {
            projectedControllerLineRenderer.enabled = visible;
        }

        if (_runtimeProjectedLineRenderer != null)
        {
            _runtimeProjectedLineRenderer.enabled = visible;
        }
    }

    private static bool TryCalculateSignedTwistAngle(Quaternion centerRotation, Quaternion currentRotation, Vector3 axis, out float signedAngle)
    {
        signedAngle = 0f;

        if (!IsUsableRotation(centerRotation) || !IsUsableRotation(currentRotation) || axis.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }

        axis.Normalize();
        Quaternion delta = NormalizeQuaternion(currentRotation) * Quaternion.Inverse(NormalizeQuaternion(centerRotation));

        Vector3 deltaVector = new Vector3(delta.x, delta.y, delta.z);
        Vector3 twistVector = Vector3.Project(deltaVector, axis);
        Quaternion twist = new Quaternion(twistVector.x, twistVector.y, twistVector.z, delta.w);

        if (!IsUsableRotation(twist))
        {
            signedAngle = 0f;
            return true;
        }

        twist = NormalizeQuaternion(twist);
        twist.ToAngleAxis(out float angle, out Vector3 twistAxis);
        if (!IsFinite(twistAxis))
        {
            return false;
        }

        if (angle > 180f)
        {
            angle -= 360f;
        }

        if (Vector3.Dot(twistAxis, axis) < 0f)
        {
            angle = -angle;
        }

        signedAngle = Mathf.DeltaAngle(0f, angle);
        return true;
    }

    private static float AverageWrappedAngles(float firstAngle, float secondAngle)
    {
        float firstRadians = firstAngle * Mathf.Deg2Rad;
        float secondRadians = secondAngle * Mathf.Deg2Rad;
        float x = Mathf.Cos(firstRadians) + Mathf.Cos(secondRadians);
        float y = Mathf.Sin(firstRadians) + Mathf.Sin(secondRadians);

        if (x * x + y * y < Mathf.Epsilon)
        {
            return firstAngle;
        }

        return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
        float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
        if (magnitude <= Mathf.Epsilon)
        {
            return Quaternion.identity;
        }

        float inverseMagnitude = 1f / magnitude;
        return new Quaternion(
            value.x * inverseMagnitude,
            value.y * inverseMagnitude,
            value.z * inverseMagnitude,
            value.w * inverseMagnitude);
    }

    private static bool IsUsableRotation(Quaternion value)
    {
        if (!IsFinite(value))
        {
            return false;
        }

        float sqrMagnitude = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        return sqrMagnitude > Mathf.Epsilon;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z) &&
               float.IsFinite(value.w);
    }
}
