using UnityEngine;

public class ConstrainToCurve : MonoBehaviour
{
    [Header("Spring Settings")]
    [Tooltip("How strongly the car is pulled back to the curve.")]
    public float springStrength = 500f;

    [Tooltip("Damping to reduce oscillation.")]
    public float damping = 50f;

    [Header("Proximity")]
    [Tooltip("Distance within which the spring force activates.")]
    public float activationDistance = 10f;

    private Spline spline;
    private Rigidbody rb;
    private float debugDistance = 0f;
    private bool isActive = false;

    private void Start()
    {
        // Try on this object first, then search parents (in case script is on a child)
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();

        if (rb == null)
            Debug.LogError("ConstrainToCurve: No Rigidbody found on car or its parents!");
        else
            Debug.Log("ConstrainToCurve: Rigidbody found on " + rb.gameObject.name);

        spline = FindFirstObjectByType<Spline>();
        if (spline == null)
        {
            Debug.LogError("ConstrainToCurve: No Spline found in the scene!");
            enabled = false;
        }
        else
            Debug.Log("ConstrainToCurve: Spline found: " + spline.gameObject.name);
    }

    private void FixedUpdate()
    {
        if (spline == null || rb == null) return;

        // Find the closest point on the spline
        float closestT = FindClosestTOnSpline(transform.position);
        Vector3 closestPoint = spline.GetPoint(closestT);

        // Calculate how far the car is from the spline
        Vector3 offset = closestPoint - transform.position;
        float distance = offset.magnitude;
        debugDistance = distance;

        // Only apply force when within activation distance
        isActive = distance <= activationDistance;
        if (!isActive) return;

        // Calculate the spline tangent at the closest point
        float tangentT = Mathf.Clamp(closestT + 0.01f, 0f, 1f);
        Vector3 tangent = (spline.GetPoint(tangentT) - closestPoint).normalized;

        // Project the offset onto the lateral direction (perpendicular to the spline tangent)
        Vector3 lateralOffset = offset - Vector3.Project(offset, tangent);

        // Project the car's velocity onto the lateral direction for damping
        Vector3 lateralVelocity = Vector3.Project(rb.linearVelocity, lateralOffset.normalized);

        // Apply spring + damping force in the lateral direction
        Vector3 springForce = lateralOffset * springStrength - lateralVelocity * damping;
        rb.AddForce(springForce, ForceMode.Force);

        // Draw debug lines in the scene view
        Debug.DrawLine(transform.position, closestPoint, Color.cyan);
        Debug.DrawRay(closestPoint, tangent * 2f, Color.yellow);
        Debug.DrawRay(transform.position, springForce.normalized, Color.red);
    }

    private void OnGUI()
    {
        GUI.color = isActive ? Color.green : Color.red;
        GUI.Label(new Rect(10, 10, 400, 25), $"Spline distance: {debugDistance:F1}m  |  Constraining: {isActive}  |  Activation: {activationDistance}m");
        GUI.color = Color.white;
    }

    private float FindClosestTOnSpline(Vector3 position)
    {
        float closestT = 0f;
        float minDistance = float.MaxValue;

        for (float t = 0; t <= 1f; t += 0.01f)
        {
            Vector3 point = spline.GetPoint(t);
            float distance = Vector3.Distance(position, point);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestT = t;
            }
        }

        return closestT;
    }
}