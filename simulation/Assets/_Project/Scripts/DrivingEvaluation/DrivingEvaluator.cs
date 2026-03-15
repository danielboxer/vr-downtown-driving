using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityStandardAssets.Vehicles.Car;

/// <summary>
/// Checklist evaluator for the novice driver training scenario.
/// Listens for stop-line crossings and checks traffic light state
/// and turn signal usage based on per-scenario rules configured in the Inspector.
/// Logs every evaluation event and exports a CSV report to Results/ when the scenario ends.
/// Place on the manager GameObject.
/// </summary>
public class DrivingEvaluator : MonoBehaviour
{
    public enum VehicleMode { Car, Bike }
    public enum SignalDirection { Left, Right }

    // ── Per-scenario configuration (set in Inspector) ──

    [System.Serializable]
    public class TurnSignalRule
    {
        [Tooltip("SUMO junction ID where a turn signal is expected.")]
        public string junctionId;
        public SignalDirection direction;
    }

    [System.Serializable]
    public class ScenarioEvalConfig
    {
        public ScenarioId scenario;
        [Tooltip("Check for red-light violations at every stop line.")]
        public bool checkRedLights = true;
        [Tooltip("Junctions where a turn signal must be active before the stop line.")]
        public List<TurnSignalRule> turnSignalChecks = new();
    }

    [Header("Scenario Rules")]
    [Tooltip("Configure evaluation rules per scenario. Scenarios not listed here will not be evaluated.")]
    [SerializeField] private List<ScenarioEvalConfig> scenarioRules = new();

    [Header("Speed Limit")]
    [Tooltip("Speed limit in km/h. Set to 0 to disable speed monitoring.")]
    public float speedLimitKmh = 50f;

    [Tooltip("Seconds between speeding violation log entries (prevents per-frame spam).")]
    public float speedingLogCooldown = 5f;

    // Auto-resolved references (no Inspector assignment needed)
    private SimulationController simController;
    private CarUserControl carUserControl;
    private Rigidbody _egoRb;

    [Header("Runtime State (read-only)")]
    [SerializeField] private ScenarioId _activeScenario;
    [SerializeField] private VehicleMode _vehicleMode = VehicleMode.Car;

    // ── Checklist state ──
    [Header("Checklist (read-only at runtime)")]
    [SerializeField] private bool _ranRedLight;
    [SerializeField] private bool _usedTurnSignal;
    [SerializeField] private bool _hadCollision;
    [SerializeField] private int _collisionCount;
    [SerializeField] private bool _exceededSpeedLimit;
    [SerializeField] private int _speedingEventCount;
    [SerializeField] private float _topSpeedKmh;

    /// <summary>True if the driver crossed a stop line while the light was red.</summary>
    public bool RanRedLight => _ranRedLight;

    /// <summary>True if the driver had the correct signal on before the stop line.</summary>
    public bool UsedTurnSignal => _usedTurnSignal;

    /// <summary>True if the driver collided with anything during this scenario.</summary>
    public bool HadCollision => _hadCollision;

    /// <summary>Number of collisions during this scenario.</summary>
    public int CollisionCount => _collisionCount;

    /// <summary>True if the driver exceeded the speed limit during this scenario.</summary>
    public bool ExceededSpeedLimit => _exceededSpeedLimit;

    /// <summary>Highest speed recorded during this scenario (km/h).</summary>
    public float TopSpeedKmh => _topSpeedKmh;

    private ScenarioEvalConfig _activeConfig;

    // ── Event log for CSV export ──

    private struct EvalEvent
    {
        public float time;
        public string junctionId;
        public string eventType;   // "RedLightViolation", "RedLightOK", "TurnSignalOK", "TurnSignalMissing"
        public string detail;      // light char, signal direction, etc.
    }

    private readonly List<EvalEvent> _eventLog = new();
    private float _evalStartTime;
    private float _lastSpeedingLogTime = -10f;

    private void Awake()
    {
        if (simController == null)
            simController = GetComponent<SimulationController>();
        if (simController == null)
            simController = FindFirstObjectByType<SimulationController>();
    }

