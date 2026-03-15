using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns floating arrow indicators along the active scenario's spline
/// to guide the driver along the intended route. Arrows bob up and down
/// with a wave-like phase offset for a polished look.
/// </summary>
public class RouteArrowSpawner : MonoBehaviour
{
    [Header("Spacing")]
    [Tooltip("Distance in metres between arrows along the spline.")]
    public float spacing = 15f;

    [Header("Appearance")]
    [Tooltip("Height above the spline position to float the arrows.")]
    public float floatHeight = 3f;

    [Tooltip("Scale of each arrow.")]
    public float arrowScale = 1.5f;

    [Tooltip("Color of the arrows.")]
    public Color arrowColor = new Color(0.2f, 0.8f, 1f, 0.8f);

    [Header("Animation")]
    [Tooltip("Amplitude of the up-down bobbing (metres).")]
    public float bobAmplitude = 0.3f;

    [Tooltip("Speed of the bobbing cycle (Hz).")]
    public float bobSpeed = 1f;

    [Tooltip("Phase offset between adjacent arrows (seconds).")]
    public float bobPhaseStep = 0.4f;

    private readonly List<Transform> _arrows = new();
    private readonly List<float> _baseY = new();

    private Mesh _arrowMesh;
    private Material _arrowMaterial;

    /// <summary>
    /// Clears existing arrows and spawns new ones along the given spline.
    /// Pass null to just clear.
    /// </summary>
    public void SpawnArrows(Spline spline)
    {
        ClearArrows();
        if (spline == null) return;

        EnsureMeshAndMaterial();

        // Build a lookup table of (t → cumulative distance) to place arrows at even metre intervals
        const int samples = 500;
        float[] tValues = new float[samples + 1];
        float[] cumDist = new float[samples + 1];
        tValues[0] = 0f;
        cumDist[0] = 0f;
        Vector3 prev = spline.GetPoint(0f);

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 p = spline.GetPoint(t);
            tValues[i] = t;
            cumDist[i] = cumDist[i - 1] + Vector3.Distance(prev, p);
            prev = p;
        }

        float totalLength = cumDist[samples];
        int arrowCount = Mathf.FloorToInt(totalLength / spacing);

        for (int a = 1; a <= arrowCount; a++)
        {
            float targetDist = a * spacing;

            // Find the t value for this distance via the lookup table
            float t = LookUpT(tValues, cumDist, targetDist, samples);
            Vector3 pos = spline.GetPoint(t);

            // Tangent via finite difference
            float tNext = Mathf.Clamp01(t + 0.005f);
            Vector3 tangent = spline.GetPoint(tNext) - pos;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.001f) continue;
            tangent.Normalize();

            pos.y += floatHeight;

            GameObject go = new GameObject($"RouteArrow_{a}");
            go.transform.SetParent(transform);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            go.transform.localScale = Vector3.one * arrowScale;

            go.AddComponent<MeshFilter>().sharedMesh = _arrowMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _arrowMaterial;

            _arrows.Add(go.transform);
            _baseY.Add(pos.y);
        }

        Debug.Log($"[RouteArrowSpawner] Spawned {_arrows.Count} arrows (length ≈ {totalLength:F0}m, spacing {spacing}m)");
    }

    /// <summary>Destroys all spawned arrow GameObjects.</summary>
    public void ClearArrows()
    {
        foreach (var a in _arrows)
        {
            if (a != null) Destroy(a.gameObject);
        }
        _arrows.Clear();
        _baseY.Clear();
    }

    private void Update()
    {
        if (_arrows.Count == 0) return;

        for (int i = 0; i < _arrows.Count; i++)
        {
            if (_arrows[i] == null) continue;
            float phase = i * bobPhaseStep;
            float bob = Mathf.Sin((Time.time + phase) * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
            var pos = _arrows[i].position;
            pos.y = _baseY[i] + bob;
            _arrows[i].position = pos;
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Binary-search the lookup table to convert a cumulative distance to a t value.
    /// </summary>
    private static float LookUpT(float[] tValues, float[] cumDist, float distance, int samples)
    {
        int lo = 0, hi = samples;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (cumDist[mid] < distance) lo = mid + 1;
            else hi = mid;
        }

        if (lo == 0) return tValues[0];

        // Lerp between the two bracketing samples for precision
        float segLen = cumDist[lo] - cumDist[lo - 1];
        float frac = (segLen > 0.0001f)
            ? (distance - cumDist[lo - 1]) / segLen
            : 0f;

        return Mathf.Lerp(tValues[lo - 1], tValues[lo], frac);
    }

    private void EnsureMeshAndMaterial()
    {
        if (_arrowMesh == null)
            _arrowMesh = CreateArrowMesh();

        if (_arrowMaterial == null)
        {
            // Unlit transparent material so arrows are visible in any lighting
            _arrowMaterial = new Material(Shader.Find("Unlit/Color"));
            _arrowMaterial.color = arrowColor;
        }
    }

    /// <summary>
    /// Creates a simple double-sided arrow mesh on the XZ plane (pointing along +Z).
    /// The shape is a narrow shaft + triangular arrowhead.
    /// </summary>
    private static Mesh CreateArrowMesh()
    {
        var mesh = new Mesh { name = "RouteArrow" };

        // Arrow shape (top-down view, pointing +Z)
        //
        //       4 (tip)
        //      / \
        //     /   \
        //    5     3     (wings, wider)
        //    |     |
        //    6     2     (shaft-to-head junction)
        //    |     |
        //    0─────1     (shaft back)

        mesh.vertices = new[]
        {
            new Vector3(-0.12f, 0f, -0.5f),  // 0 shaft back-left
            new Vector3( 0.12f, 0f, -0.5f),  // 1 shaft back-right
            new Vector3( 0.12f, 0f,  0.0f),  // 2 shaft front-right
            new Vector3( 0.35f, 0f,  0.0f),  // 3 head wing right
            new Vector3( 0.00f, 0f,  0.5f),  // 4 tip
            new Vector3(-0.35f, 0f,  0.0f),  // 5 head wing left
            new Vector3(-0.12f, 0f,  0.0f),  // 6 shaft front-left
        };

        mesh.triangles = new[]
        {
            // Top face (visible from +Y)
            0, 6, 1,
            1, 6, 2,
            5, 4, 3,

            // Bottom face (visible from -Y) — reversed winding
            1, 6, 0,
            2, 6, 1,
            3, 4, 5,
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
