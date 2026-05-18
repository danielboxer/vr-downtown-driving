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

    [Tooltip("Optional texture for the arrows. Use a white arrow on a transparent background (PNG). " +
             "The image is placed on a quad; arrow direction should point upward in the image. " +
             "If unset, the built-in procedural arrow mesh is used.")]
    public Texture2D arrowTexture;

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
    private Transform _egoTransform;

    /// <summary>
    /// Clears existing arrows and spawns new ones along the given spline.
    /// Pass null to just clear.
    /// </summary>
    public void SpawnArrows(Spline spline)
    {
        ClearArrows();
        if (spline == null) return;

        EnsureMeshAndMaterial();

        // Cache the ego vehicle transform for billboard rotation in Update
        if (arrowTexture != null)
            _egoTransform = GameObject.Find("f_0.0")?.transform;

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
            // Point the arrow's forward along the route, then tilt it to
            // face downward so the driver can see the shape from below.
            Quaternion faceForward = Quaternion.LookRotation(tangent, Vector3.up);
            go.transform.rotation = faceForward * Quaternion.Euler(90f, 0f, 0f);
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
        _egoTransform = null;
    }

    private void Update()
    {
        if (_arrows.Count == 0) return;

        // Billboard viewer: prefer the main camera (VR headset), fall back to the cached ego transform
        Transform viewer = arrowTexture != null
            ? (Camera.main != null ? Camera.main.transform : _egoTransform)
            : null;

        for (int i = 0; i < _arrows.Count; i++)
        {
            if (_arrows[i] == null) continue;
            float phase = i * bobPhaseStep;
            float bob = Mathf.Sin((Time.time + phase) * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
            var pos = _arrows[i].position;
            pos.y = _baseY[i] + bob;
            _arrows[i].position = pos;

            // Rotate the textured arrow on Y to always face the ego vehicle,
            // preserving the 90-degree downward tilt so it reads from below.
            if (viewer != null)
            {
                Vector3 toViewer = viewer.position - _arrows[i].position;
                toViewer.y = 0f;
                if (toViewer.sqrMagnitude > 0.001f)
                    _arrows[i].rotation = Quaternion.LookRotation(toViewer, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            }
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
        bool useTexture = arrowTexture != null;

        // Reset if the mode has changed since the last spawn (e.g. texture was assigned or cleared)
        if (_arrowMesh != null)
        {
            bool wasTextured = _arrowMesh.name == "RouteArrowQuad";
            if (wasTextured != useTexture)
            {
                _arrowMesh = null;
                _arrowMaterial = null;
            }
        }

        if (useTexture)
        {
            if (_arrowMesh == null)
                _arrowMesh = CreateQuadMesh();

            if (_arrowMaterial == null)
            {
                // Sprites/Default supports alpha transparency and color tinting in both Built-in and URP
                _arrowMaterial = new Material(Shader.Find("Sprites/Default"));
                _arrowMaterial.mainTexture = arrowTexture;
                _arrowMaterial.color = Color.white;
            }
        }
        else
        {
            if (_arrowMesh == null)
                _arrowMesh = CreateArrowMesh();

            if (_arrowMaterial == null)
            {
                // Unlit material so arrows are visible in any lighting
                _arrowMaterial = new Material(Shader.Find("Unlit/Color"));
                _arrowMaterial.color = Color.white;
            }
        }
    }

    /// <summary>
    /// Creates a flat double-sided 1x1 quad on the XZ plane with UV coordinates.
    /// Used when arrowTexture is assigned; the image alpha defines the arrow shape.
    /// </summary>
    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "RouteArrowQuad" };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),  // 0 back-left
            new Vector3( 0.5f, 0f, -0.5f),  // 1 back-right
            new Vector3( 0.5f, 0f,  0.5f),  // 2 front-right
            new Vector3(-0.5f, 0f,  0.5f),  // 3 front-left
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };

        // Double-sided: top face (+Y) and bottom face (-Y, reversed winding)
        mesh.triangles = new[]
        {
            0, 3, 1,  1, 3, 2,
            0, 1, 3,  1, 2, 3,
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Creates a flat double-sided arrow mesh on the XZ plane (pointing +Z).
    /// Both faces are rendered so it is visible from above and below.
    /// </summary>
    private static Mesh CreateArrowMesh()
    {
        var mesh = new Mesh { name = "RouteArrow" };

        // Arrow shape (top-down view, pointing +Z)
        //       4 (tip)
        //      / \
        //     5   3      (head wings)
        //     6   2      (shaft-to-head junction)
        //     |   |
        //     0───1      (shaft back)

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
