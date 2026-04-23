using System;
using System.Collections;
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
    [Tooltip("Configure stop-line and turn-signal rules per scenario. Scenarios not listed here skip those rule checks.")]
    [SerializeField] private List<ScenarioEvalConfig> scenarioRules = new();
    [Tooltip("If this list is not empty, evaluation only runs in the selected scenarios.")]
    [SerializeField] private List<ScenarioId> enabledScenarios = new();


    [Header("Speed Limit")]
    [Tooltip("Speed limit in km/h. Set to 0 to disable speed monitoring.")]
    public float speedLimitKmh = 50f;

    [Tooltip("Seconds between speeding violation log entries (prevents per-frame spam).")]
    public float speedingLogCooldown = 5f;

    [Header("Audio Feedback")]
    [Tooltip("Play a warning sound when the evaluator records a notable event.")]
    [SerializeField] private bool playWarningSounds = true;

    [Tooltip("Master volume for evaluator warning sounds.")]
    [Range(0f, 2f)]
    [SerializeField] private float warningVolume = 1f;

    [Tooltip("Base volume for non-curb collision sounds before impact scaling.")]
    [Range(0f, 2f)]
    [SerializeField] private float collisionVolume = 1f;

    [Tooltip("Base volume for curb bump sounds before impact scaling.")]
    [Range(0f, 2f)]
    [SerializeField] private float curbVolume = 0.75f;

    [Tooltip("Lowest volume multiplier used for a qualifying collision.")]
    [Range(0f, 1f)]
    [SerializeField] private float minImpactVolumeMultiplier = 0.35f;

    [Tooltip("Impact speed in km/h that reaches full collision volume.")]
    [SerializeField] private float impactSpeedForMaxVolume = 25f;

    [Tooltip("Minimum pitch used for collision and curb sounds.")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float collisionPitchMin = 0.97f;

    [Tooltip("Maximum pitch used for collision and curb sounds.")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float collisionPitchMax = 1.03f;

    [Header("Voice Feedback")]
    [Tooltip("Play a voice prompt after the warning sound for rule-based evaluator events.")]
    [SerializeField] private bool playVoicePrompts = true;

    [Tooltip("Playback volume for voice prompts.")]
    [Range(0f, 2f)]
    [SerializeField] private float voiceVolume = 1f;

    [Tooltip("Extra delay after the warning sound before the voice prompt starts.")]
    [SerializeField] private float warningToVoiceDelay = 0.1f;

    [Tooltip("Minimum pitch used for voice prompts.")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float voicePitchMin = 0.98f;

    [Tooltip("Maximum pitch used for voice prompts.")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float voicePitchMax = 1.02f;

    [Tooltip("Optional voice prompt played after speeding warnings.")]
    [SerializeField] private AudioClip speedingVoiceClip;

    [Tooltip("Optional voice prompt played after red-light or stop-line violations.")]
    [SerializeField] private AudioClip stopLineVoiceClip;

    [Tooltip("Optional voice prompt played after missing turn-signal warnings.")]
    [SerializeField] private AudioClip turnSignalVoiceClip;

    [Tooltip("Optional clip played for evaluator warning events such as speeding, red lights, or missing signals.")]
    [SerializeField] private AudioClip warningClip;

    [Tooltip("Optional clips played for non-curb collisions. A random clip is chosen, then falls back to the warning clip if none are assigned.")]
    [SerializeField] private List<AudioClip> collisionWarningClips = new();

    [Tooltip("Optional clip played when colliding with generated curb meshes. Falls back to the warning clip if empty.")]
    [SerializeField] private AudioClip curbWarningClip;

    // Auto-resolved references (no Inspector assignment needed)
    private SimulationController simController;
    private CarUserControl carUserControl;
    private Rigidbody _egoRb;
    private AudioSource _warningAudioSource;
    private AudioSource _collisionAudioSource;
    private AudioSource _voiceAudioSource;

    [Header("Runtime State")]
    [ReadOnly, SerializeField] private ScenarioId _activeScenario;
    [ReadOnly, SerializeField] private VehicleMode _vehicleMode = VehicleMode.Car;
    [ReadOnly, SerializeField] private bool _evaluationEnabled;

    // ── Checklist state ──
    [Header("Checklist")]
    [ReadOnly, SerializeField] private bool _ranRedLight;
    [ReadOnly, SerializeField] private bool _usedTurnSignal;
    [ReadOnly, SerializeField] private bool _hadCollision;
    [ReadOnly, SerializeField] private int _collisionCount;
    [ReadOnly, SerializeField] private bool _exceededSpeedLimit;
    [ReadOnly, SerializeField] private int _speedingEventCount;
    [ReadOnly, SerializeField] private float _topSpeedKmh;

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
    private readonly Queue<QueuedVoicePrompt> _voicePromptQueue = new();
    private float _evalStartTime;
    private float _lastSpeedingLogTime = -10f;
    private Coroutine _voicePromptCoroutine;

    private struct QueuedVoicePrompt
    {
        public AudioClip clip;
        public float delay;
    }

    private void Awake()
    {
        if (simController == null)
            simController = GetComponent<SimulationController>();
        if (simController == null)
            simController = FindFirstObjectByType<SimulationController>();

        EnsureWarningAudioSource();
        EnsureVoiceAudioSource();
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

        _evaluationEnabled = enabledScenarios.Count == 0 || enabledScenarios.Contains(scenario);

        // Look up rules for this scenario
        _activeConfig = _evaluationEnabled
            ? scenarioRules.Find(r => r.scenario == scenario)
            : null;

        carUserControl = (_vehicleMode == VehicleMode.Car)
            ? egoVehicle.GetComponent<CarUserControl>()
            : null;

        _egoRb = egoVehicle.GetComponent<Rigidbody>();

        if (!_evaluationEnabled)
        {
            Debug.Log($"[DrivingEvaluator] Evaluation disabled for scenario: {scenario}");
            return;
        }

        if (_activeConfig != null)
            Debug.Log($"[DrivingEvaluator] Evaluation started — scenario: {scenario}, mode: {_vehicleMode}");
        else
            Debug.Log($"[DrivingEvaluator] Evaluation started for '{scenario}' with no scenario-specific stop-line rules.");
    }

    /// <summary>
    /// Call when the scenario ends or before switching scenarios.
    /// Exports the evaluation log to a CSV file in the Results/ folder.
    /// </summary>
    public void EndEvaluation()
    {
        if (!_evaluationEnabled || _eventLog.Count == 0)
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

        if (_voicePromptCoroutine != null)
        {
            StopCoroutine(_voicePromptCoroutine);
            _voicePromptCoroutine = null;
        }

        _voicePromptQueue.Clear();

        if (_voiceAudioSource != null)
            _voiceAudioSource.Stop();

        if (_collisionAudioSource != null)
            _collisionAudioSource.Stop();
    }

    private void FixedUpdate()
    {
        // Speed monitoring
        if (_egoRb == null || speedLimitKmh <= 0f) return;
        if (!_evaluationEnabled) return;

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
                PlayWarningCue();
                QueueVoicePrompt(speedingVoiceClip);
                Debug.LogWarning($"[DrivingEvaluator] SPEEDING: {currentSpeedKmh:F1} km/h (limit {speedLimitKmh:F0})");
            }
        }
    }

    private void HandleStopLineCrossing(StopLineTrigger trigger, Collider ego)
    {
        if (!_evaluationEnabled) return;

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
                    PlayWarningCue();
                    QueueVoicePrompt(stopLineVoiceClip);
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
            PlayWarningCue();
            QueueVoicePrompt(turnSignalVoiceClip);
            Debug.LogWarning($"[DrivingEvaluator] MISSING TURN SIGNAL at junction {trigger.junctionId} ({rule.direction})!");
        }
    }

    // ── Logging helpers ──

    private void HandleCollision(CollisionDetector detector, Collision collision)
    {
        if (!_evaluationEnabled) return;

        _hadCollision = true;
        _collisionCount++;

        float impactSpeed = collision.relativeVelocity.magnitude;
        string otherName = collision.gameObject.name;
        string otherTag = collision.gameObject.tag;

        LogEvent("", "Collision",
            $"other={otherName};tag={otherTag};impact_speed={impactSpeed:F1}");
        PlayCollisionCue(collision);
        PlayWarningCue();

        Debug.LogWarning($"[DrivingEvaluator] COLLISION with '{otherName}' " +
                         $"(tag={otherTag}) at {impactSpeed:F1} m/s");
    }

    private void EnsureWarningAudioSource()
    {
        if (_warningAudioSource != null)
            return;

        _warningAudioSource = GetComponent<AudioSource>();
        if (_warningAudioSource == null)
            _warningAudioSource = gameObject.AddComponent<AudioSource>();

        _warningAudioSource.playOnAwake = false;
        _warningAudioSource.loop = false;
        _warningAudioSource.spatialBlend = 0f;
    }

    private void EnsureCollisionAudioSource()
    {
        if (_collisionAudioSource != null)
            return;

        _collisionAudioSource = gameObject.AddComponent<AudioSource>();
        _collisionAudioSource.playOnAwake = false;
        _collisionAudioSource.loop = false;
        _collisionAudioSource.spatialBlend = 0f;
    }

    private void EnsureVoiceAudioSource()
    {
        if (_voiceAudioSource != null)
            return;

        _voiceAudioSource = gameObject.AddComponent<AudioSource>();
        _voiceAudioSource.playOnAwake = false;
        _voiceAudioSource.loop = false;
        _voiceAudioSource.spatialBlend = 0f;
    }

    private void PlayWarningCue()
    {
        if (!playWarningSounds || warningVolume <= 0f || warningClip == null)
            return;

        EnsureWarningAudioSource();
        _warningAudioSource.pitch = 1f;

        _warningAudioSource.PlayOneShot(warningClip, warningVolume);
    }

    private void PlayCollisionCue(Collision collision)
    {
        if (!playWarningSounds)
            return;

        AudioClip clip = GetCollisionClip(collision);
        if (clip == null)
            return;

        float volume = GetCollisionCueVolume(collision);
        if (volume <= 0f)
            return;

        EnsureCollisionAudioSource();
        _collisionAudioSource.pitch = GetRandomPitch(collisionPitchMin, collisionPitchMax);
        _collisionAudioSource.PlayOneShot(clip, volume);
    }

    private void QueueVoicePrompt(AudioClip clip)
    {
        if (!playVoicePrompts || voiceVolume <= 0f || clip == null)
            return;

        _voicePromptQueue.Enqueue(new QueuedVoicePrompt
        {
            clip = clip,
            delay = GetWarningLeadInDuration()
        });

        if (_voicePromptCoroutine == null)
            _voicePromptCoroutine = StartCoroutine(ProcessVoicePromptQueue());
    }

    private IEnumerator ProcessVoicePromptQueue()
    {
        while (_voicePromptQueue.Count > 0)
        {
            QueuedVoicePrompt prompt = _voicePromptQueue.Dequeue();

            if (prompt.delay > 0f)
                yield return new WaitForSeconds(prompt.delay);

            if (!playVoicePrompts || voiceVolume <= 0f || prompt.clip == null)
                continue;

            EnsureVoiceAudioSource();
            _voiceAudioSource.pitch = GetRandomPitch(voicePitchMin, voicePitchMax);
            _voiceAudioSource.PlayOneShot(prompt.clip, voiceVolume);

            float clipDuration = prompt.clip.length / Mathf.Max(_voiceAudioSource.pitch, 0.01f);
            yield return new WaitForSeconds(clipDuration);
        }

        _voicePromptCoroutine = null;
    }

    private float GetWarningLeadInDuration()
    {
        if (warningClip == null)
            return warningToVoiceDelay;

        float warningPitch = _warningAudioSource != null
            ? Mathf.Max(_warningAudioSource.pitch, 0.01f)
            : 1f;

        return (warningClip.length / warningPitch) + warningToVoiceDelay;
    }

    private AudioClip GetCollisionClip(Collision collision)
    {
        if (IsCurbCollision(collision))
            return curbWarningClip != null ? curbWarningClip : warningClip;

        AudioClip clip = GetRandomAssignedClip(collisionWarningClips);
        return clip != null ? clip : warningClip;
    }

    private float GetCollisionCueVolume(Collision collision)
    {
        bool isCurbCollision = IsCurbCollision(collision);
        float baseVolume = isCurbCollision ? curbVolume : collisionVolume;
        if (baseVolume <= 0f)
            return 0f;

        // Convert km/h threshold to m/s to match relativeVelocity units
        float maxImpactSpeed = Mathf.Max(impactSpeedForMaxVolume / 3.6f, 0.01f);
        float normalizedImpact = Mathf.Clamp01(collision.relativeVelocity.magnitude / maxImpactSpeed);
        float impactMultiplier = Mathf.Lerp(minImpactVolumeMultiplier, 1f, normalizedImpact);

        return baseVolume * impactMultiplier;
    }

    private static AudioClip GetRandomAssignedClip(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0)
            return null;

        int startIndex = UnityEngine.Random.Range(0, clips.Count);

        for (int offset = 0; offset < clips.Count; offset++)
        {
            AudioClip clip = clips[(startIndex + offset) % clips.Count];
            if (clip != null)
                return clip;
        }

        return null;
    }

    private static float GetRandomPitch(float minPitch, float maxPitch)
    {
        float low = Mathf.Min(minPitch, maxPitch);
        float high = Mathf.Max(minPitch, maxPitch);

        if (Mathf.Approximately(low, high))
            return low;

        return UnityEngine.Random.Range(low, high);
    }

    private static bool IsCurbCollision(Collision collision)
    {
        Transform current = collision.transform;

        while (current != null)
        {
            if (current.name == "Curbs" || current.name.StartsWith("Curb_"))
                return true;

            current = current.parent;
        }

        return false;
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
