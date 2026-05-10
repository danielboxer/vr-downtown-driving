using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections.Concurrent;

public class SimulationController : MonoBehaviour
{
    private GameObject vehiclePrefab;
    private readonly Dictionary<string, GameObject> vehicleObjects = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, VehicleController> vehicleControllers = new Dictionary<string, VehicleController>();
    private readonly Dictionary<string, GameObject> vehiclePrefabById = new Dictionary<string, GameObject>();
    private readonly Dictionary<GameObject, Stack<GameObject>> vehiclePools = new Dictionary<GameObject, Stack<GameObject>>();
    private readonly HashSet<string> incomingVehicleIds = new HashSet<string>();
    private readonly List<string> vehiclesToRemove = new List<string>();

    private Transform pooledVehiclesRoot;
    private string vehicleDataJson = "{}";
    private string lastValidVehicleDataJson = "{}";
    private readonly object vehicleDataLock = new object();
    private readonly string egoVehicleId = "f_0.0";
    [HideInInspector] public GameObject egoVehicle;
    private Rigidbody egoRigidbody;
    private float long_speed;

    // Separate queue for SUMO messages avoids closure/delegate allocation per message.
    private readonly ConcurrentQueue<string> _sumoMessageQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    // Pre-allocated buffers for CollectVehicleData (avoids per-frame GC)
    private readonly Vehicle _egoVehicleBuffer = new Vehicle();
    private readonly double[] _egoPositionBuffer = new double[3];
    private readonly Vehicle[] _egoVehicleArray = new Vehicle[1];

    // Cached component references
    private ScenarioManager _scenarioManager;

    [Header("Unity Step Length (seconds)")]
    public float unityStepLength = 0.10f;

    [Header("NPC Config (optional, overrides per-field settings below)")]
    [Tooltip("Shared ScriptableObject for NPC vehicle settings. If assigned, the per-field settings below are ignored.")]
    public NpcVehicleConfig npcConfig;

    [Header("NPC Pooling")]
    [Tooltip("Reuse NPC vehicle GameObjects instead of Instantiate/Destroy every time SUMO context changes.")]
    public bool useVehiclePooling = true;
    [Tooltip("How many inactive vehicles to create per configured vehicle prefab at startup.")]
    public int prewarmVehiclesPerModel = 8;
    [Tooltip("Maximum inactive vehicles kept per prefab. Extra returns are destroyed.")]
    public int maxPoolSizePerModel = 80;

    [Header("Add all Junction GameObjects")]
    public GameObject junctions;           // drag 'Junctions' root here, or leave empty to auto-find
    private readonly Dictionary<string, GameObject> junctionCache = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, TrafficHeadCache[]> trafficHeadCache = new Dictionary<string, TrafficHeadCache[]>();
    private readonly List<StopLineCacheEntry> stopLineCache = new List<StopLineCacheEntry>();

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

    private struct StopLineCacheEntry
    {
        public string junctionId;
        public int linkIndex;
        public Vector3 position;
        public Vector3 forward;
    }

    private class TrafficHeadCache
    {
        public readonly List<GameObject> greenLights = new List<GameObject>();
        public readonly List<GameObject> yellowLights = new List<GameObject>();
        public readonly List<GameObject> redLights = new List<GameObject>();
    }

    [Header("Add Unity Vehicle Prefab (3DModel) according to Sumo Vehicle Type")]
    public List<CarModel> carModelsList = new List<CarModel>();

    /// cache last seen state per junction
    private readonly Dictionary<string, string> _lastTlState = new Dictionary<string, string>();

    /// <summary>
    /// Returns the full traffic light state string for the given junction,
    /// or null if no state has been received yet.
    /// Each character is one signal head: 'G'/'g' = green, 'y'/'Y' = yellow, 'r'/'R' = red.
    /// </summary>
    public string GetTrafficLightState(string junctionId)
    {
        return _lastTlState.TryGetValue(junctionId, out var state) ? state : null;
    }

