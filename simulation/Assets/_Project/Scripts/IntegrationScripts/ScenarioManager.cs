using UnityEngine;
using System;

/// <summary>
/// Receives the scenario name from the Python/SUMO config message and
/// configures the ego vehicle prefab and spline accordingly.
///
/// SETUP
///   1. Attach to the same GameObject as SimulationController.
///   2. Assign the ego vehicle prefabs and spline GameObjects in the Inspector.
///   3. Each Spline GameObject should be DISABLED by default in the scene.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("Ego Vehicle Prefabs")]
    public GameObject egoCarPrefab;
    public GameObject egoBikePrefab;

    [Header("Spline Paths (disabled by default in scene)")]
    [Tooltip("Spline for EgoCar_Right_Turn scenario")]
    public GameObject carRightTurnSpline;
    [Tooltip("Spline for EgoBike_Right_Turn scenario")]
    public GameObject bikeRightTurnSpline;

    [Header("Runtime State (read-only)")]
    [SerializeField] private string _activeScenario = "";

    private SimulationController _simController;

    private void Awake()
    {
        _simController = GetComponent<SimulationController>();
    }

    /// <summary>
    /// Called by SimulationController when a "config" message arrives from Python.
    /// </summary>
    public void ApplyScenario(string scenarioName)
    {
        _activeScenario = scenarioName;
        Debug.Log($"ScenarioManager: Applying scenario '{scenarioName}'");

        // Disable all splines first
        if (carRightTurnSpline != null) carRightTurnSpline.SetActive(false);
        if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(false);

        // Select ego prefab and spline based on scenario name
        switch (scenarioName)
        {
            case "EgoCar_Free_Drive":
                _simController.egoVehicle = egoCarPrefab;
                break;

            case "EgoCar_Right_Turn":
                _simController.egoVehicle = egoCarPrefab;
                if (carRightTurnSpline != null) carRightTurnSpline.SetActive(true);
                break;

            case "EgoBike_Free_Bike":
                _simController.egoVehicle = egoBikePrefab;
                break;

            case "EgoBike_Right_Turn":
                _simController.egoVehicle = egoBikePrefab;
                if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(true);
                break;

            default:
                Debug.LogWarning($"ScenarioManager: Unknown scenario '{scenarioName}', using defaults.");
                break;
        }
    }

    /// <summary>
    /// Called after the ego vehicle is instantiated to refresh its spline reference.
    /// </summary>
    public void RefreshEgoSpline(GameObject ego)
    {
        var followCurve = ego.GetComponent<FollowCurve>();
        if (followCurve != null)
            followCurve.RefreshSpline();
    }
}
