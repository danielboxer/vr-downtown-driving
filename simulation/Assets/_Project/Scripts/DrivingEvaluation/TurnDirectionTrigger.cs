using UnityEngine;

/// <summary>
/// Placed on an exit road after a traffic light junction.
/// When the ego vehicle enters, raises an event so DrivingEvaluator can
/// check whether the appropriate turn signal was active.
/// Generated automatically by RoadNetworkBuilder for left and right exits.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class TurnDirectionTrigger : MonoBehaviour
{
    [Tooltip("SUMO junction ID this trigger belongs to.")]
    public string junctionId;

    [Tooltip("Turn direction this trigger represents.")]
    public DrivingEvaluator.SignalDirection direction;

    [Tooltip("The road approach direction (world space) this trigger was built for. " +
             "Used by DrivingEvaluator to classify right/left/wrong-way via dot product.")]
    public Vector3 approachDir;

    /// <summary>Raised when the ego vehicle enters the trigger.</summary>
    public static event System.Action<TurnDirectionTrigger, Collider> OnEgoCrossedTurnTrigger;

    private void OnTriggerEnter(Collider other)
    {
        // Only fire for the ego vehicle (tagged "Player") to ignore NPC traffic
        if (!other.attachedRigidbody || !other.attachedRigidbody.CompareTag("Player"))
            return;

        OnEgoCrossedTurnTrigger?.Invoke(this, other);
    }
}
