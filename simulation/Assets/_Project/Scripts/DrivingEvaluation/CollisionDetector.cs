using UnityEngine;

/// <summary>
/// Attach to each ego vehicle. Detects collisions and fires a static event
/// so DrivingEvaluator can log them. Includes a speed threshold and cooldown
/// to filter out minor scrapes and sustained-contact spam.
/// </summary>
public class CollisionDetector : MonoBehaviour
{
    [Tooltip("Minimum relative impact speed (m/s) to register as a collision.")]
    public float minImpactSpeed = 1f;

    [Tooltip("Seconds to wait before logging another collision (prevents spam from sustained contact).")]
    public float cooldown = 2f;

    /// <summary>Raised when the ego vehicle collides with something above the speed threshold.</summary>
    public static event System.Action<CollisionDetector, Collision> OnEgoCollision;

    private float _lastCollisionTime = -10f;

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time - _lastCollisionTime < cooldown) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        _lastCollisionTime = Time.time;
        OnEgoCollision?.Invoke(this, collision);
    }
}
