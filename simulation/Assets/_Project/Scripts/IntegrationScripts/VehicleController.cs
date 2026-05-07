using System.Collections.Generic;
using UnityEngine;

public class VehicleController : MonoBehaviour
{
    private Rigidbody rb;

    private Vector3 lastPos;
    private Quaternion lastRot;
    private Vector3 curPos;
    private Quaternion curRot;
    private float lastTime;
    private float curTime;

    private float curLong, curVert, curLat;

    /// <summary>True after a collision detaches this vehicle from SUMO control.</summary>
    public bool IsDetached { get; private set; }

    /// <summary>Estimated world-space velocity used by wheel animation and crash handoff.</summary>
    public Vector3 EstimatedVelocity { get; private set; }

    /// <summary>SUMO-reported forward speed for this vehicle.</summary>
    public float CurrentLongitudinalSpeed => curLong;

    /// <summary>Whether this vehicle is currently close enough for high-detail behaviours.</summary>
    public bool IsHighDetail { get; private set; } = true;

    // ── Horn audio (fallback when no ScriptableObject is assigned) ──
    [HideInInspector] public List<AudioClip> hornClips = new List<AudioClip>();
    [HideInInspector] public float hornVolume = 1f;
    [HideInInspector] public float hornTriggerDelay = 3f;
    [HideInInspector] public float hornCooldown = 5f;
    [HideInInspector] public float hornTriggerDistance = 18f;
    [HideInInspector] public float hornHonkChance = 0.4f;
    [HideInInspector] public float hornAmbientChance = 0.1f;

    [Header("NPC Performance")]
    [SerializeField] private float highDetailDistance = 45f;
    [SerializeField] private float hornCheckInterval = 0.5f;
    [SerializeField] private float movementSharpness = 14f;
    [SerializeField] private float rotationSharpness = 14f;

    private float highDetailDistanceSqr = 45f * 45f;

    private AudioSource _hornSource;
    private float _stoppedTimer;
    private float _lastHornTime = -99f;
    private Transform _egoTransform;
    private SimulationController _simController;
    private bool _wasAtRedLight;
    private Collider[] _colliders;
    private bool _collidersEnabled = true;
    private float _nextDetailCheckTime;
    private float _nextHornCheckTime;
    private bool _egoTransformResolved;

    private const float DetailCheckInterval = 0.25f;

    private void Awake()
    {
        EnsureComponents();
        ResolveSimulationController();
        ConfigureAsSumoControlled();

        curPos = lastPos = transform.position;
        curRot = lastRot = transform.rotation;
        lastTime = curTime = Time.time;
    }

    private void Start()
    {
        // Re-apply once Start runs so inspector-modified Rigidbody values do not
        // leave NPC traffic as expensive dynamic bodies.
        if (!IsDetached)
            ConfigureAsSumoControlled();
    }

    /// <summary>
    /// Assigns a shared ScriptableObject config. VehicleController reads values
    /// from the config at runtime instead of per-instance field copies.
    /// </summary>
    public void SetConfig(NpcVehicleConfig config)
    {
        if (config == null) return;

        highDetailDistance = config.highDetailDistance;
        highDetailDistanceSqr = config.highDetailDistanceSqr;
        hornCheckInterval = config.hornCheckInterval;
        movementSharpness = config.movementSharpness;
        rotationSharpness = config.rotationSharpness;

        hornClips = config.hornClips;
        hornVolume = config.hornVolume;
        hornTriggerDelay = config.hornTriggerDelay;
        hornCooldown = config.hornCooldown;
        hornTriggerDistance = config.hornTriggerDistance;
        hornHonkChance = config.hornHonkChance;
        hornAmbientChance = config.hornAmbientChance;
    }

    /// <summary>
    /// Allows SimulationController to push one central set of performance settings
    /// to pooled/spawned NPCs without requiring every prefab to be edited.
    /// </summary>
    public void ConfigurePerformance(
        float npcHighDetailDistance,
        float npcHornCheckInterval,
        float npcMovementSharpness,
        float npcRotationSharpness)
    {
        highDetailDistance = Mathf.Max(0f, npcHighDetailDistance);
        highDetailDistanceSqr = highDetailDistance * highDetailDistance;
        hornCheckInterval = Mathf.Max(0.05f, npcHornCheckInterval);
        movementSharpness = Mathf.Max(1f, npcMovementSharpness);
        rotationSharpness = Mathf.Max(1f, npcRotationSharpness);
    }

