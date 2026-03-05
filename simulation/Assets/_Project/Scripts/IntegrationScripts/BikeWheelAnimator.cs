using UnityEngine;

public class BikeWheelAnimator : MonoBehaviour
{
    [SerializeField] Transform[] wheelMeshes;
    [SerializeField] float wheelRadius = 0.32f;   // metres

    [Tooltip("Local axis of the vehicle that points forward. " +
             "VehicleController drives along X, so default is right.")]
    [SerializeField] Vector3 forwardAxis = Vector3.right;

    [Tooltip("Local axis of each wheel mesh to spin around (the axle). " +
             "Try X first; switch to Z if the wheel rolls wrong.")]
    [SerializeField] Vector3 spinAxis = Vector3.right;  // local X = typical axle

    Rigidbody rb;
    float wheelCirc;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
        wheelCirc = 2f * Mathf.PI * wheelRadius;
    }

    void FixedUpdate()
    {
        float speed = Vector3.Dot(rb.linearVelocity, rb.rotation * forwardAxis.normalized);
        float delta = (speed / wheelCirc) * 360f * Time.fixedDeltaTime;

        foreach (var w in wheelMeshes)
            w.Rotate(spinAxis.normalized * delta, Space.Self);
    }
}
