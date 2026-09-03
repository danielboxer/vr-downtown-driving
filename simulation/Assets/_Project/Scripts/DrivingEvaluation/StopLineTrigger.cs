using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class StopLineTrigger : MonoBehaviour
{
    [Tooltip("SUMO junction ID this stop line belongs to.")]
    public string junctionId;

    [Tooltip("Link index within the junction's state string (0-based).")]
    public int linkIndex;

    public static event System.Action<StopLineTrigger, Collider> OnEgoCrossedStopLine;

    private void OnTriggerEnter(Collider other)
    {
        // Only fire for the ego vehicle (tagged "Player") to ignore NPC traffic
        if (!other.attachedRigidbody || !other.attachedRigidbody.CompareTag("Player"))
            return;

        OnEgoCrossedStopLine?.Invoke(this, other);
    }
}
