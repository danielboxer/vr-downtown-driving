using UnityEngine;

/// <summary>
/// Moves the attached GameObject forward at a constant speed using a Rigidbody.
/// Useful for testing visual elements (wheel animation, etc.) without running SUMO.
/// Attach to the bike prefab root and remove/disable when done testing.
/// </summary>
public class TestMover : MonoBehaviour
{
    [Tooltip("Speed in m/s")]
    [SerializeField] float speed = 5f;

    [Tooltip("Forward axis matching VehicleController convention (X = right)")]
    [SerializeField] Vector3 forwardAxis = Vector3.right;

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
    }

    void FixedUpdate()
    {
        rb.linearVelocity = transform.rotation * forwardAxis.normalized * speed;
    }
}
