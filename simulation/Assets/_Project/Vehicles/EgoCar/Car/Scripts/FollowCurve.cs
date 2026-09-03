using UnityEngine;

public class FollowCurve : MonoBehaviour
{

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

    [Tooltip("Maximum lateral correction in steering units (0-1).")]
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

    [Header("Spline")]
    [Tooltip("The spline this component should follow. Must be assigned explicitly.")]
    public Spline targetSpline;


    private Spline spline;
    private float currentClosestT;

    [Header("Debug")]
    [ReadOnly, SerializeField] private float _dbgEffectiveWeight;
    [ReadOnly, SerializeField] private float _dbgSplineSteer;
    [ReadOnly, SerializeField] private float _dbgLateralOffset;
    [ReadOnly, SerializeField] private float _dbgClosestT;
    [ReadOnly, SerializeField] private float _dbgDistanceToSpline;
#pragma warning disable CS0414 // assigned for Inspector display only
    [ReadOnly, SerializeField] private bool _dbgIsActive;
#pragma warning restore CS0414


    private void Start()
    {
        // ScenarioManager calls RefreshSpline before Start on late-activate
        if (spline == null)
            RefreshSpline();
    }

    public void RefreshSpline(Spline assignedSpline = null)
    {
        spline = assignedSpline ?? targetSpline;
        if (spline != null)
            Debug.Log($"FollowCurve: Using spline '{spline.gameObject.name}', base weight = {splineWeight:F2}");
    }


    public float GetBlendedSteering(float playerSteering, float maxSteerAngle)
    {
        if (spline == null) return playerSteering;

        currentClosestT = FindClosestTOnSpline(transform.position);
        Vector3 closestPoint = spline.GetPoint(currentClosestT);
        _dbgClosestT = currentClosestT;

        // once past the last point, release control
        if (currentClosestT >= 0.99f)
        {
            Vector3 splineEnd = spline.GetPoint(1f);
            Vector3 carForwardFlat = transform.forward;
            carForwardFlat.y = 0f;
            Vector3 toEnd = splineEnd - transform.position;
            toEnd.y = 0f;
            if (Vector3.Dot(carForwardFlat, toEnd) < 0f)
            {
                _dbgIsActive = false;
                _dbgEffectiveWeight = 0f;
                return playerSteering;
            }
        }

        float distToSpline = Vector3.Distance(transform.position, closestPoint);
        _dbgDistanceToSpline = distToSpline;

        if (distToSpline > activationDistance)
        {
            _dbgIsActive = false;
            _dbgEffectiveWeight = 0f;
            return playerSteering;  // too far — pure player control
        }
        _dbgIsActive = true;

        // full weight inside fullBlendDistance, fading linearly to 0 at activationDistance
        float proximityFactor = 1f;
        if (distToSpline > fullBlendDistance)
            proximityFactor = 1f - Mathf.InverseLerp(fullBlendDistance, activationDistance, distToSpline);

        float effectiveWeight = splineWeight;
        if (enableTurnTightening)
            effectiveWeight = GetTurnTightenedWeight(currentClosestT);

        effectiveWeight *= proximityFactor;
        _dbgEffectiveWeight = effectiveWeight;

        // pure pursuit: aim at a look-ahead point on the spline
        float lookAheadT = EstimateLookAheadT(currentClosestT, lookAheadMeters);
        Vector3 lookAheadPoint = spline.GetPoint(lookAheadT);

        Vector3 toTarget = lookAheadPoint - transform.position;
        toTarget.y = 0f;

        Vector3 carForward = transform.forward;
        carForward.y = 0f;
        carForward.Normalize();

        float angleToTarget = Vector3.SignedAngle(carForward, toTarget.normalized, Vector3.up);
        float splineSteering = Mathf.Clamp(angleToTarget / maxSteerAngle, -1f, 1f);

        // dot with the car's right axis: positive means the spline is to our right
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

        float blended = Mathf.Lerp(playerSteering, splineSteering, effectiveWeight);

#if UNITY_EDITOR
        Debug.DrawLine(transform.position, closestPoint, Color.cyan);   // nearest spline point
        Debug.DrawLine(transform.position, lookAheadPoint, Color.yellow); // look-ahead target
        Debug.DrawRay(transform.position, carForward * 3f, Color.blue);   // car forward
#endif

        return blended;
    }


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

    private float GetTurnTightenedWeight(float t)
    {
        if (t < turnStartT || t > turnEndT)
            return splineWeight;

        if (t <= turnPeakT)
        {
            float ramp = Mathf.InverseLerp(turnStartT, turnPeakT, t);
            return Mathf.Lerp(splineWeight, turnPeakWeight, ramp);
        }
        else
        {
            float ramp = Mathf.InverseLerp(turnEndT, turnPeakT, t);
            return Mathf.Lerp(splineWeight, turnPeakWeight, ramp);
        }
    }

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

}
