using UnityEngine;

/// <summary>
/// Attached to an invisible trigger collider at a traffic light stop line.
/// When the ego vehicle enters, raises an event so DrivingEvaluator can
/// check whether the light was red and whether the turn signal was on.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class StopLineTrigger : MonoBehaviour
{
    public enum RequiredSignal { None, Left, Right }

    [Tooltip("SUMO junction ID this stop line belongs to.")]
    public string junctionId;

    [Tooltip("Link index within the junction's state string (0-based).")]
    public int linkIndex;

    [Tooltip("Which turn signal should be active when crossing this stop line. None = no signal check.")]
    public RequiredSignal requiredSignal = RequiredSignal.None;

    /// <summary>Raised when the ego vehicle enters the trigger.</summary>
    public static event System.Action<StopLineTrigger, Collider> OnEgoCrossedStopLine;

    private void OnTriggerEnter(Collider other)
    {
        // Only fire for the ego vehicle (tagged "Player") to ignore NPC traffic
        if (!other.attachedRigidbody || !other.attachedRigidbody.CompareTag("Player"))
            return;

        OnEgoCrossedStopLine?.Invoke(this, other);
    }
}
