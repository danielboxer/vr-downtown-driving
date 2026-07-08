using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Receives the scenario name from the Python/SUMO config message and
/// activates the correct ego vehicle and spline already placed in the scene.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("Ego Vehicles (disabled in scene)")]
    public GameObject egoCar;
    public GameObject egoBike;

    [Header("Spline Paths (disabled in scene)")]
    [Tooltip("Spline for right_turn_car scenario")]
    public GameObject carRightTurnSpline;
    [Tooltip("Spline for right_turn_bike scenario")]
    public GameObject bikeRightTurnSpline;

    [Header("Default (used when running without SUMO)")]
    [Tooltip("Scenario to activate at Start if no config message arrives")]
    public ScenarioId defaultScenario = ScenarioId.calibration_car;
    [Tooltip("When true, the default scenario is not applied at Start; call StartScenario() (e.g. from the main menu Play button) instead.")]
    public bool deferStart = false;

    [Header("Transition")]
    [Tooltip("Transparent black material for the fade quad")]
    public Material vrFadeMaterial;
    [Tooltip("Duration of each fade direction (seconds)")]
    public float fadeDuration = 0.4f;

    [Header("Runtime State")]
    [ReadOnly, SerializeField] private ScenarioId _activeScenario;
    [ReadOnly, SerializeField] private bool _scenarioActive;

    /// <summary>Fired whenever the active scenario changes.</summary>
    public event Action<ScenarioId> OnScenarioChanged;
    /// <summary>The scenario that is currently active.</summary>
    public ScenarioId ActiveScenario => _activeScenario;

    private SimulationController _simController;
    private DrivingEvaluator drivingEvaluator;
    private RouteArrowSpawner _arrowSpawner;
    private Coroutine _fadeCoroutine;
    // tracks the scenario currently being transitioned to (set before the coroutine starts)
    private ScenarioId? _pendingScenario;
    // scene-defined spawn transforms captured in Awake before any physics runs
    private Vector3 _egoCarSpawnPos;
    private Quaternion _egoCarSpawnRot;
    private Vector3 _egoBikeSpawnPos;
    private Quaternion _egoBikeSpawnRot;

    private void Awake()
    {
        _simController = GetComponent<SimulationController>();
        drivingEvaluator = GetComponent<DrivingEvaluator>();
        _arrowSpawner = GetComponent<RouteArrowSpawner>();

        // Capture scene-defined spawn transforms before any scenario enables the ego vehicles.
        if (egoCar != null) { _egoCarSpawnPos = egoCar.transform.position; _egoCarSpawnRot = egoCar.transform.rotation; }
        if (egoBike != null) { _egoBikeSpawnPos = egoBike.transform.position; _egoBikeSpawnRot = egoBike.transform.rotation; }
    }

    private void Start()
    {
        if (!deferStart)
            StartScenario();
    }

    /// <summary>
    /// Applies the default scenario (no fade). The WebGL build has no Scenario Manager
    /// to switch scenarios, so it boots straight into downtown. Called at Start unless
    /// deferStart is set, in which case the main menu Play button calls it.
    /// </summary>
    public void StartScenario()
    {
#if UNITY_WEBGL
        ApplyScenarioImmediate(ScenarioId.downtown_car);
#else
        ApplyScenarioImmediate(defaultScenario);
#endif
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

        // skip if a fade to this same scenario is already running (repeated warm-up messages)
        if (_fadeCoroutine != null && _pendingScenario == id)
            return;

        if (vrFadeMaterial != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _pendingScenario = id;
            _fadeCoroutine = StartCoroutine(FadeTransition(id));
        }
        else
        {
            ApplyScenarioImmediate(id);
        }
    }

    /// <summary>
    /// Tears the scenario down to the pre-Play state: ends evaluation, clears NPC
    /// vehicles and disables the ego vehicles. Used when returning to the main menu.
    /// </summary>
    public void StopScenario()
    {
        if (drivingEvaluator != null && _scenarioActive)
            drivingEvaluator.EndEvaluation();
        if (_simController != null)
            _simController.ClearAllNpcVehicles();
        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);
        _scenarioActive = false;
    }

    /// <summary>
    /// Restarts the currently active scenario from the spawn position.
    /// Unlike ApplyScenario, this always runs even if the scenario is already active.
    /// </summary>
    public void RestartScenario()
    {
        if (!_scenarioActive) return;
        if (_fadeCoroutine != null) return; // transition already in progress, ignore
        _pendingScenario = _activeScenario;
        if (vrFadeMaterial != null)
            _fadeCoroutine = StartCoroutine(FadeTransition(_activeScenario));
        else
            ApplyScenarioImmediate(_activeScenario);
    }

    private IEnumerator FadeTransition(ScenarioId scenario)
    {
        // Create a fade quad on the current active camera.
        // Using Camera.main at transition time handles camera changes between car and bike scenarios.
        GameObject vrQuadGO = null;
        Renderer vrQuadRenderer = null;
        Camera cam = Camera.main;
        if (cam != null)
        {
            vrQuadGO = CreateFadeQuad(cam);
            vrQuadRenderer = vrQuadGO.GetComponent<Renderer>();
            vrQuadRenderer.material = Instantiate(vrFadeMaterial);
        }

        // fade to black
        yield return FadeOverlay(vrQuadRenderer, 0f, 1f);

        ApplyScenarioImmediate(scenario);

        // fade back in
        yield return FadeOverlay(vrQuadRenderer, 1f, 0f);

        if (vrQuadGO != null) Destroy(vrQuadGO);
        _fadeCoroutine = null;
        _pendingScenario = null;
    }

    /// <summary>
    /// Fades to black, runs <paramref name="atBlack"/>, then fades back in. Used by the
    /// replay loop to hide the traffic reset seam. Kept independent of the scenario-change
    /// fade so it doesn't disturb _fadeCoroutine / _pendingScenario state.
    /// </summary>
    public Coroutine FadeThrough(Action atBlack)
    {
        return StartCoroutine(FadeThroughRoutine(atBlack));
    }

    private IEnumerator FadeThroughRoutine(Action atBlack)
    {
        GameObject quadGO = null;
        Renderer quadRenderer = null;
        Camera cam = Camera.main;
        if (cam != null && vrFadeMaterial != null)
        {
            quadGO = CreateFadeQuad(cam);
            quadRenderer = quadGO.GetComponent<Renderer>();
            quadRenderer.material = Instantiate(vrFadeMaterial);
        }

        yield return FadeOverlay(quadRenderer, 0f, 1f);
        atBlack?.Invoke();
        yield return FadeOverlay(quadRenderer, 1f, 0f);

        if (quadGO != null) Destroy(quadGO);
    }

    private IEnumerator FadeOverlay(Renderer quadRenderer, float from, float to)
    {
        float elapsed = 0f;
        if (quadRenderer != null)
        {
            SetMaterialAlpha(quadRenderer.material, from);
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                SetMaterialAlpha(quadRenderer.material, Mathf.Lerp(from, to, elapsed / fadeDuration));
                yield return null;
            }
            SetMaterialAlpha(quadRenderer.material, to);
        }
        else
        {
            // No camera available — just wait out the duration so timing stays consistent.
            yield return new WaitForSeconds(fadeDuration);
        }
    }

    // Creates a quad parented to the camera, sized to fill its FOV.
    private GameObject CreateFadeQuad(Camera cam)
    {
        var quadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGO.name = "VRFadeQuad";
        Destroy(quadGO.GetComponent<MeshCollider>());
        quadGO.transform.SetParent(cam.transform, false);

        // Place just past the near clip plane so nothing in the scene can render in front.
        float dist = cam.nearClipPlane + 0.01f;
        // 2x margin because Camera.fieldOfView may not match the actual XR eye projection FOV.
        float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
        float halfW = halfH * Mathf.Max(cam.aspect, 1f) * 2f;

        quadGO.transform.localPosition = new Vector3(0f, 0f, dist);
        quadGO.transform.localRotation = Quaternion.identity;
        quadGO.transform.localScale = new Vector3(halfW * 2f, halfH * 2f, 1f);
        return quadGO;
    }

    // Sets material alpha on the _BaseColor property used by URP Unlit.
    private static void SetMaterialAlpha(Material mat, float alpha)
    {
        Color c = mat.GetColor("_BaseColor");
        c.a = alpha;
        mat.SetColor("_BaseColor", c);
    }

    private void ApplyScenarioImmediate(ScenarioId scenario)
    {
        // Export evaluation data from the previous scenario before switching
        if (drivingEvaluator != null && _scenarioActive)
            drivingEvaluator.EndEvaluation();

        _activeScenario = scenario;
        _scenarioActive = true;
        Debug.Log($"ScenarioManager: Applying scenario '{scenario}'");
        OnScenarioChanged?.Invoke(scenario);

        // Remove all active NPC vehicles before switching scenario.
        if (_simController != null)
            _simController.ClearAllNpcVehicles();

        // Disable everything first
        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);

        // Disable only the known route splines so they don't interfere with the new scenario.
        // Tree placement splines and other non-route splines are left untouched.
        if (carRightTurnSpline != null) carRightTurnSpline.SetActive(false);
        if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(false);

        GameObject activeEgo = null;
        Spline activeSpline = null;

        switch (scenario)
        {
            case ScenarioId.calibration_car:
                activeEgo = egoCar;
                break;

            case ScenarioId.downtown_car:
                activeEgo = egoCar;
                break;

            case ScenarioId.right_turn_car:
                activeEgo = egoCar;
                if (carRightTurnSpline != null)
                {
                    carRightTurnSpline.SetActive(true);
                    activeSpline = carRightTurnSpline.GetComponent<Spline>();
                }
                break;

            case ScenarioId.calibration_bike:
                activeEgo = egoBike;
                break;

            case ScenarioId.downtown_bike:
                activeEgo = egoBike;
                break;

            case ScenarioId.right_turn_bike:
                activeEgo = egoBike;
                if (bikeRightTurnSpline != null)
                {
                    bikeRightTurnSpline.SetActive(true);
                    activeSpline = bikeRightTurnSpline.GetComponent<Spline>();
                }
                break;

            default:
                Debug.LogWarning($"ScenarioManager: Unhandled scenario '{scenario}'.");
                return;
        }

        if (activeEgo != null)
        {
            // Reset to the scene-defined spawn position before enabling.
            Vector3 spawnPos = (activeEgo == egoCar) ? _egoCarSpawnPos : _egoBikeSpawnPos;
            Quaternion spawnRot = (activeEgo == egoCar) ? _egoCarSpawnRot : _egoBikeSpawnRot;
            var rb = activeEgo.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            activeEgo.transform.SetPositionAndRotation(spawnPos, spawnRot);

            activeEgo.SetActive(true);
            _simController.RegisterEgoVehicle(activeEgo);

            // Pass the active scenario spline directly — no FindFirstObjectByType needed
            var followCurve = activeEgo.GetComponent<FollowCurve>();
            if (followCurve != null)
                followCurve.RefreshSpline(activeSpline);

            // Spawn route arrows along the active spline (if any)
            if (_arrowSpawner != null)
                _arrowSpawner.SpawnArrows(activeSpline);

            // Start driving evaluation for this scenario
            if (drivingEvaluator != null)
                drivingEvaluator.BeginEvaluation(activeEgo, scenario);
        }
    }
}
