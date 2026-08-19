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


    [Header("Speed Limit")]
    [Tooltip("Speed limit in km/h. Set to 0 to disable speed monitoring.")]
    public float speedLimitKmh = 50f;

    [Tooltip("How many km/h above the limit before speeding is flagged.")]
    public float speedingToleranceKmh = 5f;

    [Tooltip("Seconds between speeding violation log entries (prevents per-frame spam).")]
    public float speedingLogCooldown = 5f;

    [Header("Stop Line Evaluation")]
    [Tooltip("Minimum dot-product alignment required for a stop-line trigger to count as the active approach.")]
    [Range(-1f, 1f)]
    [SerializeField] private float stopLineApproachAlignmentThreshold = 0.3f;

    [Header("Audio Feedback")]
    [Tooltip("Play a warning sound when the evaluator records a notable event.")]
    [SerializeField] private bool playWarningSounds = true;

    [Tooltip("Master volume for evaluator warning sounds.")]
    [Range(0f, 2f)]
    [SerializeField] private float warningVolume = 1f;

    /// <summary>Scales the warning tone and voice prompt together. Set from the options menu.</summary>
    public float WarningVolume { get; set; } = 1f;

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

    [Tooltip("Optional voice prompt played after red-light or stop-line violations.")]
    [SerializeField] private AudioClip stopLineVoiceClip;

    [Tooltip("Optional voice prompt played after missing turn-signal warnings.")]
    [SerializeField] private AudioClip turnSignalVoiceClip;

    [Tooltip("Optional voice prompt played after a non-curb vehicle collision. Plays through the voice queue after the crash sound.")]
    [SerializeField] private AudioClip collisionVoiceClip;

    [Tooltip("Optional clip played for evaluator warning events such as red lights or missing signals.")]
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
    // cumulative: set once a turn is taken without the correct signal, never reset within a scenario
    [ReadOnly, SerializeField] private bool _missedTurnSignal;
    [ReadOnly, SerializeField] private bool _hadCollision;
    [ReadOnly, SerializeField] private int _collisionCount;
    [ReadOnly, SerializeField] private bool _exceededSpeedLimit;
    [ReadOnly, SerializeField] private int _speedingEventCount;
    [ReadOnly, SerializeField] private float _topSpeedKmh;

    /// <summary>True if the driver crossed a stop line while the light was red.</summary>
    public bool RanRedLight => _ranRedLight;

    /// <summary>True only if the driver signalled correctly at every turn in this scenario.</summary>
    public bool UsedTurnSignal => !_missedTurnSignal;

    /// <summary>True if the driver collided with anything during this scenario.</summary>
    public bool HadCollision => _hadCollision;

    /// <summary>Number of collisions during this scenario.</summary>
    public int CollisionCount => _collisionCount;

    /// <summary>True if the driver exceeded the speed limit during this scenario.</summary>
    public bool ExceededSpeedLimit => _exceededSpeedLimit;

    /// <summary>Highest speed recorded during this scenario (km/h).</summary>
    public float TopSpeedKmh => _topSpeedKmh;

    // ── Event log for CSV export ──

    private struct EvalEvent
    {
        public float time;
        public string junctionId;
        public string eventType;   // "RedLightViolation", "RedLightOK", "TurnSignalOK", "TurnSignalMissing"
        public string detail;      // light char, signal direction, etc.
    }

    private readonly List<EvalEvent> _eventLog = new();
    // Each entry is a (warning beep, voice clip) pair; processed sequentially by the coroutine.
    private readonly List<PendingWarning> _warningQueue = new();
    private float _evalStartTime;
    private float _lastSpeedingLogTime = -10f;
    private Coroutine _warningCoroutine;

    // Last valid stop-line approach, used for turn-signal direction checks.
    private string _lastStopLineJunctionId;
    private Vector3 _lastStopLineApproachDir;

    // Warning tone + optional voice prompt.
    private struct PendingWarning
    {
        public AudioClip warnClip;
        public AudioClip voiceClip;
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
        _vehicleMode = scenario.ToString().Contains("bike")
            ? VehicleMode.Bike
            : VehicleMode.Car;

        _ranRedLight = false;
        _missedTurnSignal = false;
        _hadCollision = false;
        _collisionCount = 0;
        _exceededSpeedLimit = false;
        _speedingEventCount = 0;
        _topSpeedKmh = 0f;
        _lastSpeedingLogTime = -10f;
        _eventLog.Clear();
        _evalStartTime = Time.time;

        _evaluationEnabled = true;

        _lastStopLineJunctionId = null;
        _lastStopLineApproachDir = Vector3.zero;

        carUserControl = (_vehicleMode == VehicleMode.Car)
            ? egoVehicle.GetComponent<CarUserControl>()
            : null;

        _egoRb = egoVehicle.GetComponent<Rigidbody>();
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
                  $"red light violation: {_ranRedLight}, turn signal used: {!_missedTurnSignal}, " +
                  $"collisions: {_collisionCount}, top speed: {_topSpeedKmh:F0} km/h");
    }

    private void OnEnable()
    {
        StopLineTrigger.OnEgoCrossedStopLine += HandleStopLineCrossing;
        TurnDirectionTrigger.OnEgoCrossedTurnTrigger += HandleTurnDirectionCrossing;
        CollisionDetector.OnEgoCollision += HandleCollision;
    }

    private void OnDisable()
    {
        StopLineTrigger.OnEgoCrossedStopLine -= HandleStopLineCrossing;
        TurnDirectionTrigger.OnEgoCrossedTurnTrigger -= HandleTurnDirectionCrossing;
        CollisionDetector.OnEgoCollision -= HandleCollision;

        if (_warningCoroutine != null)
        {
            StopCoroutine(_warningCoroutine);
            _warningCoroutine = null;
        }

        _warningQueue.Clear();

        if (_warningAudioSource != null)
            _warningAudioSource.Stop();

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

        if (currentSpeedKmh > speedLimitKmh + speedingToleranceKmh)
        {
            _exceededSpeedLimit = true;

            if (Time.time - _lastSpeedingLogTime >= speedingLogCooldown)
            {
                _speedingEventCount++;
                _lastSpeedingLogTime = Time.time;
                LogEvent("", "Speeding", $"speed={currentSpeedKmh:F1};limit={speedLimitKmh:F0}");
            }
        }
        else
        {
            // Reset the cooldown so the next event logs promptly if they speed again
            _lastSpeedingLogTime = -10f;
        }
    }

    private void HandleStopLineCrossing(StopLineTrigger trigger, Collider ego)
    {
        if (!_evaluationEnabled) return;

        // Ignore overlapping stop-line triggers from other approaches.
        if (!IsEgoApproachingStopLine(trigger, ego))
            return;

        _lastStopLineJunctionId = trigger.junctionId;
        _lastStopLineApproachDir = NormalizeHorizontal(trigger.transform.forward);

        if (simController != null)
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
                    QueueWarning(stopLineVoiceClip);
                    Debug.LogWarning($"[DrivingEvaluator] RED LIGHT VIOLATION at junction {trigger.junctionId}");
                }
                else
                {
                    LogEvent(trigger.junctionId, "RedLightOK", $"light={c}");
                    Debug.Log($"[DrivingEvaluator] Crossed stop line at {trigger.junctionId} — light was {c} (OK)");
                }
            }
        }
    }

    private bool IsEgoApproachingStopLine(StopLineTrigger trigger, Collider ego)
    {
        Vector3 approachDir = NormalizeHorizontal(trigger.transform.forward);
        if (approachDir == Vector3.zero)
            return true;

        Rigidbody rb = _egoRb != null ? _egoRb : ego.attachedRigidbody;
        if (rb != null)
        {
            Vector3 velocity = NormalizeHorizontal(rb.linearVelocity);
            if (velocity != Vector3.zero)
                return Vector3.Dot(velocity, approachDir) > stopLineApproachAlignmentThreshold;

            Vector3 rbForward = NormalizeHorizontal(rb.transform.forward);
            if (rbForward != Vector3.zero)
                return Vector3.Dot(rbForward, approachDir) > stopLineApproachAlignmentThreshold;
        }

        Vector3 egoForward = NormalizeHorizontal(ego.transform.forward);
        if (egoForward != Vector3.zero)
            return Vector3.Dot(egoForward, approachDir) > stopLineApproachAlignmentThreshold;

        return true;
    }

    private static Vector3 NormalizeHorizontal(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
    }

    private void HandleTurnDirectionCrossing(TurnDirectionTrigger trigger, Collider ego)
    {
        if (!_evaluationEnabled) return;
        if (_vehicleMode == VehicleMode.Bike || carUserControl == null) return;

        // Only evaluate triggers that belong to the junction last crossed.
        if (_lastStopLineJunctionId == null ||
            trigger.junctionId != _lastStopLineJunctionId)
            return;

        // Same/opposite approaches map to right/left turns; perpendicular grazes are ignored.
        float d = Vector3.Dot(_lastStopLineApproachDir, trigger.approachDir);

        bool isRight = d > 0.5f;
        bool isLeft = d < -0.5f;

        if (!isRight && !isLeft)
            return; // perpendicular trigger hit during a turn, skip it

        bool signalOn = isRight
            ? carUserControl.IsRightSignalOn
            : carUserControl.IsLeftSignalOn;

        string dirLabel = isRight ? "Right" : "Left";

        if (signalOn)
        {
            LogEvent(trigger.junctionId, "TurnSignalOK", $"direction={dirLabel}");
            Debug.Log($"[DrivingEvaluator] Turn signal ON at junction {trigger.junctionId} ({dirLabel}) ✓");
        }
        else
        {
            _missedTurnSignal = true;
            LogEvent(trigger.junctionId, "TurnSignalMissing", $"direction={dirLabel}");
            QueueWarning(turnSignalVoiceClip);
            Debug.LogWarning($"[DrivingEvaluator] MISSING TURN SIGNAL at junction {trigger.junctionId} ({dirLabel})!");
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

        // Play crash sound for all collisions except curbs.
        // Voice warning is reserved for vehicle collisions only.
        if (!IsCurbCollision(collision))
            PlayCollisionCue(collision);
        if (IsVehicleCollision(collision))
            QueueWarning(collisionVoiceClip);

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

    private void QueueWarning(AudioClip voiceClip)
    {
        _warningQueue.Add(new PendingWarning
        {
            warnClip = warningClip,
            voiceClip = voiceClip
        });

        if (_warningCoroutine == null)
            _warningCoroutine = StartCoroutine(ProcessWarningQueue());
    }

    private IEnumerator ProcessWarningQueue()
    {
        while (_warningQueue.Count > 0)
        {
            PendingWarning pending = _warningQueue[0];
            _warningQueue.RemoveAt(0);

            // Play the warning tone and wait for it to finish.
            if (playWarningSounds && warningVolume * WarningVolume > 0f && pending.warnClip != null)
            {
                EnsureWarningAudioSource();
                _warningAudioSource.pitch = 1f;
                _warningAudioSource.PlayOneShot(pending.warnClip, warningVolume * WarningVolume);

                float warnDuration = pending.warnClip.length + warningToVoiceDelay;
                yield return new WaitForSeconds(warnDuration);
            }

            // Play the voice clip and wait for it to finish before the next item.
            if (playVoicePrompts && voiceVolume * WarningVolume > 0f && pending.voiceClip != null)
            {
                EnsureVoiceAudioSource();
                _voiceAudioSource.pitch = GetRandomPitch(voicePitchMin, voicePitchMax);
                _voiceAudioSource.PlayOneShot(pending.voiceClip, voiceVolume * WarningVolume);

                float voiceDuration = pending.voiceClip.length / Mathf.Max(_voiceAudioSource.pitch, 0.01f);
                yield return new WaitForSeconds(voiceDuration);
            }
        }

        _warningCoroutine = null;
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

    // Returns true when the collided object is an NPC vehicle (has VehicleController).
    private static bool IsVehicleCollision(Collision collision)
    {
        return collision.gameObject.GetComponentInParent<VehicleController>() != null;
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
#if UNITY_WEBGL
        Debug.Log("[DrivingEvaluator] CSV export is unavailable in WebGL builds.");
#else
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
            // detail is the last column and may itself contain the ';' separator, so swap it out
            sb.AppendLine(e.detail.Replace(';', '|'));
        }

        // Summary row
        sb.AppendLine();
        sb.AppendLine("# Summary");
        sb.AppendLine($"# Scenario: {_activeScenario}");
        sb.AppendLine($"# Vehicle Mode: {_vehicleMode}");
        sb.AppendLine($"# Red Light Violation: {_ranRedLight}");
        sb.AppendLine($"# Turn Signal Used: {!_missedTurnSignal}");
        sb.AppendLine($"# Collisions: {_collisionCount}");
        sb.AppendLine($"# Exceeded Speed Limit: {_exceededSpeedLimit}");
        sb.AppendLine($"# Speeding Events: {_speedingEventCount}");
        sb.AppendLine($"# Top Speed: {_topSpeedKmh:F1} km/h (limit {speedLimitKmh:F0})");
        sb.AppendLine($"# Total Events: {_eventLog.Count}");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[DrivingEvaluator] CSV exported to: {filePath}");
#endif
    }

#if !UNITY_WEBGL
    /// <summary>Finds (or creates) Results folder, matching the project convention.</summary>
    private static string LocateOrCreateResultsFolder()
    {
        // Always write to Documents so the folder is user-writable even when the game
        // is installed to Program Files (where writing without admin rights would throw).
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VR Downtown Driving",
            "Results");
        Directory.CreateDirectory(dir);
        return dir;
    }
#endif
}
