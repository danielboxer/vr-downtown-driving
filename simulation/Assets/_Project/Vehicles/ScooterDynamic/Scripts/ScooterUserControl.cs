using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityStandardAssets.Scooter
{
    [RequireComponent(typeof(ScooterController))]
    public class ScooterUserControl : MonoBehaviour
    {
        private ScooterController m_Scooter; // the scooter controller we want to use
        public GameObject m_Wheel;

        public float rotationSpeed = 5f; // Adjust for how quickly the wheel rotates to new angle
        private float currentAngle = 90f; // Starting angle
        public float maxSteeringAngle = 180f;

        [Header("Input Actions")]
        [Tooltip("Assign InputSystem_Actions asset with a Driving action map.")]
        public InputActionAsset inputActions;

        [Tooltip("Name of the action map containing driving actions.")]
        public string actionMapName = "Driving";

        private InputAction _steerAction;
        private InputAction _accelAction;
        private InputAction _brakeAction;
        private InputAction _handbrakeAction;

        private float _steerInput;
        private float _accelInput;
        private float _brakeInput;
        private float _handbrakeInput;

        private void Awake()
        {
            // get the scooter controller
            m_Scooter = GetComponent<ScooterController>();

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

            // Calculate target angle. For example, "90f + h * 180f" means:
            // - When h = 0, angle = 90d
            // - When h = 1, angle = 270d
            // - When h = -1, angle = -90d (which modulo 360 is 270d, but will interpolate smoothly)
            float targetAngle = 90f - h * maxSteeringAngle;

            // Smoothly interpolate the current angle towards the target angle
            currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * rotationSpeed);

            // Apply new rotation to the wheel
            // Rotation is around the local X-axis. If you need a different axis, adjust Euler accordingly.
            m_Wheel.transform.localRotation = Quaternion.Euler(currentAngle, -90f, 90f);

            m_Scooter.Move(h, accel, -brake, handbrake);
        }
    }
}
