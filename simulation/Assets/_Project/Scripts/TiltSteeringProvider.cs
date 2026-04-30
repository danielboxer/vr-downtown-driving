using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Reads the vector between the left and right XR controllers and maps its
/// continuous rotation around a vehicle-relative axis to a –1…+1 steering
/// value. Attach to each ego vehicle alongside the UserControl script.
/// CarUserControl / BikeUserControl will use <see cref="SteerValue"/> when
/// this component is present, enabled, and both controllers provide data.
/// </summary>
public class TiltSteeringProvider : MonoBehaviour
{
    // Kept so older prefab data that serialized a single controller hand can
    // deserialize cleanly, but steering now always uses both controllers.
    public enum Hand { Right, Left }
    public enum SteerAxis { Roll, Yaw, Pitch }

    [HideInInspector]
    public Hand controllerHand = Hand.Right;

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
    [Tooltip("Controller-wheel degrees from center to full steering lock. Real car wheels are commonly around 450°-540° each way.")]
    [Min(1f)]
    public float maxSteerAngle = 540f;

    [Tooltip("Negate the steering direction. Enable if rotating right steers left.")]
    public bool invertSteering = true;

    [Header("Calibration")]
    [Tooltip("Auto-calibrate center the first time a valid two-controller vector arrives.")]
    public bool calibrateOnEnable = true;

    [Header("Controller Vector Debug")]
    [Tooltip("Draw the live vector between the left and right controllers with a LineRenderer so it is visible in Game/VR view.")]
    public bool drawControllerLine = true;

    [Tooltip("Optional LineRenderer to use for the controller vector. If empty, one is created at runtime.")]
    public LineRenderer controllerLineRenderer;

    [Tooltip("Color of the controller vector line.")]
    public Color controllerLineColor = Color.cyan;

    [Tooltip("World-space width of the controller vector line.")]
    [Min(0.001f)]
    public float controllerLineWidth = 0.015f;

    [Tooltip("Also draw the vector using Debug.DrawLine for Scene view debugging.")]
    public bool drawDebugLine = true;

    [Header("Input Actions")]
    [Tooltip("Assign the InputSystem_Actions asset (same one used by CarUserControl/BikeUserControl).")]
    public InputActionAsset inputActions;

    [Tooltip("Action map that contains the Calibrate action.")]
    public string actionMapName = "Driving";

    /// <summary>Current steering value from –1 (full left) to +1 (full right).</summary>
    public float SteerValue { get; private set; }

    /// <summary>True when both XR controllers are detected and the controller vector is usable.</summary>
    public bool HasController { get; private set; }

    /// <summary>Current unwrapped controller-wheel angle in degrees relative to calibration.</summary>
    public float SteeringAngle => _currentUnwrappedAngle - _centerAngle;

    private const float MinProjectedVectorSqrMagnitude = 0.0001f;

    private float _centerAngle;
    private float _currentUnwrappedAngle;
    private float _lastWrappedAngle;
    private bool _hasLastWrappedAngle;
    private bool _calibrated;
    private InputAction _calibrateAction;
    private LineRenderer _runtimeLineRenderer;

    private void Awake()
    {
        if (inputActions != null)
        {
            var map = inputActions.FindActionMap(actionMapName, false);
            _calibrateAction = map?.FindAction("Calibrate", false);
        }
    }

    private void OnEnable()
    {
        _calibrateAction?.Enable();
        _calibrated = false;
        _hasLastWrappedAngle = false;
        _currentUnwrappedAngle = 0f;
        _centerAngle = 0f;
        HasController = false;
        SteerValue = 0f;
        SetLineVisible(false);
    }

    private void OnDisable()
    {
        _calibrateAction?.Disable();
        HasController = false;
        SteerValue = 0f;
        SetLineVisible(false);
    }

    private void Update()
    {
        if (!TryReadControllerPositions(out Vector3 leftPosition, out Vector3 rightPosition) ||
            !TryCalculateWrappedAngle(leftPosition, rightPosition, out float wrappedAngle))
        {
            HasController = false;
            SteerValue = 0f;
            _hasLastWrappedAngle = false;
            SetLineVisible(false);
            return;
        }

        HasController = true;
        DrawControllerVector(leftPosition, rightPosition);

        float currentAngle = UpdateUnwrappedAngle(wrappedAngle);

        // Auto-calibrate on first valid two-controller vector after enable.
        if (!_calibrated && calibrateOnEnable)
        {
            SetCenter(currentAngle);
        }

        // Manual calibration via button press.
        if (_calibrateAction != null && _calibrateAction.WasPressedThisFrame())
        {
            SetCenter(currentAngle);
        }

        float delta = currentAngle - _centerAngle;
        if (invertSteering) delta = -delta;

        SteerValue = Mathf.Clamp(delta / maxSteerAngle, -1f, 1f);
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

    private void SetCenter(float angle)
    {
        _centerAngle = angle;
        _calibrated = true;
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
        if (drawDebugLine)
        {
            Debug.DrawLine(leftPosition, rightPosition, controllerLineColor);
        }

        if (!drawControllerLine)
        {
            SetLineVisible(false);
            return;
        }

        LineRenderer line = GetOrCreateLineRenderer();
        if (line == null)
        {
            return;
        }

        line.enabled = true;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = controllerLineWidth;
        line.endWidth = controllerLineWidth;
        line.startColor = controllerLineColor;
        line.endColor = controllerLineColor;
        line.SetPosition(0, leftPosition);
        line.SetPosition(1, rightPosition);
    }

    private LineRenderer GetOrCreateLineRenderer()
    {
        if (controllerLineRenderer != null)
        {
            return controllerLineRenderer;
        }

        if (_runtimeLineRenderer != null)
        {
            return _runtimeLineRenderer;
        }

        _runtimeLineRenderer = gameObject.AddComponent<LineRenderer>();
        _runtimeLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _runtimeLineRenderer.receiveShadows = false;
        _runtimeLineRenderer.useWorldSpace = true;
        _runtimeLineRenderer.positionCount = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            _runtimeLineRenderer.material = new Material(shader)
            {
                color = controllerLineColor
            };
        }

        return _runtimeLineRenderer;
    }

    private void SetLineVisible(bool visible)
    {
        if (controllerLineRenderer != null)
        {
            controllerLineRenderer.enabled = visible;
        }

        if (_runtimeLineRenderer != null)
        {
            _runtimeLineRenderer.enabled = visible;
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }
}
