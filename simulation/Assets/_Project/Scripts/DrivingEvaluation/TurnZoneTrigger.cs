using UnityEngine;

/// <summary>
/// Place this on a trigger collider before a turn.
/// When the ego vehicle enters, it raises a static event so
/// DrivingEvaluator can check whether the turn signal was already on.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TurnZoneTrigger : MonoBehaviour
{
    public enum TurnDirection { Left, Right }

    [Tooltip("Which direction the driver should be signalling.")]
    public TurnDirection requiredSignal = TurnDirection.Right;

    /// <summary>Raised when the ego vehicle enters the turn zone.</summary>
    public static event System.Action<TurnZoneTrigger, Collider> OnEgoEnteredTurnZone;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.attachedRigidbody || !other.attachedRigidbody.CompareTag("Player"))
            return;

        OnEgoEnteredTurnZone?.Invoke(this, other);
    }
}
