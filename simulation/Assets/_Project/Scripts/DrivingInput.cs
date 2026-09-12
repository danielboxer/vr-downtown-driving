using UnityEngine;
using UnityEngine.InputSystem;

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

    private readonly bool _accelerateUnlatches;

    private float _smoothedSteer;
    private bool _throttleLatched;

    public float Steer { get; private set; }
    public float Throttle { get; private set; }
    public float Brake { get; private set; }
    public bool IsReverse { get; private set; }
    public bool GearChangedThisFrame { get; private set; }

    public DrivingInput(InputActionAsset inputActions, string actionMapName, bool accelerateUnlatches = false)
    {
        _accelerateUnlatches = accelerateUnlatches;

        var map = inputActions?.FindActionMap(actionMapName, false);
        if (map == null)
            return;

        _steerAction = map.FindAction("Steer", false);
        _accelerateAction = map.FindAction("Accelerate", false);
        _brakeAction = map.FindAction("Brake", false);
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

        _smoothedSteer = 0f;
        _throttleLatched = false;
        Steer = 0f;
        Throttle = 0f;
        Brake = 0f;
        IsReverse = false;
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
    }

    public void Update(TiltSteeringProvider tiltSteering, float steerSmoothing, float steerReturnSmoothing)
    {
        float rawSteer = _steerAction?.ReadValue<float>() ?? 0f;
        float steerRate = rawSteer != 0f ? steerSmoothing : steerReturnSmoothing;
        _smoothedSteer = Mathf.MoveTowards(_smoothedSteer, rawSteer, steerRate * Time.deltaTime);
        Steer = TiltSteeringProvider.CombineSteer(tiltSteering, _smoothedSteer);

        UpdateGear();

        Brake = _brakeAction?.ReadValue<float>() ?? 0f;
        float throttle = _accelerateAction?.ReadValue<float>() ?? 0f;

        if (AccelerationSetting.Mode == AccelerationMode.Instant)
        {
            bool acceleratePressed = _accelerateAction != null && _accelerateAction.WasPressedThisFrame();
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
        if (AccelerationSetting.Mode != AccelerationMode.Instant)
            return;

        Vector3 heading = IsReverse ? -forward : forward;
        VrFastSpeed.Apply(rigidbody, _throttleLatched ? maxSpeedMs : 0f, maxSpeedMs, heading);
    }

    private void UpdateGear()
    {
        GearChangedThisFrame = false;

        bool reverse = IsReverse;
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
