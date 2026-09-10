using UnityEngine;

// a tilt here would tilt the horizon in VR
public class DesktopCameraOffset : MonoBehaviour
{
    [Tooltip("Moved along the vehicle's own axes, negative z sits the view further back.")]
    public Vector3 positionOffset;

    [Tooltip("Euler degrees, positive x tilts the view down and positive y turns it right.")]
    public Vector3 rotationOffset;

    private void Start()
    {
        if (VrActive.IsActive) return;

        transform.localPosition += positionOffset;
        transform.localRotation *= Quaternion.Euler(rotationOffset);
    }
}
