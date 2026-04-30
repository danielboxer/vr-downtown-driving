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

        public GameObject m_Wheel; // The steering wheel GameObject

        [Header("Steering Settings")]

        [Tooltip("Speed at which the wheel rotates towards the target angle.")]
        public float rotationSpeed = 5f; // Speed at which the wheel rotates towards the target angle

        [Tooltip("Speed at which the wheel returns to center.")]
        public float returnSpeed = 5f; // Speed at which the wheel returns to center

        [Tooltip("Smoothing speed for keyboard steering (higher = snappier).")]
        public float steerSmoothing = 5f; // Mimics old Input.GetAxis smoothing

        private float currentAngle = 0f; // Current angle of the wheel
        private float _smoothedSteer; // Smoothed keyboard steering value

        private bool isLeftSignalOn = false;
        private bool isRightSignalOn = false;

        [Header("Input Actions")]
        [Tooltip("Assign InputSystem_Actions asset with a Driving action map.")]
        public InputActionAsset inputActions;

        [Tooltip("Name of the action map containing driving actions.")]
        public string actionMapName = "Driving";

        // Resolved actions (looked up by name from the asset)
        private InputAction _steerAction;
        private InputAction _accelAction;
        private InputAction _brakeAction;
        private InputAction _handbrakeAction;
        private InputAction _leftSignalAction;
        private InputAction _rightSignalAction;
        private InputAction _cancelSignalAction;
        private InputAction _hornAction;
        private InputAction _gearChangeAction;
        private InputAction _gearDriveAction;   // XR: right thumbstick up → Drive
        private InputAction _gearReverseAction; // XR: right thumbstick down → Reverse

        // Whether the car is currently in reverse gear (toggled by GearChange action)
        private bool _isReverse;

        [Header("Gear Change Audio")]
        [Tooltip("Assign the gear-change clip on the CarAudio component instead.")]
        // Gear change audio is routed through CarAudio.PlayGearChange() for consistency.

        // Cached input values (read in Update, used in FixedUpdate)
        private float _steerInput;
        private float _accelInput;
        private float _brakeInput;
        private float _handbrakeInput;

        /// <summary>Whether the left turn signal is currently active.</summary>
        public bool IsLeftSignalOn => isLeftSignalOn;
        /// <summary>Whether the right turn signal is currently active.</summary>
        public bool IsRightSignalOn => isRightSignalOn;

        /// <summary>Whether the car is currently in reverse gear.</summary>
        public bool IsReverse => _isReverse;

        private void Awake()
        {
            // Get the CarController and CarAudio components
            m_Car = GetComponent<CarController>();
            m_CarAudio = GetComponent<CarAudio>();
            m_FollowCurve = GetComponent<FollowCurve>();
            m_TiltSteering = GetComponent<TiltSteeringProvider>();

            // Resolve actions from the asset by name
            if (inputActions != null)
            {
                var map = inputActions.FindActionMap(actionMapName, false);
                if (map != null)
                {
                    _steerAction = map.FindAction("Steer", false);
                    _accelAction = map.FindAction("Accelerate", false);
                    _brakeAction = map.FindAction("Brake", false);
                    _handbrakeAction = map.FindAction("HandBrake", false);
                    _leftSignalAction = map.FindAction("LeftSignal", false);
                    _rightSignalAction = map.FindAction("RightSignal", false);
                    _cancelSignalAction = map.FindAction("CancelSignal", false);
                    _hornAction = map.FindAction("Horn", false);
                    _gearChangeAction = map.FindAction("GearChange", false);
                    _gearDriveAction = map.FindAction("GearDrive", false);
                    _gearReverseAction = map.FindAction("GearReverse", false);
                }
            }

            // Dedicated audio source for gear-change sounds
            // (sound is played through CarAudio.PlayGearChange)
        }

        private void OnEnable()
        {
            _steerAction?.Enable();
            _accelAction?.Enable();
            _brakeAction?.Enable();
            _handbrakeAction?.Enable();
            _leftSignalAction?.Enable();
            _rightSignalAction?.Enable();
            _cancelSignalAction?.Enable();
            _hornAction?.Enable();
            _gearChangeAction?.Enable();
            _gearDriveAction?.Enable();
            _gearReverseAction?.Enable();
        }

        private void OnDisable()
        {
            _steerAction?.Disable();
            _accelAction?.Disable();
            _brakeAction?.Disable();
            _handbrakeAction?.Disable();
            _leftSignalAction?.Disable();
            _rightSignalAction?.Disable();
            _cancelSignalAction?.Disable();
            _hornAction?.Disable();
            _gearChangeAction?.Disable();
            _gearDriveAction?.Disable();
            _gearReverseAction?.Disable();
        }

        private void Update()
        {
            // Read continuous axes every frame (consumed in FixedUpdate)
            // Combine tilt and action input — whichever has more authority wins
            float rawAction = _steerAction?.ReadValue<float>() ?? 0f;
            // Smooth keyboard input to mimic old Input.GetAxis ramp-up/down
            _smoothedSteer = Mathf.MoveTowards(_smoothedSteer, rawAction, steerSmoothing * Time.deltaTime);
            float actionSteer = _smoothedSteer;
            if (m_TiltSteering != null && m_TiltSteering.enabled && m_TiltSteering.HasController)
            {
                float tilt = m_TiltSteering.SteerValue;
                _steerInput = Mathf.Abs(tilt) > Mathf.Abs(actionSteer) ? tilt : actionSteer;
            }
            else
            {
                _steerInput = actionSteer;
            }
            _accelInput = _accelAction?.ReadValue<float>() ?? 0f;
            _brakeInput = _brakeAction?.ReadValue<float>() ?? 0f;
            _handbrakeInput = _handbrakeAction?.ReadValue<float>() ?? 0f;

            // Turn signal button presses
            if (_leftSignalAction != null && _leftSignalAction.WasPressedThisFrame())
                ActivateTurnSignal(true, false);
            else if (_rightSignalAction != null && _rightSignalAction.WasPressedThisFrame())
                ActivateTurnSignal(false, true);
            else if (_cancelSignalAction != null && _cancelSignalAction.WasPressedThisFrame())
                DeactivateTurnSignals();

            // H key / right thumbstick click = player horn
            if (_hornAction != null && _hornAction.WasPressedThisFrame())
                m_CarAudio?.PlayHorn();

            // G key = toggle drive/reverse gear
            if (_gearChangeAction != null && _gearChangeAction.WasPressedThisFrame())
            {
                _isReverse = !_isReverse;
                m_CarAudio?.PlayGearChange();
                Debug.Log($"[CarUserControl] Gear: {(_isReverse ? "Reverse" : "Drive")}");
            }

            // Right thumbstick up/down = direct gear selection (XR)
            if (_gearDriveAction != null && _gearDriveAction.WasPressedThisFrame())
            {
                if (_isReverse)
                {
                    _isReverse = false;
                    m_CarAudio?.PlayGearChange();
                    Debug.Log("[CarUserControl] Gear: Drive");
                }
            }
            if (_gearReverseAction != null && _gearReverseAction.WasPressedThisFrame())
            {
                if (!_isReverse)
                {
                    _isReverse = true;
                    m_CarAudio?.PlayGearChange();
                    Debug.Log("[CarUserControl] Gear: Reverse");
                }
            }
        }

        private void FixedUpdate()
        {
            float h = _steerInput;
            float accel = _accelInput;
            float brake = _brakeInput;
            float handbrake = _handbrakeInput;

            // ── Steering Influence Blending ──
            // If FollowCurve is attached and enabled, blend the player's raw
            // steering with the spline-following autopilot.
            float steeringInput = h;
            if (m_FollowCurve != null && m_FollowCurve.enabled)
            {
                steeringInput = m_FollowCurve.GetBlendedSteering(h, m_Car.m_MaximumSteerAngle);
            }

            // Determine the road-wheel steering angle based on blended input.
            // With the physical controller wheel active, keep the visible steering wheel
            // matched to the user's cradle rotation instead of the small road-wheel angle.
            float targetAngle = steeringInput * m_Car.m_MaximumSteerAngle;
            if (m_TiltSteering != null && m_TiltSteering.enabled && m_TiltSteering.HasController)
            {
                currentAngle = m_TiltSteering.SteeringWheelAngle;
            }
            else if (Mathf.Abs(steeringInput) > 0.01f)
            {
                // Smoothly rotate the wheel towards the target angle for keyboard/gamepad input.
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            }
            else
            {
                // Smoothly return the wheel to the center for keyboard/gamepad input.
                currentAngle = Mathf.LerpAngle(currentAngle, 0f, returnSpeed * Time.deltaTime);
            }

            // Apply the rotation to the wheel around the Z-axis.
            m_Wheel.transform.localRotation = Quaternion.Euler(0f, 0f, -currentAngle);

            // Pass accel and brake separately to CarController
            // CarController.Move clamps accel to [0,1] and footbrake to [-1,0]
            m_Car.Move(steeringInput, accel, -brake, handbrake, _isReverse);

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
