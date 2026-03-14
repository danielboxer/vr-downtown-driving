using UnityEngine;
using UnityStandardAssets.Vehicles.Car;

/// <summary>
/// Checklist evaluator for the novice driver training scenario.
/// Listens for stop-line crossings and checks both traffic light state
/// and turn signal usage at the same trigger. Place on the manager GameObject.
/// </summary>
public class DrivingEvaluator : MonoBehaviour
{
    public enum VehicleMode { Car, Bike }

    // Auto-resolved references (no Inspector assignment needed)
    private SimulationController simController;
    private CarUserControl carUserControl;

    [Header("Mode")]
    [Tooltip("Current vehicle type. Car checks turn signals; Bike skips them.")]
    public VehicleMode vehicleMode = VehicleMode.Car;

    // ── Checklist state ──
    [Header("Checklist (read-only at runtime)")]
    [SerializeField] private bool _ranRedLight;
    [SerializeField] private bool _usedTurnSignal;

    /// <summary>True if the driver crossed a stop line while the light was red.</summary>
    public bool RanRedLight => _ranRedLight;

    /// <summary>True if the driver had the correct signal on before the stop line.</summary>
    public bool UsedTurnSignal => _usedTurnSignal;

    private void Awake()
    {
        if (simController == null)
            simController = GetComponent<SimulationController>();
        if (simController == null)
            simController = FindFirstObjectByType<SimulationController>();
    }

    /// <summary>
    /// Called when a new scenario starts. Resets the checklist and updates references.
    /// </summary>
    public void BeginEvaluation(GameObject egoVehicle, VehicleMode mode)
    {
        vehicleMode = mode;
        _ranRedLight = false;
        _usedTurnSignal = false;

        carUserControl = (mode == VehicleMode.Car)
            ? egoVehicle.GetComponent<CarUserControl>()
            : null;

        Debug.Log($"[DrivingEvaluator] Evaluation started — mode: {mode}");
    }

    private void OnEnable()
    {
        StopLineTrigger.OnEgoCrossedStopLine += HandleStopLineCrossing;
    }

    private void OnDisable()
    {
        StopLineTrigger.OnEgoCrossedStopLine -= HandleStopLineCrossing;
    }

    private void HandleStopLineCrossing(StopLineTrigger trigger, Collider ego)
    {
        // ── 1. Red light check ──
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
                    Debug.LogWarning($"[DrivingEvaluator] RED LIGHT VIOLATION at junction {trigger.junctionId}");
                }
                else
                {
                    Debug.Log($"[DrivingEvaluator] Crossed stop line at {trigger.junctionId} — light was {c} (OK)");
                }
            }
        }

        // ── 2. Turn signal check (car only, when a signal is required) ──
        if (trigger.requiredSignal == StopLineTrigger.RequiredSignal.None) return;
        if (vehicleMode == VehicleMode.Bike) return;
        if (carUserControl == null) return;

        bool signalCorrect = trigger.requiredSignal switch
        {
            StopLineTrigger.RequiredSignal.Right => carUserControl.IsRightSignalOn,
            StopLineTrigger.RequiredSignal.Left => carUserControl.IsLeftSignalOn,
            _ => true
        };

        if (signalCorrect)
        {
            _usedTurnSignal = true;
            Debug.Log($"[DrivingEvaluator] Turn signal was ON at stop line ({trigger.requiredSignal}) (OK)");
        }
        else
        {
            _usedTurnSignal = false;
            Debug.LogWarning($"[DrivingEvaluator] MISSING TURN SIGNAL at stop line ({trigger.requiredSignal})!");
        }
    }
}
