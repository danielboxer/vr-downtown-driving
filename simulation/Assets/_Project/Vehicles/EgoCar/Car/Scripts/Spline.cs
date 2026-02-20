using UnityEngine;
using System.Linq; // For LINQ to get child transforms

public class Spline : MonoBehaviour
{
    public int resolution = 20; // Number of points to interpolate

    private Transform[] controlPoints;

    private void Awake()
    {
        // Automatically populate control points from child transforms
        controlPoints = GetComponentsInChildren<Transform>()
            .Where(t => t != transform) // Exclude the parent object itself
            .ToArray();
    }

    public Vector3 GetPoint(float t)
    {
        // Ensure t is clamped between 0 and 1
        t = Mathf.Clamp01(t);

        // Calculate indices of control points
        int numPoints = controlPoints.Length;
        int p0 = Mathf.Clamp(Mathf.FloorToInt(t * (numPoints - 1)) - 1, 0, numPoints - 1);
        int p1 = Mathf.Clamp(p0 + 1, 0, numPoints - 1);
        int p2 = Mathf.Clamp(p1 + 1, 0, numPoints - 1);
        int p3 = Mathf.Clamp(p2 + 1, 0, numPoints - 1);

        // Calculate local t (between p1 and p2)
        float localT = (t * (numPoints - 1)) - Mathf.Floor(t * (numPoints - 1));

        // Use Catmull-Rom interpolation
        return CatmullRom(
            controlPoints[p0].position,
            controlPoints[p1].position,
            controlPoints[p2].position,
            controlPoints[p3].position,
            localT
        );
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void OnDrawGizmos()
    {
        // Automatically update control points in the editor
        controlPoints = GetComponentsInChildren<Transform>()
            .Where(t => t != transform)
            .ToArray();

        if (controlPoints == null || controlPoints.Length < 2) return;

        // Draw the curve
        Gizmos.color = Color.green;
        Vector3 previousPoint = controlPoints[0].position;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            Vector3 point = GetPoint(t);
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
}