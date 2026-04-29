using UnityEngine;

public class BikeWheelAnimator : MonoBehaviour
{
    [SerializeField] Transform[] wheelMeshes;
    [SerializeField] float wheelRadius = 0.32f;

    Rigidbody rb;
    float wheelCirc;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
        wheelCirc = 2f * Mathf.PI * wheelRadius;
    }

    void FixedUpdate()
    {
        // Measure speed along this transform's forward so nested model rotation is accounted for.
        float speed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float delta = (speed / wheelCirc) * 360f * Time.fixedDeltaTime;

        foreach (var w in wheelMeshes)
            w.Rotate(Vector3.right * delta, Space.Self);
    }
}