    /// <summary>
    /// Resets this instance when it is borrowed from the pool or first spawned.
    /// </summary>
    public void ResetForSumoControl(Vector3 pos, Quaternion rot,
                                    float longSpd, float vertSpd, float latSpd)
    {
        EnsureComponents();
        ResolveSimulationController();

        IsDetached = false;
        ConfigureAsSumoControlled();

        transform.SetPositionAndRotation(pos, rot);
        rb.position = pos;
        rb.rotation = rot;

        curPos = lastPos = pos;
        curRot = lastRot = rot;
        curTime = lastTime = Time.time;
        curLong = longSpd;
        curVert = vertSpd;
        curLat = latSpd;
        EstimatedVelocity = rot * (Vector3.right * longSpd) + Vector3.up * vertSpd + rot * (Vector3.forward * latSpd);

        _stoppedTimer = 0f;
        _lastHornTime = -99f;
        _wasAtRedLight = false;
        _nextDetailCheckTime = 0f;
        _nextHornCheckTime = Time.time + Random.Range(0f, hornCheckInterval);
        _egoTransformResolved = false;

        UpdateDetailState(force: true);
    }

    /// <summary>
    /// Detach this NPC from SUMO control and apply a collision impulse.
    /// After this call the vehicle becomes a normal physics object.
    /// </summary>
    public void Detach(Vector3 impactImpulse, Vector3 contactPoint)
    {
        if (IsDetached) return;
        EnsureComponents();

        IsDetached = true;
        SetColliderState(true);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.linearVelocity = EstimatedVelocity;

        // Cap by resulting velocity (not impulse magnitude) to handle
        // low-mass Rigidbodies that would otherwise reach extreme speeds.
        const float maxPostCollisionSpeed = 8f;
        float resultingSpeed = impactImpulse.magnitude / Mathf.Max(rb.mass, 0.01f);
        if (resultingSpeed > maxPostCollisionSpeed)
            impactImpulse = impactImpulse.normalized * maxPostCollisionSpeed * rb.mass;

        rb.AddForceAtPosition(impactImpulse, contactPoint, ForceMode.Impulse);
    }

    public void UpdateTarget(Vector3 pos, Quaternion rot,
                             float longSpd, float vertSpd, float latSpd)
    {
        lastPos = curPos;
        lastRot = curRot;
        lastTime = curTime;

        curPos = pos;
        curRot = rot;
        curTime = Time.time;

        curLong = longSpd;
        curVert = vertSpd;
        curLat = latSpd;

        float dt = curTime - lastTime;
        EstimatedVelocity = dt > 0.0001f
            ? (curPos - lastPos) / dt
            : rot * (Vector3.right * longSpd) + Vector3.up * vertSpd + rot * (Vector3.forward * latSpd);
    }

    private void FixedUpdate()
    {
        if (IsDetached) return;

        MoveKinematicToLatestSumoTarget();
        UpdateDetailState(force: false);

        if (Time.time >= _nextHornCheckTime)
        {
            _nextHornCheckTime = Time.time + hornCheckInterval + Random.Range(0f, hornCheckInterval * 0.2f);
            CheckHorn();
        }
    }

    private void MoveKinematicToLatestSumoTarget()
    {
        float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float posBlend = 1f - Mathf.Exp(-movementSharpness * dt);
        float rotBlend = 1f - Mathf.Exp(-rotationSharpness * dt);

        Vector3 previousPos = rb.position;
        Vector3 nextPos = Vector3.Lerp(previousPos, curPos, posBlend);
        Quaternion nextRot = Quaternion.Slerp(rb.rotation, curRot, rotBlend);

        EstimatedVelocity = (nextPos - previousPos) / dt;
        rb.MovePosition(nextPos);
        rb.MoveRotation(nextRot);
    }

