using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using Unity.Cinemachine;

/// <summary>
/// Looping menu showreel: cycles a set of CinemachineCameras, driving each along its spline
/// and letting the brain blend to the next. Runs on unscaled time because the main menu holds
/// Time.timeScale at 0, so CinemachineCore.UniformDeltaTimeOverride is fed the unscaled delta
/// to keep dolly motion and blends advancing while everything else is frozen. Each camera aims
/// at its own LookAt target (assigned in the scene) for the whole shot.
/// In VR the brain is switched off and the rig is parented
/// to vrViewpoint instead, with the TrackedPoseDriver enabled so the headset can still look
/// around from that fixed spot.
/// </summary>
public class MenuShowreel : MonoBehaviour
{
    [System.Serializable]
    public class Shot
    {
        public CinemachineCamera camera;
        [Tooltip("Seconds this shot is on screen, blend included.")]
        public float duration = 12f;
    }

    public List<Shot> shots = new();
    [Tooltip("Priority given to the active shot; the others sit at 0.")]
    public int activePriority = 100;
    [Tooltip("Where the participant stands in VR, no flythrough. Put it at floor level, the headset adds its own eye height. Yaw only: pitch and roll tilt the whole view.")]
    public Transform vrViewpoint;

    private CinemachineBrain _brain;
    private TrackedPoseDriver _poseDriver;
    private CinemachineSplineDolly[] _dollies;
    private Transform _rigParent;
    private Vector3 _rigLocalPosition;
    private Quaternion _rigLocalRotation;
    private int _current;
    private float _elapsed;
    private bool _vrView;

    private void Awake()
    {
        // Where the rig belongs outside VR, so the VR reparent can be undone.
        _rigParent = transform.parent;
        _rigLocalPosition = transform.localPosition;
        _rigLocalRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        _brain = GetComponent<CinemachineBrain>();
        _poseDriver = GetComponent<TrackedPoseDriver>();
        _dollies = new CinemachineSplineDolly[shots.Count];
        for (int i = 0; i < shots.Count; i++)
            if (shots[i].camera != null)
                _dollies[i] = shots[i].camera.GetComponent<CinemachineSplineDolly>();

        _vrView = VrActive.IsActive;
        ApplyMode();
    }

    private void OnDisable()
    {
        // Hand normal timing back so gameplay cameras run on scaled time again.
        CinemachineCore.UniformDeltaTimeOverride = -1f;
    }

    private void Update()
    {
        // The headset can take a second or two to start running, so keep checking
        // instead of trusting the state at OnEnable (same reason MenuController polls).
        bool vr = VrActive.IsActive;
        if (vr != _vrView)
        {
            _vrView = vr;
            ApplyMode();
        }

        if (_vrView) return;
        if (shots.Count == 0) return;

        // Clamp so an editor/domain-reload frame hitch can't skip past a whole shot.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        CinemachineCore.UniformDeltaTimeOverride = dt;

        Shot shot = shots[_current];
        _elapsed += dt;

        if (_dollies[_current] != null && shot.duration > 0f)
            _dollies[_current].CameraPosition = Mathf.Clamp01(_elapsed / shot.duration);

        if (_elapsed >= shot.duration)
            Activate((_current + 1) % shots.Count);
    }

    // Switches between the flythrough and the still VR shot. The brain writes the rig
    // transform in LateUpdate, so it has to be off for the still shot to hold.
    private void ApplyMode()
    {
        if (_brain != null)
            _brain.enabled = !_vrView;
        if (_poseDriver != null)
            _poseDriver.enabled = _vrView;

        if (_vrView)
        {
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            if (_poseDriver == null)
                Debug.LogWarning("[MenuShowreel] No TrackedPoseDriver on the rig; the VR menu view is head locked.");

            // The driver writes the head pose into the local transform, so the rig has to hang
            // off the viewpoint for that pose to be relative to it instead of the world origin.
            if (vrViewpoint != null)
            {
                transform.SetParent(vrViewpoint, false);
                transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning("[MenuShowreel] No vrViewpoint assigned; the VR menu shows the rig's own position.");
            }
        }
        else
        {
            transform.SetParent(_rigParent, false);
            transform.SetLocalPositionAndRotation(_rigLocalPosition, _rigLocalRotation);
            if (shots.Count > 0)
                Activate(0);
        }
    }

    private void Activate(int index)
    {
        _current = index;
        _elapsed = 0f;
        for (int i = 0; i < shots.Count; i++)
            if (shots[i].camera != null)
                shots[i].camera.Priority = (i == index) ? activePriority : 0;
        if (_dollies[index] != null)
            _dollies[index].CameraPosition = 0f;
    }
}
