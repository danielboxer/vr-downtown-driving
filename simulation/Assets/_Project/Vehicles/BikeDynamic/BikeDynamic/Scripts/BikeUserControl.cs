using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityStandardAssets.Bike
{
    [RequireComponent(typeof(BikeController))]
    public class BikeUserControl : MonoBehaviour
    {
        private BikeController m_Bike; // the bike controller we want to use
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

        private void Awake()
        {
            // get the bike controller
            m_Bike = GetComponent<BikeController>();

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
            _steerInput = _steerAction?.ReadValue<float>() ?? 0f;
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
