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

        private DrivingInput _driving;
        private Rigidbody _rb;

        [Tooltip("Smoothing speed for keyboard steering (higher = snappier).")]
        public float steerSmoothing = 5f; // Mimics old Input.GetAxis smoothing

        [Tooltip("Rate the keyboard steering eases back to center after the key is released (lower = slower).")]
        public float steerReturnSmoothing = 2f;

        [Header("Brake Levers (VR triggers)")]
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

        private const float UncappedSpeedRamp = 0.99f;

        // capping velocity keeps the speed increase constant
        private float _speedRamp;
        private bool _autoAccelActive;
        private bool _autoAccelNeedsRelease;
        private bool _autoAccelHasMoved;
        private float _leverThrottle;
        private float _leverBrake;

        private void Awake()
        {
            m_Bike = GetComponent<BikeController>();
            m_TiltSteering = GetComponent<TiltSteeringProvider>();
            m_FollowCurve = GetComponent<FollowCurve>();
            _rb = GetComponent<Rigidbody>();

            // both triggers are brake levers on the bike
            _driving = new DrivingInput(inputActions, actionMapName, accelerateUnlatches: true, xrTriggersAreBrakeLevers: true, holdToReverse: true);
        }

        private void OnEnable()
        {
            _driving.Enable();
            _autoAccelActive = false;
            // the trigger pull that started the ride from the main menu is still held
            _autoAccelNeedsRelease = true;
            _autoAccelHasMoved = false;
            _speedRamp = 0f;
            _leverThrottle = 0f;
            _leverBrake = 0f;
        }

        private void OnDisable()
        {
            _driving.Disable();
        }

        private void Update()
        {
            _driving.Update(m_TiltSteering, steerSmoothing, steerReturnSmoothing);
            UpdateBrakeLevers(_driving.BrakeLeverPull);
        }

        private void UpdateBrakeLevers(float pull)
        {
            if (_autoAccelNeedsRelease && pull <= triggerDeadzone)
                _autoAccelNeedsRelease = false;

            if (!_autoAccelActive && !_autoAccelNeedsRelease && pull >= triggerDeadzone)
            {
                _autoAccelActive = true;
                _autoAccelHasMoved = false;
            }

            float brake = pull >= brakeStart ? Mathf.InverseLerp(brakeStart, 1f, pull) : 0f;

            if (_autoAccelActive && m_Bike.CurrentSpeed > stoppedSpeedThreshold)
                _autoAccelHasMoved = true;

            bool squeezedToStop = _autoAccelActive && _autoAccelHasMoved &&
                pull >= fullBrakeThreshold && m_Bike.CurrentSpeed <= stoppedSpeedThreshold;
            if (squeezedToStop)
            {
                _autoAccelActive = false;
                _autoAccelNeedsRelease = true;
                _autoAccelHasMoved = false;
                _speedRamp = 0f;
            }

            float cruiseTarget = pull <= triggerDeadzone ? 1f : Mathf.Clamp01(1f - pull / brakeStart);

            float target = brake > 0f ? 0f : cruiseTarget;
            float rate = target >= _speedRamp ? autoAccelRamp : autoDecelRamp * Mathf.Max(pull, triggerDeadzone);
            if (_autoAccelActive)
                _speedRamp = Mathf.MoveTowards(_speedRamp, target, rate * Time.deltaTime);

            _leverThrottle = _autoAccelActive && brake <= 0f ? cruiseTarget : 0f;
            _leverBrake = _autoAccelActive || _autoAccelNeedsRelease ? brake : 0f;
        }

        private void FixedUpdate()
        {
            m_Bike.SetTopSpeedKmh(MaxSpeedSetting.BikeKmh);

            // blend raw steering with the spline-following autopilot when FollowCurve is enabled
            float h = _driving.Steer;
            if (m_FollowCurve != null && m_FollowCurve.enabled)
                h = m_FollowCurve.GetBlendedSteering(h, m_Bike.m_MaximumSteerAngle);

            // with the physical cradle active, the visible handlebar matches the cradle, not the road-wheel angle
            float visualAngle = h * m_Bike.m_MaximumSteerAngle;
            if (m_TiltSteering != null && m_TiltSteering.IsActive)
            {
                visualAngle = Mathf.Clamp(
                    m_TiltSteering.SteeringWheelAngle,
                    -m_TiltSteering.maxSteerAngle,
                    m_TiltSteering.maxSteerAngle
                );
            }

            m_Wheel.transform.localRotation = Quaternion.Euler(0f, visualAngle, 0f);

            // holding reverse backs up without the throttle
            float throttle = _driving.IsReverse ? 1f : Mathf.Max(_driving.Throttle, _leverThrottle);
            float brake = Mathf.Max(_driving.Brake, _leverBrake);
            m_Bike.Move(h, throttle, -brake, 0f, _driving.IsReverse);

            _driving.ApplyInstantSpeed(_rb, m_Bike.MaxSpeedMs, transform.forward);
            ApplyBrakeLeverSpeed();
        }

        private void ApplyBrakeLeverSpeed()
        {
            if (!_autoAccelActive || _driving.IsReverse)
                return;

            if (AccelerationSetting.Mode == AccelerationMode.Instant)
            {
                // braking is left to the wheel brakes
                if (_leverBrake <= 0f)
                    VrFastSpeed.Apply(_rb, m_Bike.MaxSpeedMs, transform.forward);
                return;
            }

            if (_speedRamp >= UncappedSpeedRamp)
                return;

            float capMs = _speedRamp * m_Bike.MaxSpeedMs;
            if (_rb.linearVelocity.magnitude > capMs)
                _rb.linearVelocity = _rb.linearVelocity.normalized * capMs;
        }
    }
}
