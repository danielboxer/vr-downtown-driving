using UnityEngine;

public class BikeWheelAnimator : WheelAnimatorBase
{
    protected override float ComputeSpeed(Vector3 velocity)
    {
        // Project velocity along the bike's forward axis
        return Vector3.Dot(velocity, transform.forward);
    }

    protected override void RotateWheel(Transform wheel, float delta)
    {
        wheel.Rotate(Vector3.right * delta, Space.Self);
    }
}
