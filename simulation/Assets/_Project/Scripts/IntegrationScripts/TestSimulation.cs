using System.Collections.Generic;
using UnityEngine;

public class TestSimulation : MonoBehaviour
{

    [Header("Step timing (match your SimulationController setting)")]
    [Tooltip("Seconds between position updates, mirrors SUMO step length.")]
    public float stepLength = 0.10f;

    [Header("Spawning")]
    [Tooltip("Vehicle prefabs to choose from (cycles through the list).")]
    public List<GameObject> prefabs = new();

    [Tooltip("Seconds between each spawn.")]
    public float spawnInterval = 3f;

    [Tooltip("Forward speed in m/s for spawned vehicles.")]
    public float speed = 8f;

    [Tooltip("Seconds before each vehicle is destroyed (0 = never).")]
    public float lifetime = 15f;


    struct LiveVehicle
    {
        public GameObject go;
        public VehicleController vc;
        public float speed;
        public Vector3 pos;
        public float heading;
        public float spawnTime;
    }

    readonly List<LiveVehicle> live = new();
    float stepTimer;
    float spawnTimer;
    int prefabIndex;

    void FixedUpdate()
    {
        spawnTimer += Time.fixedDeltaTime;
        if (spawnTimer >= spawnInterval && prefabs.Count > 0)
        {
            spawnTimer -= spawnInterval;
            SpawnVehicle();
        }

        stepTimer += Time.fixedDeltaTime;
        if (stepTimer < stepLength) return;
        stepTimer -= stepLength;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var v = live[i];

            // Skip vehicles detached by collision
            if (v.vc.IsDetached) continue;

            if (lifetime > 0f && Time.time - v.spawnTime >= lifetime)
            {
                Destroy(v.go);
                live.RemoveAt(i);
                continue;
            }

            float dt = stepLength;

            float rad = v.heading * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad));
            v.pos += fwd * v.speed * dt;

            Quaternion rot = Quaternion.Euler(0, -v.heading + 90f, 0);
            v.vc.UpdateTarget(v.pos, rot, v.speed, 0f, 0f);

            live[i] = v;
        }
    }

    void SpawnVehicle()
    {
        GameObject prefab = prefabs[prefabIndex % prefabs.Count];
        prefabIndex++;
        if (prefab == null) return;

        float heading = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0, -heading + 90f, 0);
        Vector3 spawnPos = transform.position;

        GameObject go = Instantiate(prefab, spawnPos, rot);
        go.name = $"test_vehicle_{prefabIndex}";

        VehicleController vc = go.GetComponent<VehicleController>();
        if (vc == null) vc = go.AddComponent<VehicleController>();

        live.Add(new LiveVehicle
        {
            go = go,
            vc = vc,
            speed = speed,
            pos = spawnPos,
            heading = heading,
            spawnTime = Time.time
        });
    }

    void OnDestroy()
    {
        foreach (var v in live)
        {
            if (v.go != null) Destroy(v.go);
        }
    }
}
