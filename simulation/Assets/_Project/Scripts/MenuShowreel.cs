using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Looping menu showreel: cycles a set of CinemachineCameras, driving each along its spline
/// and letting the brain blend to the next. Runs on unscaled time because the main menu holds
/// Time.timeScale at 0, so CinemachineCore.UniformDeltaTimeOverride is fed the unscaled delta
/// to keep dolly motion and blends advancing while everything else is frozen. Each camera aims
/// at its own LookAt target (assigned in the scene) for the whole shot.
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

    private CinemachineSplineDolly[] _dollies;
    private int _current;
    private float _elapsed;

    private void OnEnable()
    {
        _dollies = new CinemachineSplineDolly[shots.Count];
        for (int i = 0; i < shots.Count; i++)
            if (shots[i].camera != null)
                _dollies[i] = shots[i].camera.GetComponent<CinemachineSplineDolly>();

        if (shots.Count > 0)
            Activate(0);
    }

    private void OnDisable()
    {
        // Hand normal timing back so gameplay cameras run on scaled time again.
        CinemachineCore.UniformDeltaTimeOverride = -1f;
    }

    private void Update()
    {
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
