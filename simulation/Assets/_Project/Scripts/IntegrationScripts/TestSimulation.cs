using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight SUMO replacement for testing.
/// Spawns vehicles from prefab lists and drives them along simple paths
/// using the same VehicleController pipeline that the real simulation uses.
/// Disable SimulationController + ExchangeData when using this.
/// </summary>
public class TestSimulation : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────────

    [Header("Step timing (match your SimulationController setting)")]
    [Tooltip("Seconds between position updates, mirrors SUMO step length.")]
    public float stepLength = 0.10f;

    [Header("Vehicles to spawn")]
    public List<TestVehicleEntry> vehicles = new();

    [Serializable]
    public class TestVehicleEntry
    {
        public string label = "test_vehicle";
        public GameObject prefab;
        public Vector3 spawnPosition;
        [Tooltip("Y-rotation in degrees at spawn.")]
        public float spawnHeading = 0f;
        [Tooltip("Forward speed in m/s.")]
        public float speed = 8f;
        [Tooltip("Optional: list of world-space waypoints. " +
                 "If empty the vehicle drives straight forever.")]
        public List<Vector3> waypoints = new();
    }

    // ──────────────────────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────────────────────

    struct LiveVehicle
    {
        public GameObject go;
        public VehicleController vc;
        public TestVehicleEntry cfg;
        public int nextWp;
        public Vector3 pos;
        public float heading;
    }

    readonly List<LiveVehicle> live = new();
    float timer;

    void Start()
    {
        foreach (var entry in vehicles)
        {
            if (entry.prefab == null) continue;

            var rot = Quaternion.Euler(0, entry.spawnHeading, 0);
            var go = Instantiate(entry.prefab, entry.spawnPosition, rot);
            go.name = entry.label;

            var vc = go.GetComponent<VehicleController>();
            if (vc == null) vc = go.AddComponent<VehicleController>();

            // initial update so VehicleController has valid data
            vc.UpdateTarget(entry.spawnPosition, rot, entry.speed, 0f, 0f);

            live.Add(new LiveVehicle
            {
                go = go,
                vc = vc,
                cfg = entry,
                nextWp = 0,
                pos = entry.spawnPosition,
                heading = entry.spawnHeading
            });
        }
    }

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;
        if (timer < stepLength) return;
        timer -= stepLength;

        for (int i = 0; i < live.Count; i++)
        {
            var v = live[i];
            float dt = stepLength;

            // Determine target heading
            if (v.cfg.waypoints != null && v.cfg.waypoints.Count > 0)
            {
                Vector3 target = v.cfg.waypoints[v.nextWp];
                Vector3 toTarget = target - v.pos;
                toTarget.y = 0;

                if (toTarget.magnitude < 1.5f)
                {
                    v.nextWp = (v.nextWp + 1) % v.cfg.waypoints.Count;
                    target = v.cfg.waypoints[v.nextWp];
                    toTarget = target - v.pos;
                    toTarget.y = 0;
                }

                if (toTarget.sqrMagnitude > 0.001f)
                {
                    // SUMO angle convention: 0 = east, CCW. Convert to Unity Y-rotation.
                    float desiredHeading = Mathf.Atan2(toTarget.z, toTarget.x) * Mathf.Rad2Deg;
                    // Smooth turn
                    v.heading = Mathf.MoveTowardsAngle(v.heading, desiredHeading, 90f * dt);
                }
            }

            // Advance position along heading (SUMO forward = Unity +X after rotation)
            float rad = v.heading * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad));
            v.pos += forward * v.cfg.speed * dt;

            // Convert heading to Unity rotation (matches SimulationController's angle-90)
            Quaternion rot = Quaternion.Euler(0, -v.heading + 90f, 0);
            v.vc.UpdateTarget(v.pos, rot, v.cfg.speed, 0f, 0f);

            live[i] = v;
        }
    }

    void OnDestroy()
    {
        foreach (var v in live)
        {
            if (v.go != null) Destroy(v.go);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Scene gizmos — visualize waypoints in editor
    // ──────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (vehicles == null) return;
        foreach (var entry in vehicles)
        {
            if (entry.waypoints == null || entry.waypoints.Count < 2) continue;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < entry.waypoints.Count; i++)
            {
                int next = (i + 1) % entry.waypoints.Count;
                Gizmos.DrawLine(entry.waypoints[i], entry.waypoints[next]);
                Gizmos.DrawSphere(entry.waypoints[i], 0.3f);
            }
        }
    }
}
