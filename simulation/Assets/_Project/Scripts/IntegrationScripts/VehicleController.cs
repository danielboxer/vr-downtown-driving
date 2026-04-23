using UnityEngine;
using System.Collections.Generic;

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
    // set at runtime, after the Inspector value is known
    private float stepLen;
    private float turnThresholdDeg;

    private const float FadeTime = 0.05f;          // how long to ease out spin

    private Vector3 residualAngularVel;           // ★ keeps turn’s leftover spin
    private float residualTimer;                // ★ fade-out countdown

    /// <summary>True after a collision detaches this vehicle from SUMO control.</summary>
    public bool IsDetached { get; private set; }

    // ── Horn audio ──
    [HideInInspector] public List<AudioClip> hornClips = new List<AudioClip>();
    [HideInInspector] public float hornVolume = 1f;
    [HideInInspector] public float hornTriggerDelay = 3f;   // seconds stopped before honking
    [HideInInspector] public float hornCooldown = 5f;       // min seconds between honks
    [HideInInspector] public float hornTriggerDistance = 18f; // max metres to ego car
    [HideInInspector] public float hornHonkChance = 0.4f;   // probability per cooldown window
    [HideInInspector] public float hornAmbientChance = 0.1f; // probability when ego not nearby

    private AudioSource _hornSource;
    private float _stoppedTimer;
    private float _lastHornTime = -99f;
    private Transform _egoTransform;

    /// <summary>
    /// Detach this NPC from SUMO control and apply a collision impulse.
    /// After this call the vehicle becomes a normal physics object.
    /// </summary>
    public void Detach(Vector3 impactImpulse, Vector3 contactPoint)
    {
        if (IsDetached) return;
        IsDetached = true;

        rb.useGravity = true;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;

        // Cap by resulting velocity (not impulse magnitude) to handle
        // low-mass Rigidbodies that would otherwise reach extreme speeds
        const float maxPostCollisionSpeed = 8f;
        float resultingSpeed = impactImpulse.magnitude / Mathf.Max(rb.mass, 0.01f);
        if (resultingSpeed > maxPostCollisionSpeed)
            impactImpulse = impactImpulse.normalized * maxPostCollisionSpeed * rb.mass;

        rb.AddForceAtPosition(impactImpulse, contactPoint, ForceMode.Impulse);
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        curPos = lastPos = transform.position;
        curRot = lastRot = transform.rotation;
        lastTime = curTime = Time.time;

        // Dedicated audio source for the horn
        _hornSource = gameObject.AddComponent<AudioSource>();
        _hornSource.playOnAwake = false;
        _hornSource.loop = false;
        _hornSource.spatialBlend = 1f;
        _hornSource.rolloffMode = AudioRolloffMode.Linear;
        _hornSource.maxDistance = 50f;
    }

    public void UpdateTarget(Vector3 pos, Quaternion rot,
                             float longSpd, float vertSpd, float latSpd)
    {
        lastPos = curPos; lastRot = curRot; lastTime = curTime;
        curPos = pos; curRot = rot; curTime = Time.time;

        curLong = longSpd; curVert = vertSpd; curLat = latSpd;
    }
    void Awake()
    {
        // Look for the first SimulationController in the scene
        SimulationController sim = FindFirstObjectByType<SimulationController>();

        if (sim == null)
        {
            Debug.LogError("SimulationController not found!");
            return;
        }

        stepLen = sim.unityStepLength;                 // ← value set in Inspector
        turnThresholdDeg = Mathf.Clamp(stepLen * 40f, 0.25f, 10f);

        // Cache ego vehicle for proximity checks (available after RegisterEgoVehicle is called)
        if (sim.egoVehicle != null)
            _egoTransform = sim.egoVehicle.transform;
    }
    private void FixedUpdate()
    {
        // Detached vehicles are pure physics objects, no SUMO control
        if (IsDetached) return;

        float dt = curTime - lastTime;
        if (dt <= 0f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(curRot);
            return;
        }

        float headingDelta = Quaternion.Angle(lastRot, curRot);   // degrees

        if (headingDelta < turnThresholdDeg)                      // straight
        {
            /* linear vel from local-axis speeds (ultra smooth) */
            Vector3 vLong = curRot * (Vector3.right * curLong);
            Vector3 vLat = curRot * (Vector3.forward * curLat);
            Vector3 vUp = Vector3.up * curVert;
            rb.linearVelocity = vLong + vLat + vUp;

            /* damp residual spin, don't kill instantly */
            if (residualTimer > 0f)
            {
                residualTimer -= Time.fixedDeltaTime;
                float k = Mathf.Clamp01(residualTimer / FadeTime);
                rb.angularVelocity = residualAngularVel * k;
                if (k <= 0f) rb.MoveRotation(curRot);            // fully aligned
            }
            else
            {
                rb.angularVelocity = Vector3.zero;
                rb.MoveRotation(curRot);
            }
        }
        else                                                      // turning
        {
            rb.linearVelocity = (curPos - lastPos) / dt;
            residualAngularVel = CalcAngularVel(lastRot, curRot, dt);
            rb.angularVelocity = residualAngularVel;
            residualTimer = FadeTime;
        }

        /* original ultra-smooth positional blend */
        transform.localPosition =
            Vector3.Lerp(transform.localPosition, curPos, 0.02f);

        CheckHorn();
    }

    private void CheckHorn()
    {
        if (hornClips == null || hornClips.Count == 0 || _hornSource == null) return;

        // Lazily resolve ego transform in case RegisterEgoVehicle ran after Awake
        if (_egoTransform == null)
        {
            SimulationController sim = FindFirstObjectByType<SimulationController>();
            if (sim != null && sim.egoVehicle != null)
                _egoTransform = sim.egoVehicle.transform;
        }

        // Ego not found yet — still allow ambient honking
        // Track consecutive stopped time from SUMO commanded speed
        const float stoppedThreshold = 0.5f;
        if (curLong < stoppedThreshold)
            _stoppedTimer += Time.fixedDeltaTime;
        else
            _stoppedTimer = 0f;

        if (_stoppedTimer < hornTriggerDelay) return;
        if (Time.time - _lastHornTime < hornCooldown) return;

        // Only honk at ego-proximity chance; otherwise try the lower ambient chance
        Vector3 toEgo = _egoTransform != null
            ? _egoTransform.position - transform.position
            : Vector3.one * float.MaxValue;
        float dist = toEgo.magnitude;
        bool egoInFront = dist <= hornTriggerDistance
            && Vector3.Dot(transform.forward, toEgo.normalized) >= 0.3f;

        float roll = Random.value;
        if (egoInFront)
        {
            if (roll > hornHonkChance) return;
        }
        else
        {
            // General traffic impatience honk, less frequent
            if (roll > hornAmbientChance) return;
        }

        AudioClip clip = hornClips[Random.Range(0, hornClips.Count)];
        if (clip != null)
            _hornSource.PlayOneShot(clip, hornVolume);
        _lastHornTime = Time.time;
    }

    private static Vector3 CalcAngularVel(Quaternion from, Quaternion to, float dt)
    {
        Quaternion dq = to * Quaternion.Inverse(from);
        dq.ToAngleAxis(out float angDeg, out Vector3 axis);
        if (angDeg > 180f) angDeg -= 360f;
        return axis.normalized * Mathf.Deg2Rad * angDeg / Mathf.Max(dt, 0.0001f);
    }
}
