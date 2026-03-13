using UnityEngine;

/// <summary>
/// Steering Influence Blending — replaces the spring-force approach of ConstrainToCurve.
///
/// Each frame this component computes two candidate steering values:
///   1. The player's raw steering input (from the physical wheel / keyboard).
///   2. A spline-following autopilot value computed via pure-pursuit look-ahead.
///
/// It then lerps between them:
///   blendedSteering = Lerp(playerSteering, splineSteering, splineWeight)
///
/// With splineWeight = 0.80 the car feels responsive yet stays on the road.
/// During the right-turn section the weight can be ramped to 1.0 via the
/// Turn-Tightening Zone, guaranteeing the manoeuvre always succeeds.
///
/// SETUP
///   1. Attach this to the same GameObject as CarController / CarUserControl.
///   2. Disable or remove ConstrainToCurve (the two systems conflict).
///   3. Ensure a Spline component exists somewhere in the scene.
///   4. Tune the Inspector values during testing sessions.
/// </summary>
public class FollowCurve : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  Inspector-tunable parameters
    // ──────────────────────────────────────────────────────────────

    [Header("Blend Settings")]
    [Tooltip("Base blend weight toward the spline.\n" +
             "0 = full player control, 1 = full spline control.\n" +
             "Recommended starting value: 0.80")]
    [Range(0f, 1f)]
    public float splineWeight = 0.80f;

    [Header("Look-Ahead (Pure Pursuit)")]
    [Tooltip("How far ahead on the spline (in metres) the car aims.\n" +
             "Larger values produce smoother, gentler corrections;\n" +
             "smaller values make the car track the curve more tightly.")]
    public float lookAheadMeters = 10f;

    [Header("Lateral Correction")]
    [Tooltip("Additional steering gain proportional to the lateral offset " +
             "from the spline. Helps pull the car back if it has drifted sideways.")]
    public float lateralCorrectionGain = 0.3f;

    [Tooltip("Maximum lateral correction in steering units (0–1).")]
    [Range(0f, 1f)]
    public float maxLateralCorrection = 0.3f;

    [Header("Proximity")]
    [Tooltip("Distance beyond which blending is completely disabled (pure player control).")]
    public float activationDistance = 10f;

    [Tooltip("Distance within which full blending is applied.\n" +
             "Between this and activationDistance the weight fades out smoothly.")]
    public float fullBlendDistance = 5f;

    [Header("Turn-Tightening Zone")]
    [Tooltip("Enable dynamic blend tightening during the turn section of the spline.")]
    public bool enableTurnTightening = true;

    [Tooltip("Spline t where tightening begins (approach to the turn).")]
    [Range(0f, 1f)]
    public float turnStartT = 0.4f;

    [Tooltip("Spline t where tightening is strongest (mid-turn).")]
    [Range(0f, 1f)]
    public float turnPeakT = 0.6f;

    [Tooltip("Spline t where tightening ends (exit of the turn).")]
    [Range(0f, 1f)]
    public float turnEndT = 0.8f;

    [Tooltip("Spline weight at the peak of the turn zone.")]
    [Range(0f, 1f)]
    public float turnPeakWeight = 1.0f;

    // ──────────────────────────────────────────────────────────────
    //  Runtime state
    // ──────────────────────────────────────────────────────────────

    private Spline spline;
    private float currentClosestT;

    // Debug readouts (visible in the Inspector at runtime)
    [Header("Debug (read-only at runtime)")]
    [SerializeField] private float _dbgEffectiveWeight;
    [SerializeField] private float _dbgSplineSteer;
    [SerializeField] private float _dbgLateralOffset;
    [SerializeField] private float _dbgClosestT;
    [SerializeField] private float _dbgDistanceToSpline;
    [SerializeField] private bool _dbgIsActive;

    // ──────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────────────────────

    private void Start()
    {
        RefreshSpline();
    }

    /// <summary>
    /// Re-scan for an active Spline in the scene.
    /// Called automatically at Start and can be called after scenario changes.
    /// </summary>
    public void RefreshSpline()
    {
        spline = FindFirstObjectByType<Spline>();
        if (spline != null)
        {
            Debug.Log($"FollowCurve: Spline found ({spline.gameObject.name}), " +
                      $"base weight = {splineWeight:F2}");
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  PUBLIC API — called by CarUserControl every FixedUpdate
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a blended steering value in [−1, 1].
    /// </summary>
    /// <param name="playerSteering">Raw player steering input in [−1, 1].</param>
    /// <param name="maxSteerAngle">
    /// The car's maximum steering angle in degrees (used to normalise the
    /// angle-to-target into the [−1, 1] range).
    /// </param>
    public float GetBlendedSteering(float playerSteering, float maxSteerAngle)
    {
        if (spline == null) return playerSteering;

        // 1. Find the closest point on the spline to the car
        currentClosestT = FindClosestTOnSpline(transform.position);
        Vector3 closestPoint = spline.GetPoint(currentClosestT);
        _dbgClosestT = currentClosestT;

        // 1b. Distance check — fade out when the car is far from the spline
        float distToSpline = Vector3.Distance(transform.position, closestPoint);
        _dbgDistanceToSpline = distToSpline;

        if (distToSpline > activationDistance)
        {
            _dbgIsActive = false;
            _dbgEffectiveWeight = 0f;
            return playerSteering;  // too far — pure player control
        }
        _dbgIsActive = true;

        // Proximity fade: full weight inside fullBlendDistance,
        // linearly fading to 0 at activationDistance.
        float proximityFactor = 1f;
        if (distToSpline > fullBlendDistance)
            proximityFactor = 1f - Mathf.InverseLerp(fullBlendDistance, activationDistance, distToSpline);

        // 2. Compute effective weight (base + optional turn tightening)
        float effectiveWeight = splineWeight;
        if (enableTurnTightening)
            effectiveWeight = GetTurnTightenedWeight(currentClosestT);

        // Apply proximity fade
        effectiveWeight *= proximityFactor;
        _dbgEffectiveWeight = effectiveWeight;

        // 3. Pure-pursuit: aim at a look-ahead point on the spline
        float lookAheadT = EstimateLookAheadT(currentClosestT, lookAheadMeters);
        Vector3 lookAheadPoint = spline.GetPoint(lookAheadT);

        // Direction from car to look-ahead point (projected onto the horizontal plane)
        Vector3 toTarget = lookAheadPoint - transform.position;
        toTarget.y = 0f;

        Vector3 carForward = transform.forward;
        carForward.y = 0f;
        carForward.Normalize();

        // Signed angle → normalised steering value
        float angleToTarget = Vector3.SignedAngle(carForward, toTarget.normalized, Vector3.up);
        float splineSteering = Mathf.Clamp(angleToTarget / maxSteerAngle, -1f, 1f);

        // 4. Lateral correction — proportional to perpendicular offset
        //    Dot with car's right axis: positive ⇒ spline is to our right ⇒ steer right.
        Vector3 offset = closestPoint - transform.position;
        offset.y = 0f;
        float lateralDot = Vector3.Dot(transform.right, offset);

        float lateralCorrection = Mathf.Clamp(
            lateralDot * lateralCorrectionGain,
            -maxLateralCorrection,
            maxLateralCorrection);

        splineSteering = Mathf.Clamp(splineSteering + lateralCorrection, -1f, 1f);

        _dbgSplineSteer = splineSteering;
        _dbgLateralOffset = lateralDot;

        // 5. Blend: lerp between player input and spline-following autopilot
        float blended = Mathf.Lerp(playerSteering, splineSteering, effectiveWeight);

        // Scene-view debug lines
        Debug.DrawLine(transform.position, closestPoint, Color.cyan);   // nearest spline point
        Debug.DrawLine(transform.position, lookAheadPoint, Color.yellow); // look-ahead target
        Debug.DrawRay(transform.position, carForward * 3f, Color.blue);   // car forward

        return blended;
    }

    // ──────────────────────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a look-ahead distance in metres to a t-offset on the spline
    /// by estimating the local arc-length per unit-t at the current position.
    /// </summary>
    private float EstimateLookAheadT(float fromT, float metres)
    {
        const float sampleDT = 0.01f;
        float tA = Mathf.Clamp01(fromT);
        float tB = Mathf.Clamp01(fromT + sampleDT);

        float segLen = Vector3.Distance(spline.GetPoint(tA), spline.GetPoint(tB));
        float metresPerT = segLen / sampleDT;

        float tOffset = (metresPerT > 0.001f) ? (metres / metresPerT) : sampleDT;
        return Mathf.Clamp01(fromT + tOffset);
    }

    /// <summary>
    /// Returns the effective spline weight at the given t, ramping from
    /// <see cref="splineWeight"/> to <see cref="turnPeakWeight"/> inside the
    /// turn zone and back again.
    /// </summary>
    private float GetTurnTightenedWeight(float t)
    {
        if (t < turnStartT || t > turnEndT)
            return splineWeight;

        if (t <= turnPeakT)
        {
            // Ramp up: approach → mid-turn
            float ramp = Mathf.InverseLerp(turnStartT, turnPeakT, t);
            return Mathf.Lerp(splineWeight, turnPeakWeight, ramp);
        }
        else
        {
            // Ramp down: mid-turn → exit
            float ramp = Mathf.InverseLerp(turnEndT, turnPeakT, t);
            return Mathf.Lerp(splineWeight, turnPeakWeight, ramp);
        }
    }

    /// <summary>
    /// Brute-force search for the closest t on the spline (step = 0.01).
    /// </summary>
    private float FindClosestTOnSpline(Vector3 position)
    {
        float closestT = 0f;
        float minDist = float.MaxValue;

        for (float t = 0f; t <= 1f; t += 0.01f)
        {
            float d = Vector3.Distance(position, spline.GetPoint(t));
            if (d < minDist)
            {
                minDist = d;
                closestT = t;
            }
        }
        return closestT;
    }

    // ──────────────────────────────────────────────────────────────
    //  HUD overlay
    // ──────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 650, 25),
            $"Weight: {_dbgEffectiveWeight:F2}  |  " +
            $"SplineSteer: {_dbgSplineSteer:F2}  |  " +
            $"Lateral: {_dbgLateralOffset:F2}m  |  " +
            $"T: {_dbgClosestT:F3}  |  " +
            $"Dist: {_dbgDistanceToSpline:F1}m  |  " +
            $"Active: {_dbgIsActive}");
    }
}
