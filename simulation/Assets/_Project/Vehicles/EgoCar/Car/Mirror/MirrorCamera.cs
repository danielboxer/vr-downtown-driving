using UnityEngine;

public class MirrorMovement : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The live VR camera/head transform.")]
    public Transform playerTarget;

    [Tooltip("The mirror transform whose local space defines the mirror movement axes.")]
    public Transform mirror;

    [Tooltip("Optional fixed transform that represents the neutral VR camera/head position, such as a seated eye-position marker. If unset, the live VR camera position when Play starts is used.")]
    public Transform playerOrigin;

    [Header("Movement")]
    [Tooltip("How much the mirror camera shifts in response to VR head movement. Set to 0 to disable parallax movement entirely.")]
    [Range(0f, 10f)] public float sensitivity = 1f;

    [Header("Rendering")]
    [Tooltip("Render the mirror every N frames. 1 = every frame. 2 = every other frame (~45 fps at 90 Hz). Keeps mirrors near real-time while saving GPU cost.")]
    [Range(1, 4)] public int renderEveryNFrames = 2;

    [Tooltip("Frame offset for staggering multiple mirrors. Set mirrors to renderEveryNFrames=3 and offsets 0, 1, 2 so only one mirror renders per frame instead of all at once.")]
    [Range(0, 3)] public int frameOffset = 0;

    private Camera _mirrorCamera;
    private Vector3 fallbackInitialPlayerPositionInMirrorSpace;
    private Vector3 initialCameraPositionInMirrorSpace;
    private Quaternion initialCameraRotationInMirrorSpace;
    private bool hasInitialPose;

    private void Start()
    {
        // Disable auto-rendering so we control when each mirror draws via Camera.Render().
        _mirrorCamera = GetComponent<Camera>();
        if (_mirrorCamera != null)
        {
            _mirrorCamera.enabled = false;
        }

        CaptureInitialPose();
    }

    private void LateUpdate()
    {
        if (!HasValidReferences())
        {
            return;
        }

        if (!hasInitialPose)
        {
            CaptureInitialPose();
        }

        Vector3 playerPositionInMirrorSpace = mirror.InverseTransformPoint(playerTarget.position);
        Vector3 playerOriginInMirrorSpace = GetPlayerOriginInMirrorSpace();
        Vector3 playerDeltaInMirrorSpace = playerPositionInMirrorSpace - playerOriginInMirrorSpace;
        // Mirror parallax always inverts X and Y: head right = mirror pans left, etc.
        Vector3 cameraOffsetInMirrorSpace = new Vector3(
            -playerDeltaInMirrorSpace.x * sensitivity,
            -playerDeltaInMirrorSpace.y * sensitivity,
            0f);

        Vector3 cameraPositionInMirrorSpace = initialCameraPositionInMirrorSpace + cameraOffsetInMirrorSpace;
        Vector3 cameraWorldPosition = mirror.TransformPoint(cameraPositionInMirrorSpace);
        Quaternion cameraWorldRotation = mirror.rotation * initialCameraRotationInMirrorSpace;

        transform.SetPositionAndRotation(cameraWorldPosition, cameraWorldRotation);

        // Only render on the designated frame interval to save GPU cost.
        if (_mirrorCamera != null && (Time.frameCount + frameOffset) % renderEveryNFrames == 0)
        {
            _mirrorCamera.Render();
        }
    }

    public void CaptureInitialPose()
    {
        if (!HasValidReferences())
        {
            hasInitialPose = false;
            return;
        }

        fallbackInitialPlayerPositionInMirrorSpace = mirror.InverseTransformPoint(playerTarget.position);
        initialCameraPositionInMirrorSpace = mirror.InverseTransformPoint(transform.position);
        initialCameraRotationInMirrorSpace = Quaternion.Inverse(mirror.rotation) * transform.rotation;
        hasInitialPose = true;
    }

    private Vector3 GetPlayerOriginInMirrorSpace()
    {
        if (playerOrigin != null)
        {
            return mirror.InverseTransformPoint(playerOrigin.position);
        }

        return fallbackInitialPlayerPositionInMirrorSpace;
    }

    private bool HasValidReferences()
    {
        return playerTarget != null && mirror != null;
    }
}
