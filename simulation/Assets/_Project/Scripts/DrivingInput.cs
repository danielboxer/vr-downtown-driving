using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class DrivingInput
{
    private const float ThrottleDeadzone = 0.05f;
    private const float BrakeDeadzone = 0.01f;

    private readonly InputAction _steerAction;
    private readonly InputAction _accelerateAction;
    private readonly InputAction _brakeAction;
    private readonly InputAction _gearChangeAction;
    private readonly InputAction _gearDriveAction;   // XR: right thumbstick up
    private readonly InputAction _gearReverseAction; // XR: right thumbstick down
    private readonly InputAction _holdToReverseAction;

    private readonly bool _accelerateUnlatches;
    private readonly bool _xrTriggersAreBrakeLevers;

    private float _smoothedSteer;
    private bool _throttleLatched;
    private bool _ignoreThrottleUntilReleased;

    public float Steer { get; private set; }
    public float Throttle { get; private set; }
    public float Brake { get; private set; }
    public bool IsReverse { get; private set; }
    public float BrakeLeverPull { get; private set; }
    public bool GearChangedThisFrame { get; private set; }

    public DrivingInput(InputActionAsset inputActions, string actionMapName, bool accelerateUnlatches = false, bool xrTriggersAreBrakeLevers = false, bool holdToReverse = false)
    {
        _accelerateUnlatches = accelerateUnlatches;
        _xrTriggersAreBrakeLevers = xrTriggersAreBrakeLevers;

        var map = inputActions?.FindActionMap(actionMapName, false);
        if (map == null)
            return;

        _steerAction = map.FindAction("Steer", false);
        _accelerateAction = map.FindAction("Accelerate", false);
        _brakeAction = map.FindAction("Brake", false);
        if (holdToReverse)
        {
            _holdToReverseAction = map.FindAction("BikeReverse", false);
            return;
        }

        _gearChangeAction = map.FindAction("GearChange", false);
        _gearDriveAction = map.FindAction("GearDrive", false);
        _gearReverseAction = map.FindAction("GearReverse", false);
    }

    public void Enable()
    {
        _steerAction?.Enable();
        _accelerateAction?.Enable();
        _brakeAction?.Enable();
        _gearChangeAction?.Enable();
        _gearDriveAction?.Enable();
        _gearReverseAction?.Enable();
        _holdToReverseAction?.Enable();

        _smoothedSteer = 0f;
        _throttleLatched = false;
        _ignoreThrottleUntilReleased = true;
        Steer = 0f;
        Throttle = 0f;
        Brake = 0f;
        IsReverse = false;
        BrakeLeverPull = 0f;
        GearChangedThisFrame = false;
    }

    public void Disable()
    {
        _steerAction?.Disable();
        _accelerateAction?.Disable();
        _brakeAction?.Disable();
        _gearChangeAction?.Disable();
        _gearDriveAction?.Disable();
        _gearReverseAction?.Disable();
        _holdToReverseAction?.Disable();
    }

    public void Update(TiltSteeringProvider tiltSteering, float steerSmoothing, float steerReturnSmoothing)
    {
        float rawSteer = _steerAction?.ReadValue<float>() ?? 0f;
        float steerRate = rawSteer != 0f ? steerSmoothing : steerReturnSmoothing;
        _smoothedSteer = Mathf.MoveTowards(_smoothedSteer, rawSteer, steerRate * Time.deltaTime);
        Steer = TiltSteeringProvider.CombineSteer(tiltSteering, _smoothedSteer);

        UpdateGear();

        Brake = ReadPedal(_brakeAction);
        float throttle = ReadPedal(_accelerateAction);
        BrakeLeverPull = _xrTriggersAreBrakeLevers
            ? Mathf.Max(ReadControls(_accelerateAction, fromXRController: true), ReadControls(_brakeAction, fromXRController: true))
            : 0f;
        // the trigger pull that started the drive from the main menu is still held
        if (throttle <= ThrottleDeadzone) _ignoreThrottleUntilReleased = false;
        if (_ignoreThrottleUntilReleased) throttle = 0f;

        if (AccelerationSetting.Mode == AccelerationMode.Instant)
        {
            bool acceleratePressed = _accelerateAction != null && _accelerateAction.WasPressedThisFrame() &&
                !(_xrTriggersAreBrakeLevers && _accelerateAction.activeControl?.device is XRController);
            if (Brake > BrakeDeadzone) _throttleLatched = false;
            else if (_accelerateUnlatches && acceleratePressed) _throttleLatched = !_throttleLatched;
            else if (throttle > ThrottleDeadzone) _throttleLatched = true;
            throttle = _throttleLatched ? 1f : 0f;
        }
        else
        {
            _throttleLatched = false;
        }

        Throttle = throttle;
    }

    // instant mode skips the acceleration cue that causes sim sickness
    public void ApplyInstantSpeed(Rigidbody rigidbody, float maxSpeedMs, Vector3 forward)
    {
        // braking is left to the wheel brakes
        if (AccelerationSetting.Mode != AccelerationMode.Instant || !_throttleLatched)
            return;

        Vector3 heading = IsReverse ? -forward : forward;
        VrFastSpeed.Apply(rigidbody, maxSpeedMs, heading);
    }

    private float ReadPedal(InputAction action)
    {
        if (_xrTriggersAreBrakeLevers)
            return ReadControls(action, fromXRController: false);
        return action?.ReadValue<float>() ?? 0f;
    }

    private static float ReadControls(InputAction action, bool fromXRController)
    {
        if (action == null)
            return 0f;

        float value = 0f;
        foreach (var control in action.controls)
        {
            bool isXRController = control.device is XRController;
            if (isXRController == fromXRController && control is InputControl<float> floatControl)
                value = Mathf.Max(value, floatControl.ReadValue());
        }
        return value;
    }

    private void UpdateGear()
    {
        GearChangedThisFrame = false;

        bool reverse = IsReverse;
        if (_holdToReverseAction != null)
            reverse = _holdToReverseAction.IsPressed();
        if (_gearChangeAction != null && _gearChangeAction.WasPressedThisFrame())
            reverse = !reverse;
        if (_gearDriveAction != null && _gearDriveAction.WasPressedThisFrame())
            reverse = false;
        if (_gearReverseAction != null && _gearReverseAction.WasPressedThisFrame())
            reverse = true;

        if (reverse == IsReverse)
            return;

        IsReverse = reverse;
        GearChangedThisFrame = true;
        // shifting while latched would send the vehicle off at top speed the other way
        _throttleLatched = false;
    }
}
