using UnityEngine;
using System.Linq;

public class Spline : MonoBehaviour
{
    public int resolution = 20;

    private Transform[] controlPoints;

    private void Awake()
    {
        RefreshControlPoints();
    }

    private void RefreshControlPoints()
    {
        controlPoints = GetComponentsInChildren<Transform>()
            .Where(t => t != transform)
            .ToArray();
    }

    public Vector3 GetPoint(float t)
    {
        t = Mathf.Clamp01(t);

        int numPoints = controlPoints.Length;
        if (numPoints < 2) return Vector3.zero;

        int segmentCount = numPoints - 1;
        float scaledT = t * segmentCount;
        int i = Mathf.Clamp(Mathf.FloorToInt(scaledT), 0, segmentCount - 1);
        float localT = scaledT - i;

        // Clamp at edges so the curve never loops back
        int iPrev = Mathf.Max(0, i - 1);
        int i0 = i;
        int i1 = i + 1;
        int iNext = Mathf.Min(numPoints - 1, i + 2);

        return CatmullRom(
            controlPoints[iPrev].position,
            controlPoints[i0].position,
            controlPoints[i1].position,
            controlPoints[iNext].position,
            localT
        );
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void OnDrawGizmos()
    {
        RefreshControlPoints();

        if (controlPoints == null || controlPoints.Length < 2) return;

        Gizmos.color = Color.green;
        Vector3 previousPoint = GetPoint(0f);

        for (int i = 1; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            Vector3 point = GetPoint(t);
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
}