using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

public class SimulationController : MonoBehaviour
{
    private GameObject vehiclePrefab;
    private Dictionary<string, GameObject> vehicleObjects = new Dictionary<string, GameObject>();
    private string vehicleDataJson = "{}";
    private object vehicleDataLock = new object();
    private string egoVehicleId = "f_0.0";
    [HideInInspector] public GameObject egoVehicle;
    private float long_speed;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    private StreamWriter writer;

    [Header("Unity Step Length (seconds)")]
    public float unityStepLength = 0.10f;

    [Header("NPC Horn Audio")]
    [Tooltip("Up to 3 short horn clips; a random one plays each honk.")]
    public List<AudioClip> npcHornClips = new List<AudioClip>();
    [Range(0f, 2f)]
    public float npcHornVolume = 1f;
    [Tooltip("Seconds an NPC must be stationary before honking.")]
    public float npcHornTriggerDelay = 3f;
    [Tooltip("Minimum seconds between honks from the same NPC.")]
    public float npcHornCooldown = 5f;
    [Tooltip("Distance in metres within which the ego car triggers honking.")]
    public float npcHornTriggerDistance = 18f;
    [Range(0f, 1f)]
    [Tooltip("Probability (0-1) that a qualifying NPC actually honks each cooldown window.")]
    public float npcHornHonkChance = 0.4f;
    [Range(0f, 1f)]
    [Tooltip("Probability (0-1) that a stopped NPC randomly honks with no ego nearby (general traffic impatience).")]
    public float npcHornAmbientChance = 0.1f;

    private float fixedTimeAccum = 0f; // Accumulator for FixedUpdate logging

    // New variables for timestamp offset
    private bool firstTimestampLogged = false;
    private float firstLoggedTime = 0f;

    // ‑‑‑‑ NEW: traffic‑light handling ‑‑‑‑
    [Header("Add all Junction GameObjects")]
    public GameObject junctions;           // drag 'Junctions' root here, or leave empty to auto-find
    private readonly Dictionary<string, GameObject> junctionCache = new();

    [Serializable]
    public class Vehicle
    {
        public string vehicle_id;
        public double[] position;
        public double angle;
        public string type;
        public float long_speed;
        public float vert_speed;
        public float lat_speed;
    }

    [Serializable]
    private class VehicleWrapper
    {
        public Vehicle[] vehicles;
    }

    [Serializable]
    public class TrafficLight
    {
        public string junction_id;
        public string state;
    }

    [Serializable]
    private class TrafficLightsWrapper
    {
        public TrafficLight[] lights;
    }

    [System.Serializable]
    public class CarModel
    {
        public string sumoVehicleType;
        public GameObject unityVehiclePrefab;
    }

    [Serializable]
    private class ConfigMessage
    {
        public string type;
        public string scenario;
    }

    [Header("Add Unity Vehicle Prefab (3DModel) according to Sumo Vehicle Type")]
    public List<CarModel> carModelsList = new List<CarModel>();

    // ── new fields ─────────────────────────────────────────────
    /// cache last seen state per junction
    private Dictionary<string, string> _lastTlState = new();

    /// <summary>
    /// Returns the full traffic light state string for the given junction,
    /// or null if no state has been received yet.
    /// Each character is one signal head: 'G'/'g' = green, 'y'/'Y' = yellow, 'r'/'R' = red.
    /// </summary>
    public string GetTrafficLightState(string junctionId)
    {
        return _lastTlState.TryGetValue(junctionId, out var state) ? state : null;
    }

    /// <summary>Finds (or creates) SUMO2Unity\Results next to the project.</summary>
    private static string LocateOrCreateResultsFolder()
    {
        // projectRoot = folder that *contains* "Assets"
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        DirectoryInfo dir = new DirectoryInfo(projectRoot);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Results");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;                       // walk upward
        }

