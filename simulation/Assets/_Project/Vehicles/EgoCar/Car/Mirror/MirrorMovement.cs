using UnityEngine;

public class MirrorMovement : MonoBehaviour
{
    public Transform playerTarget;
    public Transform mirror;
    // Independent sensitivity for lateral (X) and vertical (Y) head lean
    [Range(1f, 10f)] public float xSensitivity = 3f;
    [Range(1f, 10f)] public float ySensitivity = 3f;
    // Fixed offset in mirror local space to bias base view direction (tune to show car edge)
    public Vector2 viewBias = Vector2.zero;

    // Update is called once per frame
    void Update()
    {
        Vector3 localPlayer = mirror.InverseTransformPoint(playerTarget.position);
        // Apply independent sensitivity per axis; Y negated so raising head looks down toward curb
        // viewBias offsets the base look direction independent of head position
        Vector3 lookatmirror = mirror.TransformPoint(new Vector3(
            (-localPlayer.x * xSensitivity) + viewBias.x,
            (-localPlayer.y * ySensitivity) + viewBias.y,
            localPlayer.z));
        transform.LookAt(lookatmirror);
    }
}
