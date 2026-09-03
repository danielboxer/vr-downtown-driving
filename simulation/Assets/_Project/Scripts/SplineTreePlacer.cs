#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;

public class SplineTreePlacer : MonoBehaviour
{
    [Header("Spline")]
    [Tooltip("Spline to place trees along. If null, looks for a Spline on this GameObject.")]
    public Spline targetSpline;

    [Header("Trees")]
    [Tooltip("Tree prefabs to randomly choose from.")]
    public GameObject[] treePrefabs;
    [Tooltip("Relative spawn weight for each prefab (same length as Tree Prefabs). Higher = more frequent. Leave empty for equal weights.")]
    public float[] treeWeights;
    [Tooltip("Arc-length spacing between consecutive trees (meters).")]
    public float spacing = 8f;

    [Header("Randomization")]
    [Tooltip("Maximum random XZ position offset applied to each tree (meters).")]
    public float positionJitter = 0.3f;
    [Tooltip("Fixed Y offset applied to every tree position. Use negative values to sink trees slightly into the ground.")]
    public float heightOffset = -0.1f;
    [Tooltip("Maximum random Y-axis rotation applied to each tree (degrees, ±).")]
    public float rotationJitter = 180f;
    [Tooltip("Seed for reproducible random placement. Change to get a different arrangement.")]
    public int randomSeed = 42;

    [Header("Generation")]
    [Tooltip("Mark generated trees as static for batching and occlusion culling.")]
    public bool markStatic = true;
    [Tooltip("Number of arc samples used to estimate spline length. Higher = more accurate.")]
    public int arcSamples = 200;

    [ContextMenu("Generate Trees")]
    public void GenerateTrees()
    {
        Spline spline = targetSpline != null ? targetSpline : GetComponent<Spline>();
        if (spline == null)
        {
            Debug.LogError("[SplineTreePlacer] No Spline found. Assign one to 'Target Spline' or add a Spline component.");
            return;
        }

        if (treePrefabs == null || treePrefabs.Length == 0)
        {
            Debug.LogError("[SplineTreePlacer] No tree prefabs assigned.");
            return;
        }

        ClearTrees();

        float[] cumLengths = new float[arcSamples + 1];
        float[] tValues = new float[arcSamples + 1];
        BuildArcLengthTable(spline, arcSamples, cumLengths, tValues);

        float totalLength = cumLengths[arcSamples];
        if (totalLength < spacing)
        {
            Debug.LogWarning($"[SplineTreePlacer] Spline is shorter ({totalLength:F1} m) than spacing ({spacing:F1} m). No trees placed.");
            return;
        }

        float[] cumWeights = new float[treePrefabs.Length];
        float weightSum = 0f;
        for (int i = 0; i < treePrefabs.Length; i++)
        {
            float w = (treeWeights != null && i < treeWeights.Length) ? Mathf.Max(0f, treeWeights[i]) : 1f;
            weightSum += w;
            cumWeights[i] = weightSum;
        }
        if (weightSum <= 0f)
        {
            for (int i = 0; i < treePrefabs.Length; i++)
                cumWeights[i] = i + 1f;
            weightSum = treePrefabs.Length;
        }

        Random.InitState(randomSeed);

        // parented outside the spline so GetComponentsInChildren on Spline does not treat the trees as control points
        string containerName = $"{gameObject.name} GeneratedTrees";
        Transform containerParent = transform.parent; // sibling of this GameObject
        GameObject container = new GameObject(containerName);
        container.transform.SetParent(containerParent);
        container.transform.localPosition = Vector3.zero;
        container.transform.localRotation = Quaternion.identity;

        int placed = 0;
        float dist = spacing * 0.5f; // start half a spacing from the beginning
        while (dist <= totalLength - spacing * 0.5f)
        {
            float t = ArcLengthToT(dist, cumLengths, tValues);
            Vector3 pos = spline.GetPoint(t);

            if (positionJitter > 0f)
            {
                pos.x += Random.Range(-positionJitter, positionJitter);
                pos.z += Random.Range(-positionJitter, positionJitter);
            }

            pos.y += heightOffset;

            Quaternion rot = Quaternion.Euler(0f, Random.Range(-rotationJitter, rotationJitter), 0f);

            float rnd = Random.value * weightSum;
            int prefabIdx = treePrefabs.Length - 1;
            for (int i = 0; i < cumWeights.Length; i++)
            {
                if (rnd <= cumWeights[i]) { prefabIdx = i; break; }
            }
            GameObject prefabToUse = treePrefabs[prefabIdx];
            if (prefabToUse == null)
            {
                dist += spacing;
                continue;
            }

#if UNITY_EDITOR
            GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefabToUse);
            tree.transform.SetParent(container.transform);
            tree.transform.position = pos;
            // compose with the prefab's baked rotation so its native orientation survives
            tree.transform.rotation = rot * prefabToUse.transform.rotation;

            if (markStatic)
            {
#pragma warning disable CS0618 // NavigationStatic/OffMeshLinkGeneration deprecated but still functional
                GameObjectUtility.SetStaticEditorFlags(tree,
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.NavigationStatic |
                    StaticEditorFlags.OffMeshLinkGeneration |
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.ReflectionProbeStatic);
#pragma warning restore CS0618
            }
#else
            GameObject tree = Instantiate(prefabToUse, pos, rot * prefabToUse.transform.rotation, container.transform);
#endif
            tree.name = $"Tree_{placed}";
            placed++;
            dist += spacing;
        }

