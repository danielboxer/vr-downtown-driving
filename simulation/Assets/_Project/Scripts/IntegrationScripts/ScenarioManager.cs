using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Receives the scenario name from the Python/SUMO config message and
/// activates the correct ego vehicle and spline already placed in the scene.
///
/// SETUP
///   1. Attach to the same GameObject as SimulationController.
///   2. Place ego vehicles and splines in the scene, all DISABLED by default.
///   3. Drag the scene objects into the Inspector slots below.
///   4. Create a full-screen UI Canvas with a black Image, add a CanvasGroup,
///      set alpha to 0, and assign it to fadeOverlay.
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
    public ScenarioId defaultScenario = ScenarioId.EgoCar_Free_Drive;

    [Header("Transition")]
    [Tooltip("CanvasGroup on a full-screen black panel (alpha starts at 0)")]
    public CanvasGroup fadeOverlay;
    [Tooltip("Duration of each fade direction (seconds)")]
    public float fadeDuration = 0.4f;

    [Header("Runtime State (read-only)")]
    [SerializeField] private ScenarioId _activeScenario;
    [SerializeField] private bool _scenarioActive;

    private SimulationController _simController;
    private DrivingEvaluator drivingEvaluator;
    private RouteArrowSpawner _arrowSpawner;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        _simController = GetComponent<SimulationController>();
        drivingEvaluator = GetComponent<DrivingEvaluator>();
        _arrowSpawner = GetComponent<RouteArrowSpawner>();
    }

    private void Start()
    {
        // apply default immediately (no fade on initial load)
        ApplyScenarioImmediate(defaultScenario);
    }

    private void OnDestroy()
    {
        // Export any remaining evaluation data when the application quits
        if (drivingEvaluator != null)
            drivingEvaluator.EndEvaluation();
    }

    /// <summary>
    /// Called by SimulationController when a "config" message arrives from Python.
    /// Parses the scenario name string into a ScenarioId.
    /// If a fadeOverlay is assigned, fades to black before switching, then fades back in.
    /// </summary>
    public void ApplyScenario(string scenarioName)
    {
        if (!Enum.TryParse(scenarioName, out ScenarioId id))
        {
            Debug.LogWarning($"ScenarioManager: Unknown scenario string '{scenarioName}'.");
            return;
        }

        // skip if this scenario is already active (avoids work on repeated config messages)
        if (_scenarioActive && id == _activeScenario)
            return;

        if (fadeOverlay != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeTransition(id));
        }
        else
        {
            ApplyScenarioImmediate(id);
        }
    }

    private IEnumerator FadeTransition(ScenarioId scenario)
    {
        // fade to black
        yield return FadeOverlay(0f, 1f);

        ApplyScenarioImmediate(scenario);

        // fade back in
        yield return FadeOverlay(1f, 0f);
        _fadeCoroutine = null;
    }

    private IEnumerator FadeOverlay(float from, float to)
    {
        float elapsed = 0f;
        fadeOverlay.alpha = from;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = to;
    }

    private void ApplyScenarioImmediate(ScenarioId scenario)
    {
        // Export evaluation data from the previous scenario before switching
        if (drivingEvaluator != null && _scenarioActive)
            drivingEvaluator.EndEvaluation();

        _activeScenario = scenario;
        _scenarioActive = true;
        Debug.Log($"ScenarioManager: Applying scenario '{scenario}'");

        // Disable everything first
        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);
        if (carRightTurnSpline != null) carRightTurnSpline.SetActive(false);
        if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(false);

        GameObject activeEgo = null;

        switch (scenario)
        {
            case ScenarioId.EgoCar_Free_Drive:
                activeEgo = egoCar;
                break;

            case ScenarioId.EgoCar_Right_Turn:
                activeEgo = egoCar;
                if (carRightTurnSpline != null) carRightTurnSpline.SetActive(true);
                break;

            case ScenarioId.EgoBike_Free_Bike:
                activeEgo = egoBike;
                break;

            case ScenarioId.EgoBike_Right_Turn:
                activeEgo = egoBike;
                if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(true);
                break;

            default:
                Debug.LogWarning($"ScenarioManager: Unhandled scenario '{scenario}'.");
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

            // Spawn route arrows along the active spline (if any)
            if (_arrowSpawner != null)
            {
                Spline activeSpline = FindFirstObjectByType<Spline>();
                _arrowSpawner.SpawnArrows(activeSpline);
            }

            // Start driving evaluation for this scenario
            if (drivingEvaluator != null)
                drivingEvaluator.BeginEvaluation(activeEgo, scenario);
        }
    }
}
