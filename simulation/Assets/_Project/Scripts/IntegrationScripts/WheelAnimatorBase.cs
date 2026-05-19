using UnityEngine;

public abstract class WheelAnimatorBase : MonoBehaviour
{
    [SerializeField] protected Transform[] wheelMeshes;
    [SerializeField] protected float wheelRadius = 0.32f;
    [SerializeField] protected float minSpeedToAnimate = 0.05f;

    protected Rigidbody rb;
    protected VehicleController vehicleController;
    bool vehicleControllerLookupDone;
    bool rigidbodyLookupDone;
    protected float wheelCirc;

    protected virtual void Awake()
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

        if (vehicleController != null && !vehicleController.IsHighDetail)
            return;

        Vector3 velocity = GetVelocity();
        if (velocity == Vector3.zero)
            return;

        float speed = ComputeSpeed(velocity);
        if (Mathf.Abs(speed) < minSpeedToAnimate)
            return;

        float delta = (speed / wheelCirc) * 360f * Time.deltaTime;
        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            if (wheelMeshes[i] != null)
                RotateWheel(wheelMeshes[i], delta);
        }
    }

    protected Vector3 GetVelocity()
    {
        if (vehicleController != null && !vehicleController.IsDetached)
            return vehicleController.EstimatedVelocity;
        if (rb != null)
            return rb.linearVelocity;
        return Vector3.zero;
    }

    protected abstract float ComputeSpeed(Vector3 velocity);
    protected abstract void RotateWheel(Transform wheel, float delta);
}
