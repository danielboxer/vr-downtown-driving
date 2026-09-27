using UnityEngine;

public static class VrFastSpeed
{
    private const float MinHeadingSqr = 0.01f;
    private const float RampSeconds = 0.1f;

    public static void Apply(Rigidbody rb, float maxSpeedMs, Vector3 forward)
    {
        Vector3 v = rb.linearVelocity;
        Vector3 heading = new Vector3(v.x, 0f, v.z);
        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
        bool moving = heading.sqrMagnitude > MinHeadingSqr;
        // still rolling the other way after a gear change
        if (moving && Vector3.Dot(heading, flatForward) < 0f)
            return;

        Vector3 dir = moving ? heading.normalized : flatForward;
        float rate = maxSpeedMs / RampSeconds;
        float speed = Mathf.MoveTowards(heading.magnitude, maxSpeedMs, rate * Time.fixedDeltaTime);
        Vector3 planar = dir * speed;
        rb.linearVelocity = new Vector3(planar.x, v.y, planar.z);
    }
}
