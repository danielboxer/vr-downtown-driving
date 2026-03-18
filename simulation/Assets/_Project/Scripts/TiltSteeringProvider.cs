using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Reads an XR controller's orientation and maps a rotation axis to a
/// –1…+1 steering value. Attach to each ego vehicle alongside the
/// UserControl script. CarUserControl / BikeUserControl will use
/// <see cref="SteerValue"/> when this component is present and enabled.
/// </summary>
public class TiltSteeringProvider : MonoBehaviour
{
    public enum Hand { Right, Left }
    public enum SteerAxis { Roll, Yaw, Pitch }

    [Header("Controller")]
    [Tooltip("Which hand's controller orientation to read.")]
    public Hand controllerHand = Hand.Right;

    [Tooltip("Rotation axis that maps to steering.\n" +
             "Roll (Z) = upright steering wheel.\n" +
             "Yaw (Y) = flat handlebars.")]
    public SteerAxis steerAxis = SteerAxis.Roll;

    [Header("Steering Range")]
    [Tooltip("Max tilt angle (degrees) for full steering lock.")]
    public float maxSteerAngle = 45f;

    [Tooltip("Negate the steering direction. Enable if tilting right steers left.")]
    public bool invertSteering = true;

    [Header("Calibration")]
    [Tooltip("Auto-calibrate center the first time a valid reading arrives.")]
    public bool calibrateOnEnable = true;

    [Header("Input Actions")]
    [Tooltip("Assign the InputSystem_Actions asset (same one used by CarUserControl).")]
    public InputActionAsset inputActions;

    [Tooltip("Action map that contains the Calibrate action.")]
    public string actionMapName = "Driving";

    /// <summary>Current steering value from –1 (full left) to +1 (full right).</summary>
    public float SteerValue { get; private set; }

    private float _centerAngle;
    private bool _calibrated;
    private InputAction _calibrateAction;

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
        SteerValue = 0f;
    }

    private void OnDisable()
    {
        _calibrateAction?.Disable();
        SteerValue = 0f;
    }

    private void Update()
    {
        XRController controller = controllerHand == Hand.Right
            ? XRController.rightHand
            : XRController.leftHand;

        if (controller == null) return;

        Quaternion rotation = controller.deviceRotation.ReadValue();
        float currentAngle = ExtractAxis(rotation);

        // Auto-calibrate on first valid reading after enable
        if (!_calibrated && calibrateOnEnable)
        {
            _centerAngle = currentAngle;
            _calibrated = true;
        }

        // Manual calibration via button press
        if (_calibrateAction != null && _calibrateAction.WasPressedThisFrame())
        {
            Calibrate();
        }

        float delta = Mathf.DeltaAngle(_centerAngle, currentAngle);
        if (invertSteering) delta = -delta;
        SteerValue = Mathf.Clamp(delta / maxSteerAngle, -1f, 1f);
    }

    /// <summary>Set the current controller orientation as the steering center.</summary>
    public void Calibrate()
    {
        XRController controller = controllerHand == Hand.Right
            ? XRController.rightHand
            : XRController.leftHand;

        if (controller != null)
        {
            _centerAngle = ExtractAxis(controller.deviceRotation.ReadValue());
            _calibrated = true;
        }
    }

    private float ExtractAxis(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        return steerAxis switch
        {
            SteerAxis.Roll => euler.z,
            SteerAxis.Yaw => euler.y,
            SteerAxis.Pitch => euler.x,
            _ => euler.z
        };
    }
}