    /// <summary>
    /// Called when a new scenario starts. Resets the checklist, looks up
    /// matching scenario rules, and updates references.
    /// </summary>
    public void BeginEvaluation(GameObject egoVehicle, ScenarioId scenario)
    {
        _activeScenario = scenario;
        _vehicleMode = scenario.ToString().Contains("Bike")
            ? VehicleMode.Bike
            : VehicleMode.Car;

        _ranRedLight = false;
        _usedTurnSignal = false;
        _hadCollision = false;
        _collisionCount = 0;
        _exceededSpeedLimit = false;
        _speedingEventCount = 0;
        _topSpeedKmh = 0f;
        _lastSpeedingLogTime = -10f;
        _eventLog.Clear();
        _evalStartTime = Time.time;

        // Look up rules for this scenario
        _activeConfig = scenarioRules.Find(r => r.scenario == scenario);

        carUserControl = (_vehicleMode == VehicleMode.Car)
            ? egoVehicle.GetComponent<CarUserControl>()
            : null;

        _egoRb = egoVehicle.GetComponent<Rigidbody>();

        if (_activeConfig != null)
            Debug.Log($"[DrivingEvaluator] Evaluation started — scenario: {scenario}, mode: {_vehicleMode}");
        else
            Debug.Log($"[DrivingEvaluator] No rules configured for '{scenario}' — evaluation inactive.");
    }

    /// <summary>
    /// Call when the scenario ends or before switching scenarios.
    /// Exports the evaluation log to a CSV file in the Results/ folder.
    /// </summary>
    public void EndEvaluation()
    {
        if (_activeConfig == null || _eventLog.Count == 0)
        {
            Debug.Log("[DrivingEvaluator] No evaluation data to export.");
            return;
        }

        ExportCsv();

        Debug.Log($"[DrivingEvaluator] Evaluation ended — scenario: {_activeScenario}, " +
                  $"red light violation: {_ranRedLight}, turn signal used: {_usedTurnSignal}, " +
                  $"collisions: {_collisionCount}, top speed: {_topSpeedKmh:F0} km/h");
    }

    private void OnEnable()
    {
        StopLineTrigger.OnEgoCrossedStopLine += HandleStopLineCrossing;
        CollisionDetector.OnEgoCollision += HandleCollision;
    }

    private void OnDisable()
    {
        StopLineTrigger.OnEgoCrossedStopLine -= HandleStopLineCrossing;
        CollisionDetector.OnEgoCollision -= HandleCollision;
    }

    private void FixedUpdate()
    {
        // Speed monitoring
        if (_egoRb == null || speedLimitKmh <= 0f) return;
        if (_activeConfig == null) return;

        float currentSpeedKmh = _egoRb.linearVelocity.magnitude * 3.6f;

        if (currentSpeedKmh > _topSpeedKmh)
            _topSpeedKmh = currentSpeedKmh;

        if (currentSpeedKmh > speedLimitKmh)
        {
            _exceededSpeedLimit = true;

            if (Time.time - _lastSpeedingLogTime >= speedingLogCooldown)
            {
                _speedingEventCount++;
                _lastSpeedingLogTime = Time.time;
                LogEvent("", "Speeding", $"speed={currentSpeedKmh:F1};limit={speedLimitKmh:F0}");
                Debug.LogWarning($"[DrivingEvaluator] SPEEDING: {currentSpeedKmh:F1} km/h (limit {speedLimitKmh:F0})");
            }
        }
    }

