using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityStandardAssets.Vehicles.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarUserControl : MonoBehaviour
    {
        private CarController m_Car; // The car controller we want to use
        private CarAudio m_CarAudio; // The car audio controller
        private FollowCurve m_FollowCurve; // Steering influence blending (spline guide)
        private TiltSteeringProvider m_TiltSteering; // Optional tilt-based steering
        private Rigidbody _rb;

        public GameObject m_Wheel; // The steering wheel GameObject

        [Header("Steering Settings")]

        [Tooltip("Speed at which the wheel rotates towards the target angle.")]
        public float rotationSpeed = 5f; // Speed at which the wheel rotates towards the target angle

        [Tooltip("Speed at which the wheel returns to center.")]
        public float returnSpeed = 5f; // Speed at which the wheel returns to center

        [Tooltip("Smoothing speed for keyboard steering (higher = snappier).")]
        public float steerSmoothing = 5f; // Mimics old Input.GetAxis smoothing

        [Tooltip("Rate the keyboard steering eases back to center after the key is released (lower = slower).")]
        public float steerReturnSmoothing = 2f;

        private float currentAngle = 0f; // Current angle of the wheel

        private bool isLeftSignalOn = false;
        private bool isRightSignalOn = false;

        [Header("Input Actions")]
        [Tooltip("Assign InputSystem_Actions asset with a Driving action map.")]
        public InputActionAsset inputActions;

        [Tooltip("Name of the action map containing driving actions.")]
        public string actionMapName = "Driving";

        private DrivingInput _driving;
        private InputAction _handbrakeAction;
        private InputAction _leftSignalAction;
        private InputAction _rightSignalAction;
        private InputAction _cancelSignalAction;
        private InputAction _hornAction;

        private float _handbrakeInput;

        public bool IsLeftSignalOn => isLeftSignalOn;
        public bool IsRightSignalOn => isRightSignalOn;

        public bool IsReverse => _driving.IsReverse;

        private void Awake()
        {
            m_Car = GetComponent<CarController>();
            m_CarAudio = GetComponent<CarAudio>();
            m_FollowCurve = GetComponent<FollowCurve>();
            m_TiltSteering = GetComponent<TiltSteeringProvider>();
            _rb = GetComponent<Rigidbody>();

            _driving = new DrivingInput(inputActions, actionMapName);

            var map = inputActions?.FindActionMap(actionMapName, false);
            if (map != null)
            {
                _handbrakeAction = map.FindAction("HandBrake", false);
                _leftSignalAction = map.FindAction("LeftSignal", false);
                _rightSignalAction = map.FindAction("RightSignal", false);
                _cancelSignalAction = map.FindAction("CancelSignal", false);
                _hornAction = map.FindAction("Horn", false);
            }
        }

        private void OnEnable()
        {
            _driving.Enable();
            _handbrakeAction?.Enable();
            _leftSignalAction?.Enable();
            _rightSignalAction?.Enable();
            _cancelSignalAction?.Enable();
            _hornAction?.Enable();
        }

        private void OnDisable()
        {
            _driving.Disable();
            _handbrakeAction?.Disable();
            _leftSignalAction?.Disable();
            _rightSignalAction?.Disable();
            _cancelSignalAction?.Disable();
            _hornAction?.Disable();
        }

        private void Update()
        {
            _driving.Update(m_TiltSteering, steerSmoothing, steerReturnSmoothing);
            _handbrakeInput = _handbrakeAction?.ReadValue<float>() ?? 0f;

            if (_leftSignalAction != null && _leftSignalAction.WasPressedThisFrame())
                ActivateTurnSignal(true, false);
            else if (_rightSignalAction != null && _rightSignalAction.WasPressedThisFrame())
                ActivateTurnSignal(false, true);
            else if (_cancelSignalAction != null && _cancelSignalAction.WasPressedThisFrame())
                DeactivateTurnSignals();

            // H key / right thumbstick click = player horn
            if (_hornAction != null && _hornAction.WasPressedThisFrame())
                m_CarAudio?.PlayHorn();

            if (_driving.GearChangedThisFrame)
                m_CarAudio?.PlayGearChange();
        }

        private void FixedUpdate()
        {
            m_Car.SetTopSpeedKmh(MaxSpeedSetting.CarKmh);

            // blend raw steering with the spline-following autopilot when FollowCurve is enabled
            float steeringInput = _driving.Steer;
            if (m_FollowCurve != null && m_FollowCurve.enabled)
            {
                steeringInput = m_FollowCurve.GetBlendedSteering(steeringInput, m_Car.m_MaximumSteerAngle);
            }

            // with the physical wheel active, the visible wheel matches the cradle, not the small road-wheel angle
            float targetAngle = steeringInput * m_Car.m_MaximumSteerAngle;
            if (m_TiltSteering != null && m_TiltSteering.IsActive)
            {
                currentAngle = m_TiltSteering.SteeringWheelAngle;
            }
            else if (Mathf.Abs(steeringInput) > 0.01f)
            {
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            }
            else
            {
                currentAngle = Mathf.LerpAngle(currentAngle, 0f, returnSpeed * Time.deltaTime);
            }

            m_Wheel.transform.localRotation = Quaternion.Euler(0f, 0f, -currentAngle);

            // CarController.Move clamps accel to [0,1] and footbrake to [-1,0]
            m_Car.Move(steeringInput, _driving.Throttle, -_driving.Brake, _handbrakeInput, _driving.IsReverse);

            _driving.ApplyInstantSpeed(_rb, m_Car.MaxSpeedMs, transform.forward);
        }

        private void ActivateTurnSignal(bool left, bool right)
        {
            isLeftSignalOn = left;
            isRightSignalOn = right;

            if (m_CarAudio != null)
            {
                // PlayTurnSignalOnSound handles the loop start after the click finishes
                m_CarAudio.PlayTurnSignalOnSound();
            }
        }

        public void DeactivateTurnSignals()
        {
            isLeftSignalOn = false;
            isRightSignalOn = false;

            if (m_CarAudio != null)
            {
                m_CarAudio.StopTurnSignalLoop();
            }
        }
    }
}
