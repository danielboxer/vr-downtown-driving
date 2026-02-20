using UnityEngine;

namespace UnityStandardAssets.Vehicles.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarUserControl : MonoBehaviour
    {
        private CarController m_Car; // The car controller we want to use
        private CarAudio m_CarAudio; // The car audio controller

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

        private void Awake()
        {
            // Get the CarController and CarAudio components
            m_Car = GetComponent<CarController>();
            m_CarAudio = GetComponent<CarAudio>();
        }

        private void FixedUpdate()
        {
            // Get input
            float h = Input.GetAxis("Horizontal"); // Horizontal input for steering
            float v = Input.GetAxis("Vertical");   // Vertical input for acceleration/braking

            // Determine the target steering angle based on input
            float targetAngle = h * m_Car.m_MaximumSteerAngle;

            if (Mathf.Abs(h) > 0.01f)
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

            // Get the handbrake input
            float handbrake = Input.GetAxis("Jump"); // Typically mapped to the spacebar

            // Pass the input to the car controller
            m_Car.Move(h, v, v, handbrake);

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

        private void Update()
        {
            // Turn signal input
            if (Input.GetKeyDown(KeyCode.Q)) // Left turn signal
            {
                ActivateTurnSignal(true, false);
            }
            else if (Input.GetKeyDown(KeyCode.E)) // Right turn signal
            {
                ActivateTurnSignal(false, true);
            }
            else if (Input.GetKeyDown(KeyCode.C)) // Cancel turn signals
            {
                DeactivateTurnSignals();
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

        private void DeactivateTurnSignals()
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
