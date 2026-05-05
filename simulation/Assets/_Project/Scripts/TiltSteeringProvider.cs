using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Reads the vector between the left and right XR controllers and maps its
/// continuous rotation around a vehicle-relative axis to a -1…+1 steering

/// value. Attach to each ego vehicle alongside the UserControl script.
/// CarUserControl / BikeUserControl will use <see cref="SteerValue"/> when
/// this component is present, enabled, and both controllers provide data.
/// </summary>
public class TiltSteeringProvider : MonoBehaviour
{
    public enum SteerAxis { Roll, Yaw, Pitch }

    [Header("Controller Wheel")]
    [Tooltip("Optional tracked left controller transform. If assigned with Right Controller Transform, world-space transform positions are used instead of raw XR devicePosition values.")]
    public Transform leftControllerTransform;

    [Tooltip("Optional tracked right controller transform. If assigned with Left Controller Transform, world-space transform positions are used instead of raw XR devicePosition values.")]
    public Transform rightControllerTransform;

    [Tooltip("Optional transform that converts raw XR devicePosition values from tracking space to world space when controller transforms are not assigned.")]
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

    [Header("Calibration")]
    [Tooltip("Auto-calibrate center the first time a valid two-controller vector arrives.")]
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

    /// <summary>True when both XR controllers are detected and the controller vector is usable.</summary>
    public bool HasController { get; private set; }

    /// <summary>Clamped physical controller/cradle angle in degrees relative to the active center, before invertSteering is applied.</summary>
    public float ControllerWheelAngle { get; private set; }

    /// <summary>Clamped physical controller/cradle angle in degrees after invertSteering is applied. This is the angle used to calculate SteerValue.</summary>
    public float SteeringWheelAngle { get; private set; }

    /// <summary>Current unwrapped controller-wheel steering angle in degrees after inversion.</summary>
    public float SteeringAngle => SteeringWheelAngle;

    private const float MinProjectedVectorSqrMagnitude = 0.0001f;

    private InputAction _keyboardSteerAction;
    private InputAction _calibrateAction;

    private float _centerAngle;
    private float _currentUnwrappedAngle;
    private float _lastWrappedAngle;
    private bool _hasLastWrappedAngle;
    private bool _calibrated;
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
            _calibrateAction = map?.FindAction("Calibrate", false);
        }
    }

    private void OnEnable()
    {
        _calibrated = false;
        _hasLastWrappedAngle = false;
        _currentUnwrappedAngle = 0f;
        _centerAngle = 0f;
        HasController = false;
        SteerValue = 0f;
        ControllerWheelAngle = 0f;
        SteeringWheelAngle = 0f;
        SetLineVisible(false);
        _keyboardSteerAction?.Enable();
        _calibrateAction?.Enable();
    }

    private void OnDisable()
    {
        HasController = false;
        SteerValue = 0f;
        ControllerWheelAngle = 0f;
        SteeringWheelAngle = 0f;
        SetLineVisible(false);
        _keyboardSteerAction?.Disable();
        _calibrateAction?.Disable();
    }

    private void Update()
    {
        if (!TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition) ||
            !TryCalculateWrappedAngle(leftPosition, rightPosition, out float wrappedAngle))
        {
            HasController = false;
            SteerValue = 0f;
            ControllerWheelAngle = 0f;
            SteeringWheelAngle = 0f;
            _hasLastWrappedAngle = false;
            SetLineVisible(false);
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
        {
            SetCenter(currentAngle);
        }

        // Manual calibration: use the Calibrate action
        bool calibrateHeld = _calibrateAction?.IsPressed() ?? false;
        if (calibrateHeld)
        {
            SetCenter(currentAngle);
        }

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

    /// <summary>Set the current two-controller vector as the steering center.</summary>
    public void Calibrate()
    {
        if (!TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition) ||
            !TryCalculateWrappedAngle(leftPosition, rightPosition, out float wrappedAngle))
        {
            return;
        }

        float currentAngle = UpdateUnwrappedAngle(wrappedAngle);
        SetCenter(currentAngle);
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

    private void SetCenter(float angle)
    {
        _centerAngle = angle;
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

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }
}
