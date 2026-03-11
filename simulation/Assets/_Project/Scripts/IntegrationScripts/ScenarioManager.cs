using UnityEngine;
using System;

/// <summary>
/// Receives the scenario name from the Python/SUMO config message and
/// activates the correct ego vehicle and spline already placed in the scene.
///
/// SETUP
///   1. Attach to the same GameObject as SimulationController.
///   2. Place ego vehicles and splines in the scene, all DISABLED by default.
///   3. Drag the scene objects into the Inspector slots below.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("Ego Vehicles (disabled in scene)")]
    public GameObject egoCar;
    public GameObject egoBike;

    [Header("Spline Paths (disabled in scene)")]
    [Tooltip("Spline for EgoCar_Right_Turn scenario")]
    public GameObject carRightTurnSpline;
    [Tooltip("Spline for EgoBike_Right_Turn scenario")]
    public GameObject bikeRightTurnSpline;

    [Header("Default (used when running without SUMO)")]
    [Tooltip("Scenario to activate at Start if no config message arrives")]
    public string defaultScenario = "EgoCar_Free_Drive";

    [Header("Runtime State (read-only)")]
    [SerializeField] private string _activeScenario = "";

    private SimulationController _simController;

    private void Awake()
    {
        _simController = GetComponent<SimulationController>();
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(defaultScenario))
            ApplyScenario(defaultScenario);
    }

    /// <summary>
    /// Called by SimulationController when a "config" message arrives from Python.
    /// Activates the correct ego vehicle + spline and registers the ego with SimulationController.
    /// </summary>
    public void ApplyScenario(string scenarioName)
    {
        _activeScenario = scenarioName;
        Debug.Log($"ScenarioManager: Applying scenario '{scenarioName}'");

        // Disable everything first
        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);
        if (carRightTurnSpline != null) carRightTurnSpline.SetActive(false);
        if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(false);

        GameObject activeEgo = null;

        switch (scenarioName)
        {
            case "EgoCar_Free_Drive":
                activeEgo = egoCar;
                break;

            case "EgoCar_Right_Turn":
                activeEgo = egoCar;
                if (carRightTurnSpline != null) carRightTurnSpline.SetActive(true);
                break;

            case "EgoBike_Free_Bike":
                activeEgo = egoBike;
                break;

            case "EgoBike_Right_Turn":
                activeEgo = egoBike;
                if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(true);
                break;

            default:
                Debug.LogWarning($"ScenarioManager: Unknown scenario '{scenarioName}', using defaults.");
                return;
        }

        if (activeEgo != null)
        {
            activeEgo.SetActive(true);
            _simController.RegisterEgoVehicle(activeEgo);

            // let FollowCurve pick up the newly active spline
            var followCurve = activeEgo.GetComponent<FollowCurve>();
            if (followCurve != null)
                followCurve.RefreshSpline();
        }
    }
}
