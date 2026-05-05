using UnityEngine;

public class BikeWheelAnimator : MonoBehaviour
{
    [SerializeField] Transform[] wheelMeshes;
    [SerializeField] float wheelRadius = 0.32f;
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

        // Skip distant SUMO NPC wheel animation. Nearby NPCs and the ego vehicle
        // still animate normally.
        if (vehicleController != null && !vehicleController.IsHighDetail)
            return;

        Vector3 velocity;
        if (vehicleController != null && !vehicleController.IsDetached)
            velocity = vehicleController.EstimatedVelocity;
        else if (rb != null)
            velocity = rb.linearVelocity;
        else
            return;

        float speed = Vector3.Dot(velocity, transform.forward);
        if (Mathf.Abs(speed) < minSpeedToAnimate)
            return;

        float delta = (speed / wheelCirc) * 360f * Time.deltaTime;
        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            if (wheelMeshes[i] != null)
                wheelMeshes[i].Rotate(Vector3.right * delta, Space.Self);
        }
    }
}
