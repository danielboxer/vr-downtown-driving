// ─────────────────────────────────────────────────────────────────────────────
// LaneSegmentDecalController  (LEFT/RIGHT names swapped logic)
// ─────────────────────────────────────────────────────────────────────────────
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class LaneSegmentDecalController : MonoBehaviour
{
    [Tooltip("Broken Lines in LEFT lane marking (acts on objects named *Right* after swap).")]
    public bool brokenLeft;
    [Tooltip("Broken Lines in RIGHT lane marking (acts on objects named *Left* after swap).")]
    public bool brokenRight;

    [HideInInspector] public float solidDepth = 3f;
    [HideInInspector] public float brokenDepth = 1.5f;

    private bool _prevBrokenLeft;
    private bool _prevBrokenRight;

    private void OnValidate()
    {
        if (brokenLeft != _prevBrokenLeft)
        {
            SetDepthForSide("LaneMarking_Right_Decal", brokenLeft);
            _prevBrokenLeft = brokenLeft;
        }

        if (brokenRight != _prevBrokenRight)
        {
            SetDepthForSide("LaneMarking_Left_Decal", brokenRight);
            _prevBrokenRight = brokenRight;
        }
    }

    private void SetDepthForSide(string prefix, bool broken)
    {
        float targetDepth = broken ? brokenDepth : solidDepth;

        var decals = GetComponentsInChildren<DecalProjector>(true);
        foreach (var d in decals)
        {
            if (!d.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            Vector3 s = d.size;
            if (Math.Abs(s.z - targetDepth) < 0.0001f) continue;
            s.z = targetDepth;
            d.size = s;
#if UNITY_EDITOR
            EditorUtility.SetDirty(d);
#endif
        }
    }
}
