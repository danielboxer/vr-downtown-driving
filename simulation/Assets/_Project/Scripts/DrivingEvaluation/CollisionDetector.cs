using UnityEngine;

/// <summary>
/// Attach to each ego vehicle. Detects collisions and fires a static event
/// so DrivingEvaluator can log them. Includes a speed threshold and cooldown
/// to filter out minor scrapes and sustained-contact spam.
/// On impact, detaches hit NPC vehicles from SUMO control and applies physics force.
/// </summary>
public class CollisionDetector : MonoBehaviour
{
    [Tooltip("Minimum relative impact speed (m/s) to register as a collision.")]
    public float minImpactSpeed = 1f;

    [Tooltip("Seconds to wait before logging another collision (prevents spam from sustained contact).")]
    public float cooldown = 2f;

    [Tooltip("Force multiplier applied to NPC vehicles on collision.")]
    public float impactForceMultiplier = 1.5f;

    /// <summary>Raised when the ego vehicle collides with something above the speed threshold.</summary>
    public static event System.Action<CollisionDetector, Collision> OnEgoCollision;

    private Rigidbody _egoRb;
    private float _lastCollisionTime = -10f;

    private void Awake()
    {
        _egoRb = GetComponentInParent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time - _lastCollisionTime < cooldown) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        _lastCollisionTime = Time.time;
        OnEgoCollision?.Invoke(this, collision);

        // Detach hit NPC from SUMO and apply impact force
        VehicleController npc = collision.gameObject.GetComponentInParent<VehicleController>();
        if (npc != null && !npc.IsDetached)
        {
            Vector3 egoVelocity = _egoRb != null ? _egoRb.linearVelocity : Vector3.zero;
            float mass = _egoRb != null ? _egoRb.mass : 1f;
            Vector3 impulse = egoVelocity * mass * impactForceMultiplier;
            npc.Detach(impulse);
        }
    }
}
