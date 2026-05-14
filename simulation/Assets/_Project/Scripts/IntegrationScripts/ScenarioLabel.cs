using TMPro;
using UnityEngine;

/// <summary>
/// Displays the currently active scenario name on a TextMeshProUGUI element.
/// Attach to the same GameObject as the label, or assign via Inspector.
/// </summary>
public class ScenarioLabel : MonoBehaviour
{
    [Tooltip("ScenarioManager to observe.")]
    [SerializeField] private ScenarioManager _scenarioManager;

    [Tooltip("TMP text element that shows the scenario name.")]
    [SerializeField] private TextMeshProUGUI _label;

    [Tooltip("Text prepended to the scenario name, e.g. \"Scenario: \".")]
    [SerializeField] private string _prefix = "Scenario: ";

    private void OnEnable()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged += UpdateLabel;
        if (_label != null)
            _label.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged -= UpdateLabel;
        if (_label != null)
            _label.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Initialize text immediately in case the event already fired before OnEnable
        if (_scenarioManager != null)
            UpdateLabel(_scenarioManager.ActiveScenario);
    }

    private void UpdateLabel(ScenarioId scenario)
    {
        if (_label != null)
            _label.text = _prefix + scenario.ToString().Replace('_', ' ');
    }
}