        // Not found – create it next to the project
        string fallback = Path.Combine(projectRoot, "Results");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private void Start()
    {
        vehiclePrefab = Resources.Load("EloraGold") as GameObject;

        if (vehiclePrefab == null)
        {
            Debug.LogError("Vehicle prefab 'EloraGold' not found in Resources.");
            return;
        }

        // Ensure ExchangeData component is present (added here if not already on the GameObject)
        if (GetComponent<ExchangeData>() == null)
            gameObject.AddComponent<ExchangeData>();

        // Auto-find the Junctions root if not assigned in the Inspector
        if (junctions == null)
        {
            var found = GameObject.Find("Junctions");
            if (found != null)
                junctions = found;
            else
                Debug.LogWarning("[SimulationController] 'Junctions' GameObject not found in scene. Traffic lights will not update.");
        }

        // 3) open log file in SUMOData folder
        string sumoDataDir = LocateOrCreateResultsFolder();
        string logPath = Path.Combine(sumoDataDir, "vehicle_data_report.txt");
        writer = new StreamWriter(logPath, append: false, Encoding.UTF8);
        writer.WriteLine("timestep_time;vehicle_id;vehicle_x;vehicle_y;vehicle_z");
    }

    /// <summary>
    /// Called by ScenarioManager to register a pre-placed ego vehicle from the scene.
    /// </summary>
    public void RegisterEgoVehicle(GameObject ego)
    {
        egoVehicle = ego;
        egoVehicle.name = egoVehicleId;
        if (!vehicleObjects.ContainsKey(egoVehicleId))
            vehicleObjects.Add(egoVehicleId, egoVehicle);
        else
            vehicleObjects[egoVehicleId] = egoVehicle;
    }

