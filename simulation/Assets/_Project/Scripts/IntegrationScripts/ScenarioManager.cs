using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

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

    public event Action<ScenarioId> OnScenarioChanged;
    public ScenarioId ActiveScenario => _activeScenario;

    private SimulationController _simController;
    private DrivingEvaluator drivingEvaluator;
    private RouteArrowSpawner _arrowSpawner;
    private Coroutine _fadeCoroutine;
    // the running FadeTransition's quad, so an interrupted transition can destroy it
    private GameObject _activeFadeQuad;
    private ScenarioId? _pendingScenario;
    // captured in Awake before any physics runs
    private Vector3 _egoCarSpawnPos;
    private Quaternion _egoCarSpawnRot;
    private Vector3 _egoBikeSpawnPos;
    private Quaternion _egoBikeSpawnRot;

    private void Awake()
    {
        _simController = GetComponent<SimulationController>();
        drivingEvaluator = GetComponent<DrivingEvaluator>();
        _arrowSpawner = GetComponent<RouteArrowSpawner>();

        // captured before any scenario enables the ego vehicles
        if (egoCar != null) { _egoCarSpawnPos = egoCar.transform.position; _egoCarSpawnRot = egoCar.transform.rotation; }
        if (egoBike != null) { _egoBikeSpawnPos = egoBike.transform.position; _egoBikeSpawnRot = egoBike.transform.rotation; }
    }

    private void Start()
    {
        if (!deferStart)
            StartScenario();
    }

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
        if (drivingEvaluator != null)
            drivingEvaluator.EndEvaluation();
    }

    public void ApplyScenario(string scenarioName)
    {
        if (!Enum.TryParse(scenarioName, out ScenarioId id))
        {
            Debug.LogWarning($"ScenarioManager: Unknown scenario string '{scenarioName}'.");
            return;
        }

        // repeated config messages would otherwise redo the work
        if (_scenarioActive && id == _activeScenario)
            return;

        // repeated warm-up messages would otherwise restart the fade
        if (_fadeCoroutine != null && _pendingScenario == id)
            return;

        if (vrFadeMaterial != null)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                // the stopped coroutine never reaches its Destroy, so clean up its quad here
                if (_activeFadeQuad != null) { Destroy(_activeFadeQuad); _activeFadeQuad = null; }
            }
            _pendingScenario = id;
            _fadeCoroutine = StartCoroutine(FadeTransition(id));
        }
        else
        {
            ApplyScenarioImmediate(id);
        }
    }

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
        // Camera.main at transition time handles the camera changing between car and bike scenarios
        GameObject vrQuadGO = null;
        Renderer vrQuadRenderer = null;
        Camera cam = Camera.main;
        if (cam != null)
        {
            vrQuadGO = CreateFadeQuad(cam);
            vrQuadRenderer = vrQuadGO.GetComponent<Renderer>();
            vrQuadRenderer.material = Instantiate(vrFadeMaterial);
            _activeFadeQuad = vrQuadGO;
        }

        yield return FadeOverlay(vrQuadRenderer, 0f, 1f);

        ApplyScenarioImmediate(scenario);

        yield return FadeOverlay(vrQuadRenderer, 1f, 0f);

        if (vrQuadGO != null) Destroy(vrQuadGO);
        _activeFadeQuad = null;
        _fadeCoroutine = null;
        _pendingScenario = null;
    }

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
            // no camera, so just wait out the duration to keep timing consistent
            yield return new WaitForSeconds(fadeDuration);
        }
    }

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

    private static void SetMaterialAlpha(Material mat, float alpha)
    {
        Color c = mat.GetColor("_BaseColor");
        c.a = alpha;
        mat.SetColor("_BaseColor", c);
    }

    private void ApplyScenarioImmediate(ScenarioId scenario)
    {
        if (drivingEvaluator != null && _scenarioActive)
            drivingEvaluator.EndEvaluation();

        _activeScenario = scenario;
        _scenarioActive = true;
        Debug.Log($"ScenarioManager: Applying scenario '{scenario}'");
        OnScenarioChanged?.Invoke(scenario);

        if (_simController != null)
            _simController.ClearAllNpcVehicles();

        if (egoCar != null) egoCar.SetActive(false);
        if (egoBike != null) egoBike.SetActive(false);

            // only the known route splines, tree placement splines must stay enabled
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
            Vector3 spawnPos = (activeEgo == egoCar) ? _egoCarSpawnPos : _egoBikeSpawnPos;
            Quaternion spawnRot = (activeEgo == egoCar) ? _egoCarSpawnRot : _egoBikeSpawnRot;
            var rb = activeEgo.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            activeEgo.transform.SetPositionAndRotation(spawnPos, spawnRot);

            activeEgo.SetActive(true);
            _simController.RegisterEgoVehicle(activeEgo);

            var followCurve = activeEgo.GetComponent<FollowCurve>();
            if (followCurve != null)
                followCurve.RefreshSpline(activeSpline);

            if (_arrowSpawner != null)
                _arrowSpawner.SpawnArrows(activeSpline);

            if (drivingEvaluator != null)
                drivingEvaluator.BeginEvaluation(activeEgo, scenario);
        }
    }
}
