using UnityEngine;

public class WheelAnimator : WheelAnimatorBase
{
    protected override float ComputeSpeed(Vector3 velocity)
    {
        // Project velocity along the vehicle's right axis (wheel spin direction for cars)
        Quaternion rotation;
        if (vehicleController != null && !vehicleController.IsDetached)
            rotation = vehicleController.transform.rotation;
        else if (rb != null)
            rotation = rb.rotation;
        else
            return 0f;

        return Vector3.Dot(velocity, rotation * Vector3.right);
    }

    protected override void RotateWheel(Transform wheel, float delta)
    {
        wheel.Rotate(0f, delta, 0f, Space.Self);
    }
}