    /// <summary>
    /// Cached replacement for VehicleController's old per-NPC junction/stop-line hierarchy scan.
    /// </summary>
    public bool IsNpcWaitingAtRedOrYellowLight(Vector3 npcPos, Vector3 npcDriveDir, float maxDist)
    {
        if (stopLineCache.Count == 0 || _lastTlState.Count == 0)
            return false;

        float maxDistSqr = maxDist * maxDist;
        float closestDistSqr = float.MaxValue;
        bool closestIsRedOrYellow = false;

        for (int i = 0; i < stopLineCache.Count; i++)
        {
            StopLineCacheEntry sl = stopLineCache[i];
            if (!_lastTlState.TryGetValue(sl.junctionId, out string state))
                continue;

            Vector3 toSl = sl.position - npcPos;
            float distSqr = toSl.sqrMagnitude;
            if (distSqr > maxDistSqr || distSqr < 0.0001f)
                continue;

            // NPC must be on the same approach as this stop line.
            if (Vector3.Dot(npcDriveDir, sl.forward) < 0.5f)
                continue;

            // NPC must be behind or at the stop line, not past it.
            float invDist = 1f / Mathf.Sqrt(distSqr);
            if (Vector3.Dot(npcDriveDir, toSl * invDist) < -0.2f)
                continue;

            if (distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closestIsRedOrYellow = false;
                if (sl.linkIndex >= 0 && sl.linkIndex < state.Length)
                {
                    char c = state[sl.linkIndex];
                    closestIsRedOrYellow = c == 'r' || c == 'R' || c == 'y' || c == 'Y';
                }
            }
        }

        return closestIsRedOrYellow;
    }

    private void Start()
    {
        vehiclePrefab = Resources.Load("Cars/EloraGold") as GameObject;

        if (vehiclePrefab == null)
        {
            Debug.LogError("Vehicle prefab 'EloraGold' not found in Resources/Cars.");
            return;
        }

        _scenarioManager = GetComponent<ScenarioManager>();

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

        BuildStopLineCache();
        CreatePoolRoot();
        PrewarmVehiclePools();

        // Open log file in Results folder
        //string sumoDataDir = LocateOrCreateResultsFolder();
        //string logPath = Path.Combine(sumoDataDir, "vehicle_data_report.txt");
        //writer = new StreamWriter(logPath, append: false, Encoding.UTF8);
        //writer.WriteLine("timestep_time;vehicle_id;vehicle_x;vehicle_y;vehicle_z");
    }

