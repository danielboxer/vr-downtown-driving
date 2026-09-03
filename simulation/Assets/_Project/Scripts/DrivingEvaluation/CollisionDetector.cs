using UnityEngine;

public class CollisionDetector : MonoBehaviour
{
    [Tooltip("Minimum relative impact speed (m/s) to register as a collision.")]
    public float minImpactSpeed = 1f;

    [Tooltip("Seconds to wait before logging another collision (prevents spam from sustained contact).")]
    public float cooldown = 2f;

    [Tooltip("Force multiplier applied to NPC vehicles on collision.")]
    public float impactForceMultiplier = 1.5f;

    [Tooltip("Maximum impulse magnitude (Ns) to prevent NPCs from flying away.")]
    public float maxImpulseMagnitude = 2000f;

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

        VehicleController npc = collision.gameObject.GetComponentInParent<VehicleController>();
        if (npc != null && !npc.IsDetached)
        {
            Vector3 relVel = collision.relativeVelocity;
            float egoMass = _egoRb != null ? _egoRb.mass : 1f;
            Rigidbody npcRb = npc.GetComponent<Rigidbody>();
            float npcMass = npcRb != null ? npcRb.mass : 1f;

            // Scale by mass ratio so heavier NPCs move less
            float massRatio = egoMass / (egoMass + npcMass);
            Vector3 impulse = relVel * egoMass * massRatio * impactForceMultiplier;

            // Cap impulse to prevent launch-into-orbit
            if (impulse.magnitude > maxImpulseMagnitude)
                impulse = impulse.normalized * maxImpulseMagnitude;

            // at the contact point so the NPC spins
            Vector3 contactPoint = collision.contacts.Length > 0
                ? collision.contacts[0].point
                : npc.transform.position;

            npc.Detach(impulse, contactPoint);
        }
    }
}
