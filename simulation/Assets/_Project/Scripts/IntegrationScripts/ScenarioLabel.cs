using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Tooltip("Optional background panel Image. Assign the panel's Image component to toggle it with the label.")]
    [SerializeField] private Image _background;

    private void OnEnable()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged += UpdateLabel;
        if (_label != null)
            _label.gameObject.SetActive(true);
        if (_background != null)
            _background.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged -= UpdateLabel;
        if (_label != null)
            _label.gameObject.SetActive(false);
        if (_background != null)
            _background.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Initialize text immediately in case the event already fired before OnEnable
        if (_scenarioManager != null)
            UpdateLabel(_scenarioManager.ActiveScenario);
    }

    private void UpdateLabel(ScenarioId scenario)
    {
        if (_label == null) return;
        _label.text = _prefix + scenario.ToString().Replace('_', ' ');
    }
}
