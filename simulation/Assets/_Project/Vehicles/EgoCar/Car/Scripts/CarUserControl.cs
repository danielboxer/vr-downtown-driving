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

        private float currentAngle = 0f; // Current angle of the wheel

        [Header("Turn Signal Settings")]
        public Light leftTurnSignal;
        public Light rightTurnSignal;
        public float blinkInterval = 0.5f;

        private bool isLeftSignalOn = false;
        private bool isRightSignalOn = false;
        private float signalTimer = 0f;

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

        // Cached input values (read in Update, used in FixedUpdate)
        private float _steerInput;
        private float _accelInput;
        private float _brakeInput;
        private float _handbrakeInput;

        /// <summary>Whether the left turn signal is currently active.</summary>
        public bool IsLeftSignalOn => isLeftSignalOn;
        /// <summary>Whether the right turn signal is currently active.</summary>
        public bool IsRightSignalOn => isRightSignalOn;

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
                }
            }
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
        }

        private void Update()
        {
            // Read continuous axes every frame (consumed in FixedUpdate)
            // Tilt steering overrides the action-based axis when available
            _steerInput = (m_TiltSteering != null && m_TiltSteering.enabled)
                ? m_TiltSteering.SteerValue
                : _steerAction?.ReadValue<float>() ?? 0f;
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

            // Determine the target steering angle based on blended input
            float targetAngle = steeringInput * m_Car.m_MaximumSteerAngle;

            if (Mathf.Abs(steeringInput) > 0.01f)
            {
                // Smoothly rotate the wheel towards the target angle
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            }
            else
            {
                // Smoothly return the wheel to the center
                currentAngle = Mathf.LerpAngle(currentAngle, 0f, returnSpeed * Time.deltaTime);
            }

            // Apply the rotation to the wheel around the Z-axis
            m_Wheel.transform.localRotation = Quaternion.Euler(0f, 0f, -currentAngle);

            // Pass accel and brake separately to CarController
            // CarController.Move clamps accel to [0,1] and footbrake to [-1,0]
            m_Car.Move(steeringInput, accel, -brake, handbrake);

            // Handle turn signal blinking
            signalTimer += Time.deltaTime;
            if (isLeftSignalOn && signalTimer >= blinkInterval)
            {
                leftTurnSignal.enabled = !leftTurnSignal.enabled;
                signalTimer = 0f;
            }

            if (isRightSignalOn && signalTimer >= blinkInterval)
            {
                rightTurnSignal.enabled = !rightTurnSignal.enabled;
                signalTimer = 0f;
            }
        }

        private void ActivateTurnSignal(bool left, bool right)
        {
            isLeftSignalOn = left;
            isRightSignalOn = right;

            leftTurnSignal.enabled = left;
            rightTurnSignal.enabled = right;

            if (m_CarAudio != null)
            {
                m_CarAudio.PlayTurnSignalOnSound();
                m_CarAudio.PlayTurnSignalLoop();
            }
        }

        public void DeactivateTurnSignals()
        {
            isLeftSignalOn = false;
            isRightSignalOn = false;

            leftTurnSignal.enabled = false;
            rightTurnSignal.enabled = false;

            if (m_CarAudio != null)
            {
                m_CarAudio.StopTurnSignalLoop();
            }
        }
    }
}