    private void HandleStopLineCrossing(StopLineTrigger trigger, Collider ego)
    {
        // No rules for the active scenario — skip all checks
        if (_activeConfig == null) return;

        // ── 1. Red light check ──
        if (_activeConfig.checkRedLights && simController != null)
        {
            string state = simController.GetTrafficLightState(trigger.junctionId);
            if (!string.IsNullOrEmpty(state))
            {
                int idx = Mathf.Clamp(trigger.linkIndex, 0, state.Length - 1);
                char c = state[idx];
                bool isRed = (c == 'r' || c == 'R');

                if (isRed)
                {
                    _ranRedLight = true;
                    LogEvent(trigger.junctionId, "RedLightViolation", $"light={c}");
                    Debug.LogWarning($"[DrivingEvaluator] RED LIGHT VIOLATION at junction {trigger.junctionId}");
                }
                else
                {
                    LogEvent(trigger.junctionId, "RedLightOK", $"light={c}");
                    Debug.Log($"[DrivingEvaluator] Crossed stop line at {trigger.junctionId} — light was {c} (OK)");
                }
            }
        }

        // ── 2. Turn signal check (only if a rule exists for this junction) ──
        if (_vehicleMode == VehicleMode.Bike || carUserControl == null) return;

        TurnSignalRule rule = _activeConfig.turnSignalChecks.Find(
            r => r.junctionId == trigger.junctionId);
        if (rule == null) return;

        bool signalCorrect = rule.direction switch
        {
            SignalDirection.Right => carUserControl.IsRightSignalOn,
            SignalDirection.Left => carUserControl.IsLeftSignalOn,
            _ => true
        };

        if (signalCorrect)
        {
            _usedTurnSignal = true;
            LogEvent(trigger.junctionId, "TurnSignalOK", $"direction={rule.direction}");
            Debug.Log($"[DrivingEvaluator] Turn signal was ON at junction {trigger.junctionId} ({rule.direction}) ✓");
        }
        else
        {
            _usedTurnSignal = false;
            LogEvent(trigger.junctionId, "TurnSignalMissing", $"direction={rule.direction}");
            Debug.LogWarning($"[DrivingEvaluator] MISSING TURN SIGNAL at junction {trigger.junctionId} ({rule.direction})!");
        }
    }

    // ── Logging helpers ──

    private void HandleCollision(CollisionDetector detector, Collision collision)
    {
        _hadCollision = true;
        _collisionCount++;

        float impactSpeed = collision.relativeVelocity.magnitude;
        string otherName = collision.gameObject.name;
        string otherTag = collision.gameObject.tag;

        LogEvent("", "Collision",
            $"other={otherName};tag={otherTag};impact_speed={impactSpeed:F1}");

        Debug.LogWarning($"[DrivingEvaluator] COLLISION with '{otherName}' " +
                         $"(tag={otherTag}) at {impactSpeed:F1} m/s");
    }

    private void LogEvent(string junctionId, string eventType, string detail)
    {
        _eventLog.Add(new EvalEvent
        {
            time = Time.time - _evalStartTime,
            junctionId = junctionId,
            eventType = eventType,
            detail = detail
        });
    }

    // ── CSV export ──

    private void ExportCsv()
    {
        string resultsDir = LocateOrCreateResultsFolder();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"evaluation_{_activeScenario}_{timestamp}.csv";
        string filePath = Path.Combine(resultsDir, fileName);

        var sb = new StringBuilder();

        // Header
        sb.AppendLine("time;scenario;vehicle_mode;junction_id;event_type;detail");

        // Event rows
        foreach (var e in _eventLog)
        {
            sb.Append($"{e.time:F3};");
            sb.Append($"{_activeScenario};");
            sb.Append($"{_vehicleMode};");
            sb.Append($"{e.junctionId};");
            sb.Append($"{e.eventType};");
            sb.AppendLine(e.detail);
        }

        // Summary row
        sb.AppendLine();
        sb.AppendLine("# Summary");
        sb.AppendLine($"# Scenario: {_activeScenario}");
        sb.AppendLine($"# Vehicle Mode: {_vehicleMode}");
        sb.AppendLine($"# Red Light Violation: {_ranRedLight}");
        sb.AppendLine($"# Turn Signal Used: {_usedTurnSignal}");
        sb.AppendLine($"# Collisions: {_collisionCount}");
        sb.AppendLine($"# Exceeded Speed Limit: {_exceededSpeedLimit}");
        sb.AppendLine($"# Speeding Events: {_speedingEventCount}");
        sb.AppendLine($"# Top Speed: {_topSpeedKmh:F1} km/h (limit {speedLimitKmh:F0})");
        sb.AppendLine($"# Total Events: {_eventLog.Count}");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[DrivingEvaluator] CSV exported to: {filePath}");
    }

    /// <summary>Finds (or creates) Results folder, matching the project convention.</summary>
    private static string LocateOrCreateResultsFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        DirectoryInfo dir = new DirectoryInfo(projectRoot);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Results");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        // Not found — create it next to the project
        string fallback = Path.Combine(projectRoot, "Results");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
