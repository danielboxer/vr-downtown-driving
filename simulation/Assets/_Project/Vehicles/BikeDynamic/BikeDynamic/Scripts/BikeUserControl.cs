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
        private FollowCurve m_FollowCurve; // Steering influence blending (spline guide)
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
        private InputAction _reverseAction;

        // Cached input values (read in Update, used in FixedUpdate)
        private float _steerInput;
        private float _accelInput;  // throttle sent to BikeController (0-1)
        private float _brakeInput;
        private bool _reverseInput;
        private float _smoothedSteer; // Smoothed keyboard steering value

        // Linear speed ramp: velocity is capped to _speedRamp * MaxSpeed.
        // This produces a constant rate of speed increase (truly linear).
        private float _speedRamp;
        private Rigidbody _rb;

        // Auto-acceleration starts off and turns on the first time the rider presses a trigger.
        private bool _autoAccelActive;
        private bool _autoAccelNeedsRelease;
        private bool _autoAccelHasMoved;

        [Tooltip("Smoothing speed for keyboard steering (higher = snappier).")]
        public float steerSmoothing = 5f; // Mimics old Input.GetAxis smoothing

        [Header("Auto Speed")]
        [Tooltip("How fast (fraction of top speed per second) the allowed speed increases after activating. Lower = gentler ramp.")]
        public float autoAccelRamp = 0.1f;

        [Tooltip("Max rate (fraction of top speed per second) at which trigger braking decreases the cruise speed. Scales with trigger amount.")]
        public float autoDecelRamp = 0.5f;

        [Tooltip("Ignore small trigger noise below this value.")]
        [Range(0f, 0.2f)] public float triggerDeadzone = 0.05f;

        [Tooltip("Trigger value where slowing turns into active braking.")]
        [Range(0.05f, 1f)] public float brakeStart = 0.65f;

        [Tooltip("Trigger value counted as a full brake squeeze for disabling auto-accel at a stop.")]
        [Range(0.05f, 1f)] public float fullBrakeThreshold = 0.95f;

        [Tooltip("Speed in mph below which a full brake squeeze turns auto-accel off.")]
        public float stoppedSpeedThreshold = 0.5f;

        private void Awake()
        {
            // get the bike controller
            m_Bike = GetComponent<BikeController>();
            m_TiltSteering = GetComponent<TiltSteeringProvider>();
            m_FollowCurve = GetComponent<FollowCurve>();
            _rb = GetComponent<Rigidbody>();


            // Resolve actions from the asset by name
            if (inputActions != null)
            {
                var map = inputActions.FindActionMap(actionMapName, false);
                if (map != null)
                {
                    _steerAction = map.FindAction("Steer", false);
                    _accelAction = map.FindAction("Accelerate", false);
                    _brakeAction = map.FindAction("Brake", false);
                    _reverseAction = map.FindAction("BikeReverse", false);
                }
            }
        }

        private void OnEnable()
        {
            _steerAction?.Enable();
            _accelAction?.Enable();
            _brakeAction?.Enable();
            _reverseAction?.Enable();
            _autoAccelActive = false;
            _autoAccelNeedsRelease = false;
            _autoAccelHasMoved = false;
            _speedRamp = 0f;
            _accelInput = 0f;
            _brakeInput = 0f;
        }

        private void OnDisable()
        {
            _steerAction?.Disable();
            _accelAction?.Disable();
            _brakeAction?.Disable();
            _reverseAction?.Disable();
        }

        private void Update()
        {
            // Combine tilt and action input; whichever has more authority wins.
            float rawAction = _steerAction?.ReadValue<float>() ?? 0f;
            // Smooth keyboard input to mimic old Input.GetAxis ramp-up/down
            _smoothedSteer = Mathf.MoveTowards(_smoothedSteer, rawAction, steerSmoothing * Time.deltaTime);
            _steerInput = TiltSteeringProvider.CombineSteer(m_TiltSteering, _smoothedSteer);
            // Either trigger can wake auto-accel; once active, trigger amount slows/brakes.
            float rawTrigger = Mathf.Max(
                _brakeAction?.ReadValue<float>() ?? 0f,
                _accelAction?.ReadValue<float>() ?? 0f
            );

            if (_autoAccelNeedsRelease && rawTrigger <= triggerDeadzone)
                _autoAccelNeedsRelease = false;

            if (!_autoAccelActive && !_autoAccelNeedsRelease && rawTrigger >= triggerDeadzone)
            {
                _autoAccelActive = true;
                _autoAccelHasMoved = false;
            }

            float brake = rawTrigger >= brakeStart
                ? Mathf.InverseLerp(brakeStart, 1f, rawTrigger)
                : 0f;

            if (_autoAccelActive && m_Bike.CurrentSpeed > stoppedSpeedThreshold)
                _autoAccelHasMoved = true;

            if (_autoAccelActive && _autoAccelHasMoved && rawTrigger >= fullBrakeThreshold && m_Bike.CurrentSpeed <= stoppedSpeedThreshold)
            {
                _autoAccelActive = false;
                _autoAccelNeedsRelease = true;
                _autoAccelHasMoved = false;
                _speedRamp = 0f;
            }

            // Below brakeStart, the trigger lowers the cruise target instead of braking.
            float cruiseTarget = rawTrigger <= triggerDeadzone
                ? 1f
                : Mathf.Clamp01(1f - (rawTrigger / brakeStart));

            float target = brake > 0f ? 0f : cruiseTarget;
            float rate = target >= _speedRamp ? autoAccelRamp : autoDecelRamp * Mathf.Max(rawTrigger, triggerDeadzone);
            if (_autoAccelActive)
                _speedRamp = Mathf.MoveTowards(_speedRamp, target, rate * Time.deltaTime);

            _accelInput = (_autoAccelActive && brake <= 0f) ? cruiseTarget : 0f;
            _brakeInput = (_autoAccelActive || _autoAccelNeedsRelease) ? brake : 0f;
            _reverseInput = _reverseAction != null && _reverseAction.IsPressed();
        }

        private void FixedUpdate()
        {
            m_Bike.SetTopSpeedKmh(MaxSpeedSetting.Kmh);

            float h = _steerInput;
            float accel = _accelInput;
            float brake = _brakeInput;

            // If FollowCurve is attached and enabled, blend the player's raw
            // steering input with the spline-following autopilot value.
            if (m_FollowCurve != null && m_FollowCurve.enabled)
                h = m_FollowCurve.GetBlendedSteering(h, m_Bike.m_MaximumSteerAngle);

            // Determine the road-wheel steering angle based on input.
            float targetAngle = h * m_Bike.m_MaximumSteerAngle;

            // With the physical bike handlebar cradle active, keep the visible handlebar
            // matched to the user's cradle angle instead of the smaller road-wheel angle.
            float visualAngle = targetAngle;
            if (m_TiltSteering != null && m_TiltSteering.IsActive)
            {
                visualAngle = Mathf.Clamp(
                    m_TiltSteering.SteeringWheelAngle,
                    -m_TiltSteering.maxSteerAngle,
                    m_TiltSteering.maxSteerAngle
                );
            }

            // Apply the rotation to the wheel around the Y-axis
            m_Wheel.transform.localRotation = Quaternion.Euler(0f, visualAngle, 0f);

            // Pass the input to the bike controller
            m_Bike.Move(h, accel, -brake, 0f, _reverseInput);

            bool instant = AccelerationSetting.Mode == AccelerationMode.Instant;
            if (instant && !_reverseInput)
            {
                // Very fast (~100ms) ramp to max cruise or a stop, removing most of
                // the acceleration cue that causes sim sickness.
                bool throttle = accel > 0f && brake <= 0f;
                VrFastSpeed.Apply(_rb, throttle ? m_Bike.MaxSpeedMs : 0f, m_Bike.MaxSpeedMs, transform.forward);
            }
            else if (!instant && _autoAccelActive && _speedRamp < 0.99f)
            {
                // Cap velocity to the speed ramp fraction of top speed for linear speed control
                float capMs = _speedRamp * (m_Bike.MaxSpeed / 2.23693629f); // top speed in m/s
                if (_rb.linearVelocity.magnitude > capMs)
                    _rb.linearVelocity = _rb.linearVelocity.normalized * capMs;
            }
        }
    }
}
