#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-fixes Scale In Lightmap on all scene MeshRenderers that have
/// ContributeGI enabled, compensating for objects that were scaled in the
/// Unity scene rather than being modelled at 1 unit = 1 metre in Blender.
/// </summary>
public static class FixLightmapScaleEditor
{
    [MenuItem("Sumo2Unity/Fix Lightmap Scale on Buildings")]
    private static void FixLightmapScale()
    {
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int fixed_ = 0;
        Undo.SetCurrentGroupName("Fix Lightmap Scale on Buildings");

        foreach (var mr in renderers)
        {
            // Only target objects that contribute to the baked lightmap.
            var flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
            if ((flags & StaticEditorFlags.ContributeGI) == 0) continue;

            // Compute the maximum world-space scale component so we can normalise.
            // Objects modelled at metre scale and then scaled to 100 in the scene
            // need Scale In Lightmap = 1 / worldScale to keep consistent texel density.
            float worldScale = Mathf.Max(
                Mathf.Abs(mr.transform.lossyScale.x),
                Mathf.Abs(mr.transform.lossyScale.y),
                Mathf.Abs(mr.transform.lossyScale.z));

            if (worldScale < 1f) continue; // skip if already at metre scale

            float normalised = 1f / worldScale;

            Undo.RecordObject(mr, "Fix Lightmap Scale");
            mr.scaleInLightmap = normalised;
            EditorUtility.SetDirty(mr);
            fixed_++;
        }

        Debug.Log($"[FixLightmapScale] Set Scale In Lightmap on {fixed_} MeshRenderers. " +
                  "Re-bake the scene to apply the changes.");
    }
}
#endif
