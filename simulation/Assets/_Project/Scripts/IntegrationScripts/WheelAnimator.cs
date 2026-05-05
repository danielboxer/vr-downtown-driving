using UnityEngine;

public class WheelAnimator : MonoBehaviour
{
    [SerializeField] Transform[] wheelMeshes;
    [SerializeField] float wheelRadius = 0.32f;   // metres
    [SerializeField] float minSpeedToAnimate = 0.05f;

    Rigidbody rb;
    VehicleController vehicleController;
    bool vehicleControllerLookupDone;
    bool rigidbodyLookupDone;
    float wheelCirc;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
        vehicleController = GetComponentInParent<VehicleController>();
        wheelCirc = 2f * Mathf.PI * wheelRadius;
    }

    void Update()
    {
        // Some vehicle prefabs receive VehicleController/Rigidbody at pool creation
        // time, after child wheel Awake() has already run.
        if (vehicleController == null && !vehicleControllerLookupDone)
        {
            vehicleController = GetComponentInParent<VehicleController>();
            vehicleControllerLookupDone = true;
        }
        if (rb == null && !rigidbodyLookupDone)
        {
            rb = GetComponentInParent<Rigidbody>();
            rigidbodyLookupDone = true;
        }

        // SUMO NPCs are now mostly kinematic visual objects, so updating wheels
        // once per rendered frame is enough and avoids another per-vehicle FixedUpdate.
        if (vehicleController != null && !vehicleController.IsHighDetail)
            return;

        Vector3 velocity;
        Quaternion rotation;

        if (vehicleController != null && !vehicleController.IsDetached)
        {
            velocity = vehicleController.EstimatedVelocity;
            rotation = vehicleController.transform.rotation;
        }
        else if (rb != null)
        {
            velocity = rb.linearVelocity;
            rotation = rb.rotation;
        }
        else
        {
            return;
        }

        float speed = Vector3.Dot(velocity, rotation * Vector3.right);
        if (Mathf.Abs(speed) < minSpeedToAnimate)
            return;

        float delta = (speed / wheelCirc) * 360f * Time.deltaTime;
        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            if (wheelMeshes[i] != null)
                wheelMeshes[i].Rotate(0f, delta, 0f, Space.Self);
        }
    }
}
