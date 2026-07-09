using UnityEngine;

// VR comfort locomotion: ramp horizontal speed to a target over a short fixed
// window (RampSeconds) instead of snapping, so the rider spends almost no time in
// the changing-velocity state that drives sim sickness while avoiding the jarring
// jolt of an instantaneous change. The full 0..max range is covered in RampSeconds
// regardless of direction, so accelerating and braking feel equally quick. Vertical
// velocity is kept so gravity and ground contact still work; the existing horizontal
// heading is reused so steering keeps curving the motion.
public static class VrFastSpeed
{
    private const float MinHeadingSqr = 0.01f;
    private const float RampSeconds = 0.1f;

    public static void Apply(Rigidbody rb, float targetSpeedMs, float maxSpeedMs, Vector3 forward)
    {
        Vector3 v = rb.linearVelocity;
        Vector3 heading = new Vector3(v.x, 0f, v.z);
        Vector3 dir = heading.sqrMagnitude > MinHeadingSqr
            ? heading.normalized
            : new Vector3(forward.x, 0f, forward.z).normalized;
        float rate = maxSpeedMs / RampSeconds;
        float speed = Mathf.MoveTowards(heading.magnitude, targetSpeedMs, rate * Time.fixedDeltaTime);
        Vector3 planar = dir * speed;
        rb.linearVelocity = new Vector3(planar.x, v.y, planar.z);
    }
}