    void Update()
    {
        try
        {
            string data = CollectVehicleData();
            lock (vehicleDataLock)
            {
                vehicleDataJson = data;
            }

            while (mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in Update(): {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void FixedUpdate()
    {
        // Only log if we have started and not stopped recording
        if (!RecordingManager.startRecordingFromZero)
        {
            return;
        }

        fixedTimeAccum += Time.fixedDeltaTime;
        if (fixedTimeAccum >= unityStepLength - 0.002)
        {
            float currentTime = Time.fixedTime;

            // If this is the first timestamp we log, record it as the start
            if (!firstTimestampLogged)
            {
                firstLoggedTime = currentTime;
                firstTimestampLogged = true;
            }

            // Log time adjusted by first logged time
            float logTime = currentTime - firstLoggedTime;
            LogVehicleData(logTime);
            fixedTimeAccum = 0f;
        }

    }

    private void LogVehicleData(float relativeLogTime)
    {
        foreach (var kvp in vehicleObjects)
        {
            string vehicleId = kvp.Key;
            GameObject vehicleObj = kvp.Value;
            Vector3 pos = vehicleObj.transform.position;
            writer.WriteLine($"{relativeLogTime:F3};{vehicleId};{pos.x:F2};{pos.y:F2};{pos.z:F2}");
        }
    }

    private void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }

    public void EnqueueMainThreadAction(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    public string CollectVehicleData()
    {
        if (!vehicleObjects.ContainsKey(egoVehicleId))
        {
            UnityEngine.Debug.LogWarning("Ego vehicle not found. Sending empty JSON.");
            return "{}";
        }

        GameObject egoVehicle = vehicleObjects[egoVehicleId];
        long_speed = egoVehicle.GetComponent<Rigidbody>().linearVelocity.magnitude;

        Vector3 position = egoVehicle.transform.position;
        float unroundangle = egoVehicle.transform.rotation.eulerAngles.y;
        double angle = Math.Round(unroundangle, 2);
        double x = Math.Round(position.x, 2);
        double y = Math.Round(position.z, 2);
        double z = Math.Round(position.y, 2);
        string type = "ego";

        float vertical_speed = (float)Math.Round(egoVehicle.GetComponent<Rigidbody>().linearVelocity.y, 2);
        float lateral_speed = (float)Math.Round(egoVehicle.GetComponent<Rigidbody>().linearVelocity.z, 2);

        Vehicle egoVehicleData = new Vehicle();
        egoVehicleData.vehicle_id = egoVehicleId;
        egoVehicleData.position = new double[] { x, y, z };
        egoVehicleData.angle = angle;
        egoVehicleData.type = type;
        egoVehicleData.long_speed = (float)Math.Round(long_speed, 2);
        egoVehicleData.vert_speed = vertical_speed;
        egoVehicleData.lat_speed = lateral_speed;

        string jsonData = JsonHelper.ToJson(new Vehicle[] { egoVehicleData });
        return jsonData;
    }

    public string GetVehicleDataJson()
    {
        lock (vehicleDataLock)
        {
            return vehicleDataJson;
        }
    }

    public void HandleMessage(string message)
    {
        CommonMessage common = JsonUtility.FromJson<CommonMessage>(message);

        if (common == null || string.IsNullOrEmpty(common.type))
        {
            Debug.LogError("Received message with no type field or invalid JSON.");
            return;
        }

        if (common.type == "config")
        {
            ConfigMessage cfg = JsonUtility.FromJson<ConfigMessage>(message);
            Debug.Log($"Received config: scenario={cfg.scenario}");
            var scenarioManager = GetComponent<ScenarioManager>();
            if (scenarioManager != null)
                scenarioManager.ApplyScenario(cfg.scenario);
            return;
        }
        else if (common.type == "command")
        {
            if (common.command == "START_RECORDING")
            {
                RecordingManager.startRecordingFromZero = true;
                RecordingManager.recordingStartTime = Time.time;
                Debug.Log("Received START_RECORDING command from SUMO. Starting logs from zero now.");

                // Reset offset logging variables when we start recording
                firstTimestampLogged = false;
                firstLoggedTime = 0f;
            }
            else if (common.command == "STOP_RECORDING")
            {
                // Stop recording
                RecordingManager.startRecordingFromZero = false;
                Debug.Log("Received STOP_RECORDING command from SUMO. Stopping logs.");

                // Remove all surrounding cars except ego
                var nonEgoKeys = vehicleObjects.Keys.Where(k => k != egoVehicleId).ToList();
                foreach (var vid in nonEgoKeys)
                {
                    GameObject obj = vehicleObjects[vid];
                    Destroy(obj);
                    vehicleObjects.Remove(vid);
                }
            }

            return; // No further vehicle parsing needed
        }
        else if (common.type == "vehicles")
        {
            VehicleWrapper wrapper = JsonUtility.FromJson<VehicleWrapper>(message);
            Vehicle[] vehicleArray = wrapper.vehicles;
            List<Vehicle> vehiclesData = vehicleArray != null ? vehicleArray.ToList() : new List<Vehicle>();

            HashSet<string> incomingVehicleIds = new HashSet<string>(vehiclesData.Select(v => v.vehicle_id));
            var vehiclesToRemove = vehicleObjects.Keys.Where(id => !incomingVehicleIds.Contains(id) && id != egoVehicleId).ToList();

            foreach (var id in vehiclesToRemove)
            {
                GameObject vehicleToDestroy = vehicleObjects[id];
                // Keep detached (crashed) vehicles as physics debris
                VehicleController vc = vehicleToDestroy.GetComponent<VehicleController>();
                if (vc != null && vc.IsDetached)
                {
                    vehicleObjects.Remove(id);
                    continue;
                }
                GameObject.Destroy(vehicleToDestroy);
                vehicleObjects.Remove(id);
            }

            foreach (var vehicle in vehiclesData)
            {
                Vector3 newPosition = new Vector3((float)vehicle.position[0], (float)vehicle.position[2], (float)vehicle.position[1]);
                Quaternion newRotation = Quaternion.Euler(0, (float)vehicle.angle - 90f, 0);
                float vehicleSpeed = vehicle.long_speed;
                float vehiclevertical_speed = vehicle.vert_speed;
                float vehiclelateral_speed = vehicle.lat_speed;

                if (vehicle.vehicle_id == egoVehicleId)
                {
                    continue;
                }

                if (vehicleObjects.ContainsKey(vehicle.vehicle_id))
                {
                    GameObject existingVehicle = vehicleObjects[vehicle.vehicle_id];
                    VehicleController vehicleController = existingVehicle.GetComponent<VehicleController>();
                    if (vehicleController != null && !vehicleController.IsDetached)
                    {
                        vehicleController.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    }
                }
                else
                {
                    GameObject prefabToInstantiate = vehiclePrefab;
                    foreach (CarModel carModel in carModelsList)
                    {
                        if (carModel.sumoVehicleType == vehicle.type)
                        {
                            prefabToInstantiate = carModel.unityVehiclePrefab;
                            break;
                        }
                    }

                    GameObject newVehicle = GameObject.Instantiate(prefabToInstantiate, newPosition, newRotation);
                    newVehicle.name = vehicle.vehicle_id;
                    VehicleController vc = newVehicle.GetComponent<VehicleController>();
                    if (vc == null)
                    {
                        vc = newVehicle.AddComponent<VehicleController>();
                    }

                    // Pass horn settings from SimulationController so the clip
                    // only needs to be assigned in one place (the SimController inspector)
                    vc.hornClips = npcHornClips;
                    vc.hornVolume = npcHornVolume;
                    vc.hornTriggerDelay = npcHornTriggerDelay;
                    vc.hornCooldown = npcHornCooldown;
                    vc.hornTriggerDistance = npcHornTriggerDistance;
                    vc.hornHonkChance = npcHornHonkChance;
                    vc.hornAmbientChance = npcHornAmbientChance;

                    vc.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    vehicleObjects.Add(vehicle.vehicle_id, newVehicle);
                }
            }

        }
        else if (common.type == "trafficlights")
        {
            // 2) parse wrapper
            var wrapper = JsonUtility.FromJson<TrafficLightsWrapper>(message);

            foreach (var tl in wrapper.lights)
            {
                // only repaint if state actually changed
                if (!_lastTlState.TryGetValue(tl.junction_id, out var prev)
                 || prev != tl.state)
                {
                    ChangeTrafficStatus(tl.junction_id, tl.state);
                    _lastTlState[tl.junction_id] = tl.state;
                }
            }
        }
        else
        {
            Debug.LogWarning("Received message with unknown type: " + common.type);
        }
    }

    public void EnqueueOnMainThread(string message)
    {
        EnqueueMainThreadAction(() => HandleMessage(message));
    }

    private void ChangeTrafficStatus(string junctionID, string state)
    {
        if (junctions == null) return;

        // find & cache the J4 GameObject exactly as before
        if (!junctionCache.TryGetValue(junctionID, out GameObject junctionGO))
        {
            var t = junctions.transform.Find(junctionID);
            if (t == null) { Debug.LogWarning($"Junction {junctionID} not found"); return; }
            junctionGO = t.gameObject;
            junctionCache[junctionID] = junctionGO;
        }

        // now for each character in the state string
        for (int i = 0; i < state.Length; i++)
        {
            // look for the child named "Head0", "Head1", etc.
            var headTransform = junctionGO.transform.Find($"Head{i}");
            if (headTransform == null)
            {
                Debug.LogWarning($"  Head{i} not found under {junctionID}");
                continue;
            }
            SetSignalState(state[i], headTransform.gameObject);
        }
    }


    private void SetSignalState(char c, GameObject head)
    {
        bool isGreen = (c == 'G' || c == 'g');
        bool isYellow = (c == 'y' || c == 'Y');

        // Toggle all matching lights recursively (covers mirrored duplicates)
        var buf = new System.Collections.Generic.List<GameObject>();
        FindChildrenRecursive(head.transform, "green_light", buf);
        foreach (var g in buf) g.SetActive(isGreen);

        buf.Clear();
        FindChildrenRecursive(head.transform, "yellow_light", buf);
        foreach (var y in buf) y.SetActive(isYellow);

        buf.Clear();
        FindChildrenRecursive(head.transform, "red_light", buf);
        foreach (var r in buf) r.SetActive(!(isGreen || isYellow));
    }

    private void FindChildrenRecursive(Transform parent, string name, System.Collections.Generic.List<GameObject> results)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) results.Add(child.gameObject);
            FindChildrenRecursive(child, name, results);
        }
    }


    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            string newJson = "{ \"vehicles\": " + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.vehicles;
        }

        public static string ToJson<T>(T[] array)
        {
            Wrapper<T> wrapper = new Wrapper<T> { vehicles = array };
            return JsonUtility.ToJson(wrapper);
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] vehicles;
        }
    }
}