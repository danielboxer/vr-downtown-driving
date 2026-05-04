using UnityEngine;
using UnityEngine.InputSystem;
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

    [Header("Transition")]
    [Tooltip("Transparent black material for the fade quad")]
    public Material vrFadeMaterial;
    [Tooltip("Duration of each fade direction (seconds)")]
    public float fadeDuration = 0.4f;

    [Header("Input")]
    [Tooltip("Assign InputSystem_Actions asset. The Restart action is resolved from the Driving map.")]
    public InputActionAsset inputActions;

    [Header("Runtime State")]
    [ReadOnly, SerializeField] private ScenarioId _activeScenario;
    [ReadOnly, SerializeField] private bool _scenarioActive;

    private SimulationController _simController;
    private DrivingEvaluator drivingEvaluator;
    private RouteArrowSpawner _arrowSpawner;
    private Coroutine _fadeCoroutine;
    private InputAction _restartAction;
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

        if (inputActions != null)
            _restartAction = inputActions.FindActionMap("Driving", false)?.FindAction("Restart", false);

        // Capture scene-defined spawn transforms before any scenario enables the ego vehicles.
        if (egoCar != null) { _egoCarSpawnPos = egoCar.transform.position; _egoCarSpawnRot = egoCar.transform.rotation; }
        if (egoBike != null) { _egoBikeSpawnPos = egoBike.transform.position; _egoBikeSpawnRot = egoBike.transform.rotation; }
    }

    private void OnEnable()
    {
        if (_restartAction != null)
        {
            _restartAction.Enable();
            _restartAction.performed += OnRestartPerformed;
        }
    }

    private void OnDisable()
    {
        if (_restartAction != null)
        {
            _restartAction.performed -= OnRestartPerformed;
            _restartAction.Disable();
        }
    }

    private void OnRestartPerformed(InputAction.CallbackContext ctx)
    {
        // Sends RESTART_SIMULATION to Python (triggers traci.load()) and
        // respawns the ego vehicle here in Unity.
        ExchangeData.SendCommand("{\"type\":\"command\",\"command\":\"RESTART_SIMULATION\"}");
        RestartScenario();
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
    /// Restarts the currently active scenario from the spawn position.
    /// Unlike ApplyScenario, this always runs even if the scenario is already active.
    /// </summary>
    public void RestartScenario()
    {
        if (!_scenarioActive) return;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
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

        // Disable everything first
        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);

        // Disable ALL splines in the scene (including any not tracked in Inspector fields)
        // so FindFirstObjectByType<Spline>() in FollowCurve.RefreshSpline() doesn't pick
        // up a leftover spline that belongs to a different scenario.
        var allSplines = FindObjectsByType<Spline>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var s in allSplines)
            s.gameObject.SetActive(false);

        GameObject activeEgo = null;

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
                if (carRightTurnSpline != null) carRightTurnSpline.SetActive(true);
                break;

            case ScenarioId.calibration_bike:
                activeEgo = egoBike;
                break;

            case ScenarioId.downtown_bike:
                activeEgo = egoBike;
                break;

            case ScenarioId.right_turn_bike:
                activeEgo = egoBike;
                if (bikeRightTurnSpline != null) bikeRightTurnSpline.SetActive(true);
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
