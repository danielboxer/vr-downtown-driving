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

        private void Awake()
        {
            m_Bike = GetComponent<BikeController>();
            m_TiltSteering = GetComponent<TiltSteeringProvider>();
            m_FollowCurve = GetComponent<FollowCurve>();
            _rb = GetComponent<Rigidbody>();

            // both triggers are brake levers on the bike
            _driving = new DrivingInput(inputActions, actionMapName, accelerateUnlatches: true);
        }

        private void OnEnable()
        {
            _driving.Enable();
        }

        private void OnDisable()
        {
            _driving.Disable();
        }

        private void Update()
        {
            _driving.Update(m_TiltSteering, steerSmoothing, steerReturnSmoothing);
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

            m_Bike.Move(h, _driving.Throttle, -_driving.Brake, 0f, _driving.IsReverse);

            _driving.ApplyInstantSpeed(_rb, m_Bike.MaxSpeedMs, transform.forward);
        }
    }
}