    private void ConfigureAsSumoControlled()
    {
        EnsureComponents();

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    private void EnsureComponents()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider>(true);
    }

    private void ResolveSimulationController()
    {
        if (_simController == null)
            _simController = FindFirstObjectByType<SimulationController>();

        if (!_egoTransformResolved && _simController != null && _simController.egoVehicle != null)
        {
            _egoTransform = _simController.egoVehicle.transform;
            _egoTransformResolved = true;
        }
    }

    private void UpdateDetailState(bool force)
    {
        if (!force && Time.time < _nextDetailCheckTime) return;
        _nextDetailCheckTime = Time.time + DetailCheckInterval;

        if (!_egoTransformResolved)
            ResolveSimulationController();

        if (_egoTransform == null)
        {
            IsHighDetail = true;
            return;
        }

        float sqrDist = (_egoTransform.position - transform.position).sqrMagnitude;
        IsHighDetail = sqrDist <= highDetailDistanceSqr;
    }

    private void SetColliderState(bool enabled)
    {
        if (_colliders == null) return;
        if (_collidersEnabled == enabled) return;

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = enabled;
        }

        _collidersEnabled = enabled;
    }

    private void CheckHorn()
    {
        if (hornClips == null || hornClips.Count == 0) return;

        ResolveSimulationController();

        // Skip all horn logic for NPCs too far from the ego to be heard.
        const float maxHornCheckDistSqr = 30f * 30f;
        if (_egoTransform != null &&
            (_egoTransform.position - transform.position).sqrMagnitude > maxHornCheckDistSqr)
        {
            _stoppedTimer = 0f;
            return;
        }

        // Track consecutive stopped time from SUMO commanded speed.
        const float stoppedThreshold = 0.5f;
        if (curLong < stoppedThreshold)
            _stoppedTimer += hornCheckInterval;
        else
            _stoppedTimer = 0f;

        if (_stoppedTimer < hornTriggerDelay) return;
        if (Time.time - _lastHornTime < hornCooldown) return;

        bool egoInFront = false;
        if (_egoTransform != null)
        {
            Vector3 toEgo = _egoTransform.position - transform.position;
            float triggerSqr = hornTriggerDistance * hornTriggerDistance;
            if (toEgo.sqrMagnitude <= triggerSqr)
            {
                Vector3 dirToEgo = toEgo.sqrMagnitude > 0.0001f ? toEgo.normalized : Vector3.zero;
                egoInFront = Vector3.Dot(transform.right, dirToEgo) >= 0.3f;
            }
        }

        // Filter before the expensive red-light query. Ambient horns remain possible,
        // but only a small fraction of far stopped cars perform the stop-line check.
        float roll = Random.value;
        if (egoInFront)
        {
            if (roll > hornHonkChance) return;
        }
        else
        {
            if (hornAmbientChance <= 0f || roll > hornAmbientChance) return;
        }

        // Don't honk when legitimately waiting at a red or yellow light.
        bool atRed = IsWaitingAtRedLight();
        if (atRed) { _wasAtRedLight = true; return; }

        // Light just turned green: reset stopped timer so they don't all honk at once.
        if (_wasAtRedLight)
        {
            _wasAtRedLight = false;
            _stoppedTimer = 0f;
            return;
        }

        EnsureHornSource();
        AudioClip clip = hornClips[Random.Range(0, hornClips.Count)];
        if (clip != null)
            _hornSource.PlayOneShot(clip, hornVolume);
        _lastHornTime = Time.time;
    }

    private void EnsureHornSource()
    {
        if (_hornSource != null) return;

        _hornSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _hornSource.playOnAwake = false;
        _hornSource.loop = false;
        _hornSource.spatialBlend = 1f;
        _hornSource.rolloffMode = AudioRolloffMode.Linear;
        _hornSource.maxDistance = 50f;
    }

    private bool IsWaitingAtRedLight()
    {
        if (_simController == null) return false;

        Vector3 npcDriveDir = curRot * Vector3.right;
        return _simController.IsNpcWaitingAtRedOrYellowLight(transform.position, npcDriveDir, 50f);
    }

}
