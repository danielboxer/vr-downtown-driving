using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityStandardAssets.Bike
{
    [RequireComponent(typeof(BikeController))]
    public class BikeUserControl : MonoBehaviour
    {
        private BikeController m_Bike; // the bike controller we want to use
        private TiltSteeringProvider m_TiltSteering; // Optional tilt-based steering
        public GameObject m_Wheel;

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

        // Cached input values (read in Update, used in FixedUpdate)
        private float _steerInput;
        private float _accelInput;
        private float _brakeInput;
        private float _handbrakeInput;
        private float _smoothedSteer; // Smoothed keyboard steering value

        [Tooltip("Smoothing speed for keyboard steering (higher = snappier).")]
        public float steerSmoothing = 5f; // Mimics old Input.GetAxis smoothing

        private void Awake()
        {
            // get the bike controller
            m_Bike = GetComponent<BikeController>();
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
                }
            }
        }

        private void OnEnable()
        {
            _steerAction?.Enable();
            _accelAction?.Enable();
            _brakeAction?.Enable();
            _handbrakeAction?.Enable();
        }

        private void OnDisable()
        {
            _steerAction?.Disable();
            _accelAction?.Disable();
            _brakeAction?.Disable();
            _handbrakeAction?.Disable();
        }

        private void Update()
        {
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
        }

        private void FixedUpdate()
        {
            float h = _steerInput;
            float accel = _accelInput;
            float brake = _brakeInput;
            float handbrake = _handbrakeInput;

            // Determine the target steering angle based on input
            float targetAngle = h * m_Bike.m_MaximumSteerAngle;

            // Apply the rotation to the wheel around the Y-axis
            m_Wheel.transform.localRotation = Quaternion.Euler(0f, targetAngle, 0f);

            // Pass the input to the bike controller
            m_Bike.Move(h, accel, -brake, handbrake);
        }
    }
}
