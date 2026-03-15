using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityStandardAssets.Vehicles.Car;
using UnityStandardAssets.Bike;

/// <summary>
/// VR dashboard overlay that displays real-time driving info and event
/// notifications. Attach to the world-space Canvas on each ego vehicle.
/// Drag Text elements into the Inspector slots — any slot left empty is
/// simply skipped (no errors).
/// </summary>
public class DashboardUI : MonoBehaviour
{
    // ── UI Slots (wire in Inspector) ──

    [Header("Text Elements")]
    [Tooltip("Shows current speed in km/h.")]
    public Text speedText;

    [Tooltip("Shows which turn signal is active (or blank).")]
    public Text turnSignalText;

    [Tooltip("Contextual instruction for the current scenario.")]
    public Text instructionText;

    [Tooltip("Temporary event message (collision, red light, etc.).")]
    public Text eventMessageText;

    [Tooltip("Speed warning indicator — enabled when exceeding the limit.")]
    public Text speedWarningText;

    // ── Settings ──

    [Header("Speed")]
    [Tooltip("Speed limit (km/h). Used only for the colour change / warning.")]
    public float speedWarningThreshold = 50f;
    public Color normalSpeedColor = Color.white;
    public Color warningSpeedColor = Color.red;

    [Header("Events")]
    [Tooltip("How long an event message stays visible (seconds).")]
    public float eventMessageDuration = 3f;

    [Header("Scenario Instructions")]
    [Tooltip("Map each scenario to a short instruction shown on the HUD.")]
    public List<ScenarioInstruction> instructions = new();

    [System.Serializable]
    public class ScenarioInstruction
    {
        public ScenarioId scenario;
        [TextArea(1, 3)]
        public string message;
    }

    // ── Resolved references ──

    private Rigidbody _rb;
    private CarUserControl _carControl;
    private BikeUserControl _bikeControl;
    private float _eventTimer;

    private float SpeedKmh => _rb != null
        ? _rb.linearVelocity.magnitude * 3.6f
        : 0f;

    private void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>();
        _carControl = GetComponentInParent<CarUserControl>();
        _bikeControl = GetComponentInParent<BikeUserControl>();
    }

    private void OnEnable()
    {
        StopLineTrigger.OnEgoCrossedStopLine += HandleStopLine;
        CollisionDetector.OnEgoCollision += HandleCollision;

        // Clear stale text
        if (eventMessageText != null) eventMessageText.text = "";
        if (speedWarningText != null) speedWarningText.enabled = false;
    }

    private void OnDisable()
    {
        StopLineTrigger.OnEgoCrossedStopLine -= HandleStopLine;
        CollisionDetector.OnEgoCollision -= HandleCollision;
    }

    private void Update()
    {
        UpdateSpeed();
        UpdateTurnSignals();
        UpdateEventTimer();
    }

    // ── Public API (called by ScenarioManager) ──

    /// <summary>Update the instruction text for the active scenario.</summary>
    public void SetScenario(ScenarioId scenario)
    {
        if (instructionText == null) return;
        var entry = instructions.Find(i => i.scenario == scenario);
        instructionText.text = entry != null ? entry.message : "";
    }

    /// <summary>Display a temporary event message on the HUD.</summary>
    public void ShowEventMessage(string message)
    {
        if (eventMessageText == null) return;
        eventMessageText.text = message;
        _eventTimer = eventMessageDuration;
    }

    // ── Internal updates ──

    private void UpdateSpeed()
    {
        if (speedText == null) return;

        float kmh = SpeedKmh;
        speedText.text = $"{kmh:0} km/h";

        bool overLimit = kmh > speedWarningThreshold;
        speedText.color = overLimit ? warningSpeedColor : normalSpeedColor;

        if (speedWarningText != null)
            speedWarningText.enabled = overLimit;
    }

    private void UpdateTurnSignals()
    {
        if (turnSignalText == null) return;

        bool left = _carControl != null && _carControl.IsLeftSignalOn;
        bool right = _carControl != null && _carControl.IsRightSignalOn;

        // Bikes don't have turn signals in the current build
        if (left)
            turnSignalText.text = "<< LEFT";
        else if (right)
            turnSignalText.text = "RIGHT >>";
        else
            turnSignalText.text = "";
    }

    private void UpdateEventTimer()
    {
        if (eventMessageText == null) return;

        if (_eventTimer > 0f)
        {
            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f)
                eventMessageText.text = "";
        }
    }

    // ── Event handlers ──

    private void HandleStopLine(StopLineTrigger trigger, Collider ego)
    {
        // Only react if this is our vehicle
        if (!ego.transform.IsChildOf(transform.root)) return;

        // Check the traffic light state via SimulationController
        var simCtrl = FindFirstObjectByType<SimulationController>();
        if (simCtrl == null) return;

        string state = simCtrl.GetTrafficLightState(trigger.junctionId);
        if (string.IsNullOrEmpty(state)) return;

        int idx = Mathf.Clamp(trigger.linkIndex, 0, state.Length - 1);
        char c = state[idx];
        bool isRed = (c == 'r' || c == 'R');

        if (isRed)
            ShowEventMessage("RED LIGHT!");
    }

    private void HandleCollision(CollisionDetector detector, Collision collision)
    {
        // Only react if this collision came from our vehicle
        if (!detector.transform.IsChildOf(transform.root)) return;

        ShowEventMessage("COLLISION!");
    }
}