        Debug.Log($"[SplineTreePlacer] Placed {placed} trees along spline (total length {totalLength:F1} m).");

#if UNITY_EDITOR
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    [ContextMenu("Clear Trees")]
    public void ClearTrees()
    {
        string containerName = $"{gameObject.name} GeneratedTrees";
        Transform searchRoot = transform.parent;
        GameObject existing = null;
        if (searchRoot != null)
        {
            var t = searchRoot.Find(containerName);
            if (t != null) existing = t.gameObject;
        }
        else
        {
            existing = GameObject.Find(containerName);
        }

        if (existing == null) return;

#if UNITY_EDITOR
        DestroyImmediate(existing);
#else
        Destroy(existing);
#endif
    }

    private void OnDrawGizmos()
    {
        Spline spline = targetSpline != null ? targetSpline : GetComponent<Spline>();
        if (spline == null || treePrefabs == null || treePrefabs.Length == 0 || spacing <= 0f) return;

        const int gizmoSamples = 60;
        float[] cumLengths = new float[gizmoSamples + 1];
        float[] tValues = new float[gizmoSamples + 1];
        try { BuildArcLengthTable(spline, gizmoSamples, cumLengths, tValues); }
        catch { return; }

        float totalLength = cumLengths[gizmoSamples];
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.75f);

        float dist = spacing * 0.5f;
        while (dist <= totalLength - spacing * 0.5f)
        {
            float t = ArcLengthToT(dist, cumLengths, tValues);
            try { Gizmos.DrawSphere(spline.GetPoint(t), 0.5f); }
            catch { return; }
            dist += spacing;
        }
    }

    private static void BuildArcLengthTable(Spline spline, int samples, float[] cumLengths, float[] tValues)
    {
        cumLengths[0] = 0f;
        tValues[0] = 0f;
        Vector3 prev = spline.GetPoint(0f);
        for (int i = 1; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 curr = spline.GetPoint(t);
            cumLengths[i] = cumLengths[i - 1] + Vector3.Distance(prev, curr);
            tValues[i] = t;
            prev = curr;
        }
    }

    private static float ArcLengthToT(float arcDist, float[] cumLengths, float[] tValues)
    {
        int lo = 0, hi = cumLengths.Length - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (cumLengths[mid] < arcDist) lo = mid;
            else hi = mid;
        }
        float segLen = cumLengths[hi] - cumLengths[lo];
        if (segLen <= Mathf.Epsilon) return tValues[hi];
        float blend = (arcDist - cumLengths[lo]) / segLen;
        return Mathf.Lerp(tValues[lo], tValues[hi], blend);
    }
}
