using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TurnDirectionTrigger : MonoBehaviour
{
    [Tooltip("SUMO junction ID this trigger belongs to.")]
    public string junctionId;

    [Tooltip("Road approach direction used by DrivingEvaluator for turn-signal checks.")]
    public Vector3 approachDir;

    public static event System.Action<TurnDirectionTrigger, Collider> OnEgoCrossedTurnTrigger;

    private void OnTriggerEnter(Collider other)
    {
        // Only fire for the ego vehicle (tagged "Player") to ignore NPC traffic
        if (!other.attachedRigidbody || !other.attachedRigidbody.CompareTag("Player"))
            return;

        OnEgoCrossedTurnTrigger?.Invoke(this, other);
    }
}