    /// <summary>
    /// Called by ScenarioManager to register a pre-placed ego vehicle from the scene.
    /// </summary>
    public void RegisterEgoVehicle(GameObject ego)
    {
        egoVehicle = ego;
        egoRigidbody = egoVehicle != null ? egoVehicle.GetComponent<Rigidbody>() : null;
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

            // Drain SUMO messages (no closure allocation per message)
            while (_sumoMessageQueue.TryDequeue(out string msg))
                HandleMessage(msg);

            // Drain any other main-thread actions
            while (mainThreadActions.TryDequeue(out var action))
                action();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in Update(): {ex.Message}\n{ex.StackTrace}");
        }
    }

    public string CollectVehicleData()
    {
        if (!vehicleObjects.ContainsKey(egoVehicleId) || egoVehicle == null)
        {
            // Return the last valid ego data so Python keeps calling moveToXY
            // and SUMO does not remove f_0.0 from the simulation.
            return lastValidVehicleDataJson;
        }

        if (egoRigidbody == null)
            egoRigidbody = egoVehicle.GetComponent<Rigidbody>();

        Vector3 egoVelocity = egoRigidbody != null ? egoRigidbody.linearVelocity : Vector3.zero;
        long_speed = egoVelocity.magnitude;

        Vector3 position = egoVehicle.transform.position;
        float unroundangle = egoVehicle.transform.rotation.eulerAngles.y;

        _egoPositionBuffer[0] = Math.Round(position.x, 2);
        _egoPositionBuffer[1] = Math.Round(position.z, 2);
        _egoPositionBuffer[2] = Math.Round(position.y, 2);

        _egoVehicleBuffer.vehicle_id = egoVehicleId;
        _egoVehicleBuffer.position = _egoPositionBuffer;
        _egoVehicleBuffer.angle = Math.Round(unroundangle, 2);
        _egoVehicleBuffer.type = "ego";
        _egoVehicleBuffer.long_speed = (float)Math.Round(long_speed, 2);
        _egoVehicleBuffer.vert_speed = (float)Math.Round(egoVelocity.y, 2);
        _egoVehicleBuffer.lat_speed = (float)Math.Round(egoVelocity.z, 2);

        _egoVehicleArray[0] = _egoVehicleBuffer;
        string jsonData = JsonHelper.ToJson(_egoVehicleArray);
        lastValidVehicleDataJson = jsonData;
        return jsonData;
    }

    public string GetVehicleDataJson()
    {
        lock (vehicleDataLock)
        {
            return vehicleDataJson;
        }
    }

    public void EnqueueOnMainThread(string message)
    {
        _sumoMessageQueue.Enqueue(message);
    }

    public void EnqueueMainThreadAction(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    /// <summary>
    /// Removes all NPC vehicles from the scene (returns them to pool or destroys).
    /// Does not touch the ego vehicle. Call on scenario change or simulation restart.
    /// </summary>
    public void ClearAllNpcVehicles()
    {
        vehiclesToRemove.Clear();
        foreach (string id in vehicleObjects.Keys)
        {
            if (id != egoVehicleId)
                vehiclesToRemove.Add(id);
        }

        for (int i = 0; i < vehiclesToRemove.Count; i++)
            RemoveNpcVehicle(vehiclesToRemove[i], keepDetachedDebris: false);

        vehiclesToRemove.Clear();
    }

    // Constants for fast type detection without full JSON deserialization.
    // Python sends JSON with separators=(",",":") so the prefix is always {"type":"<type>"
    private const string TypeKeyVehicles = "\"type\":\"vehicles\"";
    private const string TypeKeyTrafficlights = "\"type\":\"trafficlights\"";
    private const string TypeKeyConfig = "\"type\":\"config\"";
    private const string TypeKeyCommand = "\"type\":\"command\"";

    public void HandleMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        // Fast path: detect message type via string search (avoids a full JSON
        // deserialization just to read the "type" field).
        if (message.IndexOf(TypeKeyVehicles, StringComparison.Ordinal) >= 0)
        {
            HandleVehiclesMessage(message);
        }
        else if (message.IndexOf(TypeKeyTrafficlights, StringComparison.Ordinal) >= 0)
        {
            HandleTrafficLightsMessage(message);
        }
        else if (message.IndexOf(TypeKeyConfig, StringComparison.Ordinal) >= 0)
        {
            ConfigMessage cfg = JsonUtility.FromJson<ConfigMessage>(message);
            if (cfg != null && !string.IsNullOrEmpty(cfg.scenario))
            {
                Debug.Log($"Received config: scenario={cfg.scenario}");
                if (_scenarioManager != null)
                    _scenarioManager.ApplyScenario(cfg.scenario);
            }
        }
        else if (message.IndexOf(TypeKeyCommand, StringComparison.Ordinal) >= 0)
        {
            CommonMessage common = JsonUtility.FromJson<CommonMessage>(message);
            if (common != null)
                HandleCommandMessage(common.command);
        }
        else
        {
            Debug.LogWarning("Received message with unknown type: " + message);
        }
    }

    private void HandleCommandMessage(string command)
    {
        if (command == "START_RECORDING")
        {
            RecordingManager.startRecordingFromZero = true;
            RecordingManager.recordingStartTime = Time.time;
            Debug.Log("Received START_RECORDING command from SUMO. Starting logs from zero now.");
        }
        else if (command == "STOP_RECORDING")
        {
            RecordingManager.startRecordingFromZero = false;
            Debug.Log("Received STOP_RECORDING command from SUMO. Stopping logs.");

            // Remove all surrounding cars except ego
            vehiclesToRemove.Clear();
            foreach (string vid in vehicleObjects.Keys)
            {
                if (vid != egoVehicleId)
                    vehiclesToRemove.Add(vid);
            }

            for (int i = 0; i < vehiclesToRemove.Count; i++)
                RemoveNpcVehicle(vehiclesToRemove[i], keepDetachedDebris: false);
        }
    }

    private void HandleTrafficLightsMessage(string message)
    {
        var wrapper = JsonUtility.FromJson<TrafficLightsWrapper>(message);
        if (wrapper == null || wrapper.lights == null) return;

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

    private void HandleVehiclesMessage(string message)
    {
        VehicleWrapper wrapper = JsonUtility.FromJson<VehicleWrapper>(message);
        Vehicle[] vehiclesData = wrapper != null && wrapper.vehicles != null ? wrapper.vehicles : Array.Empty<Vehicle>();

        incomingVehicleIds.Clear();
        for (int i = 0; i < vehiclesData.Length; i++)
        {
            Vehicle vehicle = vehiclesData[i];
            if (vehicle != null && !string.IsNullOrEmpty(vehicle.vehicle_id))
                incomingVehicleIds.Add(vehicle.vehicle_id);
        }

        vehiclesToRemove.Clear();
        foreach (string id in vehicleObjects.Keys)
        {
            if (id != egoVehicleId && !incomingVehicleIds.Contains(id))
                vehiclesToRemove.Add(id);
        }

        for (int i = 0; i < vehiclesToRemove.Count; i++)
            RemoveNpcVehicle(vehiclesToRemove[i], keepDetachedDebris: true);

        for (int i = 0; i < vehiclesData.Length; i++)
        {
            Vehicle vehicle = vehiclesData[i];
            if (vehicle == null || string.IsNullOrEmpty(vehicle.vehicle_id))
                continue;

            if (vehicle.vehicle_id == egoVehicleId)
                continue;

            Vector3 newPosition = new Vector3((float)vehicle.position[0], (float)vehicle.position[2], (float)vehicle.position[1]);
            Quaternion newRotation = Quaternion.Euler(0, (float)vehicle.angle - 90f, 0);
            float vehicleSpeed = vehicle.long_speed;
            float vehicleVerticalSpeed = vehicle.vert_speed;
            float vehicleLateralSpeed = vehicle.lat_speed;

            if (vehicleObjects.TryGetValue(vehicle.vehicle_id, out GameObject existingVehicle))
            {
                if (vehicleControllers.TryGetValue(vehicle.vehicle_id, out VehicleController vehicleController)
                    && vehicleController != null
                    && !vehicleController.IsDetached)
                {
                    vehicleController.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehicleVerticalSpeed, vehicleLateralSpeed);
                }
            }
            else
            {
                GameObject prefabToInstantiate = ResolveVehiclePrefab(vehicle.type);
                if (prefabToInstantiate == null)
                {
                    Debug.LogWarning($"No valid prefab for vehicle type '{vehicle.type}' (id: {vehicle.vehicle_id}), skipping.");
                    continue;
                }

                GameObject newVehicle = BorrowVehicleFromPool(prefabToInstantiate, vehicle.vehicle_id, newPosition, newRotation);
                VehicleController vc = newVehicle.GetComponent<VehicleController>();
                if (vc == null)
                    vc = newVehicle.AddComponent<VehicleController>();

                ApplyNpcSettings(vc);
                vc.ResetForSumoControl(newPosition, newRotation, vehicleSpeed, vehicleVerticalSpeed, vehicleLateralSpeed);

                vehicleObjects.Add(vehicle.vehicle_id, newVehicle);
                vehicleControllers.Add(vehicle.vehicle_id, vc);
                vehiclePrefabById.Add(vehicle.vehicle_id, prefabToInstantiate);
            }
        }
    }

    private void ChangeTrafficStatus(string junctionID, string state)
    {
        if (junctions == null || string.IsNullOrEmpty(junctionID) || string.IsNullOrEmpty(state)) return;

        if (!junctionCache.TryGetValue(junctionID, out GameObject junctionGO))
        {
            var t = junctions.transform.Find(junctionID);
            if (t == null) { Debug.LogWarning($"Junction {junctionID} not found"); return; }
            junctionGO = t.gameObject;
            junctionCache[junctionID] = junctionGO;
        }

        TrafficHeadCache[] heads = GetOrBuildTrafficHeadCache(junctionID, junctionGO, state.Length);

        for (int i = 0; i < state.Length && i < heads.Length; i++)
        {
            TrafficHeadCache head = heads[i];
            if (head == null) continue;
            SetSignalState(state[i], head);
        }
    }

    private void SetSignalState(char c, TrafficHeadCache head)
    {
        bool isGreen = (c == 'G' || c == 'g');
        bool isYellow = (c == 'y' || c == 'Y');
        bool isRed = !(isGreen || isYellow);

        SetActiveIfDifferent(head.greenLights, isGreen);
        SetActiveIfDifferent(head.yellowLights, isYellow);
        SetActiveIfDifferent(head.redLights, isRed);
    }

    private static void SetActiveIfDifferent(List<GameObject> objects, bool active)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject go = objects[i];
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }
    }

    private TrafficHeadCache[] GetOrBuildTrafficHeadCache(string junctionID, GameObject junctionGO, int stateLength)
    {
        if (trafficHeadCache.TryGetValue(junctionID, out TrafficHeadCache[] cached)
            && cached != null
            && cached.Length >= stateLength)
        {
            return cached;
        }

        int cacheLength = Mathf.Max(stateLength, 0);
        TrafficHeadCache[] heads = new TrafficHeadCache[cacheLength];
        for (int i = 0; i < cacheLength; i++)
        {
            Transform headTransform = junctionGO.transform.Find($"Head{i}");
            if (headTransform == null)
            {
                Debug.LogWarning($"  Head{i} not found under {junctionID}");
                continue;
            }

            TrafficHeadCache cache = new TrafficHeadCache();
            FindChildrenRecursive(headTransform, "green_light", cache.greenLights);
            FindChildrenRecursive(headTransform, "yellow_light", cache.yellowLights);
            FindChildrenRecursive(headTransform, "red_light", cache.redLights);
            heads[i] = cache;
        }

        trafficHeadCache[junctionID] = heads;
        return heads;
    }

    private void BuildStopLineCache()
    {
        stopLineCache.Clear();
        if (junctions == null) return;

        StopLineTrigger[] triggers = junctions.GetComponentsInChildren<StopLineTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            StopLineTrigger trigger = triggers[i];
            if (trigger == null) continue;

            string junctionId = !string.IsNullOrEmpty(trigger.junctionId)
                ? trigger.junctionId
                : FindNearestJunctionName(trigger.transform);

            if (string.IsNullOrEmpty(junctionId))
                continue;

            stopLineCache.Add(new StopLineCacheEntry
            {
                junctionId = junctionId,
                linkIndex = trigger.linkIndex,
                position = trigger.transform.position,
                forward = trigger.transform.forward
            });
        }

        Debug.Log($"[SimulationController] Cached {stopLineCache.Count} stop-line triggers.");
    }

    private string FindNearestJunctionName(Transform t)
    {
        if (junctions == null || t == null) return null;

        Transform cur = t;
        while (cur != null && cur.parent != null)
        {
            if (cur.parent == junctions.transform)
                return cur.name;

            cur = cur.parent;
        }

        return t.parent != null ? t.parent.name : null;
    }

    private void FindChildrenRecursive(Transform parent, string name, List<GameObject> results)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) results.Add(child.gameObject);
            FindChildrenRecursive(child, name, results);
        }
    }

    private void CreatePoolRoot()
    {
        if (pooledVehiclesRoot != null) return;

        GameObject root = new GameObject("Pooled SUMO Vehicles");
        pooledVehiclesRoot = root.transform;
    }

    private void PrewarmVehiclePools()
    {
        if (!useVehiclePooling || prewarmVehiclesPerModel <= 0) return;

        HashSet<GameObject> prefabs = new HashSet<GameObject>();
        if (vehiclePrefab != null)
            prefabs.Add(vehiclePrefab);

        for (int i = 0; i < carModelsList.Count; i++)
        {
            CarModel model = carModelsList[i];
            if (model != null && model.unityVehiclePrefab != null)
                prefabs.Add(model.unityVehiclePrefab);
        }

        foreach (GameObject prefab in prefabs)
        {
            EnsurePool(prefab);
            Stack<GameObject> pool = vehiclePools[prefab];
            for (int i = pool.Count; i < prewarmVehiclesPerModel; i++)
            {
                GameObject obj = CreatePooledVehicleInstance(prefab);
                pool.Push(obj);
            }
        }
    }

    private GameObject ResolveVehiclePrefab(string sumoVehicleType)
    {
        for (int i = 0; i < carModelsList.Count; i++)
        {
            CarModel carModel = carModelsList[i];
            if (carModel != null
                && carModel.sumoVehicleType == sumoVehicleType
                && carModel.unityVehiclePrefab != null)
            {
                return carModel.unityVehiclePrefab;
            }
        }

        return vehiclePrefab;
    }

    private GameObject BorrowVehicleFromPool(GameObject prefab, string vehicleId, Vector3 position, Quaternion rotation)
    {
        GameObject obj = null;

        if (useVehiclePooling)
        {
            EnsurePool(prefab);
            Stack<GameObject> pool = vehiclePools[prefab];
            while (pool.Count > 0 && obj == null)
                obj = pool.Pop();
        }

        if (obj == null)
            obj = GameObject.Instantiate(prefab);

        obj.name = vehicleId;
        obj.transform.SetParent(pooledVehiclesRoot, false);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    private GameObject CreatePooledVehicleInstance(GameObject prefab)
    {
        GameObject obj = GameObject.Instantiate(prefab, pooledVehiclesRoot);
        obj.name = prefab.name + "_pooled";

        VehicleController vc = obj.GetComponent<VehicleController>();
        if (vc == null)
            vc = obj.AddComponent<VehicleController>();
        ApplyNpcSettings(vc);

        obj.SetActive(false);
        return obj;
    }

    private void EnsurePool(GameObject prefab)
    {
        if (prefab == null) return;
        if (!vehiclePools.ContainsKey(prefab))
            vehiclePools.Add(prefab, new Stack<GameObject>());
    }

    private void RemoveNpcVehicle(string id, bool keepDetachedDebris)
    {
        if (!vehicleObjects.TryGetValue(id, out GameObject vehicleObj))
            return;

        vehicleObjects.Remove(id);
        vehicleControllers.TryGetValue(id, out VehicleController vc);
        vehicleControllers.Remove(id);

        vehiclePrefabById.TryGetValue(id, out GameObject prefab);
        vehiclePrefabById.Remove(id);

        if (vehicleObj == null)
            return;

        if (keepDetachedDebris && vc != null && vc.IsDetached)
            return;

        ReturnVehicleToPoolOrDestroy(vehicleObj, prefab);
    }

    private void ReturnVehicleToPoolOrDestroy(GameObject obj, GameObject prefab)
    {
        if (obj == null) return;

        if (!useVehiclePooling || prefab == null)
        {
            GameObject.Destroy(obj);
            return;
        }

        EnsurePool(prefab);
        Stack<GameObject> pool = vehiclePools[prefab];
        if (pool.Count >= Mathf.Max(0, maxPoolSizePerModel))
        {
            GameObject.Destroy(obj);
            return;
        }

        obj.name = prefab.name + "_pooled";
        obj.SetActive(false);
        obj.transform.SetParent(pooledVehiclesRoot, false);
        pool.Push(obj);
    }

    private void ApplyNpcSettings(VehicleController vc)
    {
        if (vc == null) return;

        if (npcConfig != null)
            vc.SetConfig(npcConfig);
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
