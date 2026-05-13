using System.Collections;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using Debug = UnityEngine.Debug;

/// <summary>
/// Handles the in-game overlay menu.
/// </summary>
public class MenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The panel GameObject that contains all the menu buttons.")]
    public GameObject menuPanel;
    [Tooltip("The HUD toggle button outside the panel (shown when panel is hidden, hidden when panel is visible).")]
    public GameObject menuToggleButton;
    [Tooltip("Image component on the HUD toggle button used to swap between open/close icons.")]
    public Image menuToggleButtonIcon;
    [Tooltip("Icon shown when the menu is closed (e.g. gear/settings).")]
    public Sprite openMenuSprite;
    [Tooltip("Icon shown when the menu is open (e.g. X/close).")]
    public Sprite closeMenuSprite;

    [Header("Scenario Manager EXE")]
    [Tooltip("Absolute path to the ScenarioManager executable. Populate once the PyInstaller binary is built.")]
    public string scenarioManagerExePath = "";

    [Header("Input Mode")]
    [Tooltip("The XR Interaction Simulator root GameObject. Place it in the scene disabled; auto-enabled when no real XR device is detected.")]
    public GameObject xrInteractionSimulator;
    [Tooltip("The XR Interaction Simulator UI overlay. Toggle independently of the simulator itself.")]
    public GameObject xrSimulatorHUD;

    [Header("Feedback")]
    [Tooltip("TMP text element inside the menu panel that shows brief action feedback.")]
    public TextMeshProUGUI feedbackText;
    [Tooltip("How long (seconds) before the feedback message fades out.")]
    public float feedbackDuration = 2f;

    [Header("Input")]
    [Tooltip("Assign InputSystem_Actions asset. The ToggleMenu action is resolved from the Driving map.")]
    public InputActionAsset inputActions;

    private InputAction _toggleMenuAction;
    private Coroutine _feedbackCoroutine;
    private ScenarioManager _scenarioManager;
    private TiltSteeringProvider _tiltSteering;
    private Fps _fpsDisplay;

    // whether the HUD elements (FPS counter + toggle button) are shown
    private bool _displayVisible = true;
    // whether the menu panel is currently open
    private bool _menuOpen = false;

    private void Awake()
    {
        if (inputActions != null)
            _toggleMenuAction = inputActions.FindActionMap("Driving", false)?.FindAction("ToggleMenu", false);
    }

    private void Start()
    {
        _scenarioManager = FindFirstObjectByType<ScenarioManager>();
        _tiltSteering = FindFirstObjectByType<TiltSteeringProvider>();
        _fpsDisplay = FindFirstObjectByType<Fps>();

        // Auto-enable the interaction simulator when no real XR device is running.
        if (xrInteractionSimulator != null)
            xrInteractionSimulator.SetActive(!XRSettings.isDeviceActive);

        // Auto-find the simulator HUD if not manually assigned (it's instantiated as a
        // child of the simulator prefab in Awake, so it exists by the time Start runs).
        if (xrSimulatorHUD == null && xrInteractionSimulator != null)
        {
            foreach (Transform child in xrInteractionSimulator.transform)
            {
                if (child.name.StartsWith("XR Interaction Simulator UI"))
                {
                    xrSimulatorHUD = child.gameObject;
                    break;
                }
            }
        }

        // Panel starts hidden; FPS and toggle button start visible.
        _displayVisible = true;
        _menuOpen = false;
        RefreshUI();
    }

    private void OnEnable()
    {
        if (_toggleMenuAction != null)
        {
            _toggleMenuAction.Enable();
            _toggleMenuAction.performed += OnToggleMenuPerformed;
        }
    }

    private void OnDisable()
    {
        if (_toggleMenuAction != null)
        {
            _toggleMenuAction.performed -= OnToggleMenuPerformed;
            _toggleMenuAction.Disable();
        }
    }

    private void OnToggleMenuPerformed(InputAction.CallbackContext ctx)
    {
        OnToggleMenu();
    }

    private void RefreshUI()
    {
        if (menuPanel != null) menuPanel.SetActive(_menuOpen);
        // Button is visible whenever the HUD is on; the icon switches between gear (closed) and X (open).
        if (menuToggleButton != null) menuToggleButton.SetActive(_displayVisible);
        if (_fpsDisplay != null) _fpsDisplay.enabled = _displayVisible;
        // Swap the button icon based on whether the menu is currently open.
        if (menuToggleButtonIcon != null)
            menuToggleButtonIcon.sprite = _menuOpen ? closeMenuSprite : openMenuSprite;
        // Simulator HUD follows the same visibility as the rest of the display.
        if (xrSimulatorHUD != null) xrSimulatorHUD.SetActive(_displayVisible);
    }

    // ── Button handlers ────────────────────────────────────────────────────────

    /// <summary>Opens or closes the menu panel. Wire to the HUD toggle button and the panel's close button.</summary>
    public void OnToggleMenu()
    {
        _menuOpen = !_menuOpen;
        RefreshUI();
    }

    /// <summary>Hides or shows the FPS counter and HUD toggle button.</summary>
    public void OnToggleDisplay()
    {
        _displayVisible = !_displayVisible;
        RefreshUI();
        ShowFeedback(_displayVisible ? "Display on" : "Display hidden");
    }

    /// <summary>Launches the ScenarioManager PyInstaller binary in a separate process.</summary>
    public void OnOpenScenarioManager()
    {
        // Use the Inspector-assigned path if set, otherwise fall back to a path
        // relative to the Unity build directory (sibling of the game .exe).
        string exePath = !string.IsNullOrEmpty(scenarioManagerExePath)
            ? scenarioManagerExePath
            : System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "ScenarioManager.exe"));

        if (!System.IO.File.Exists(exePath))
        {
            Debug.LogWarning($"[MenuController] Scenario Manager executable not found at: {exePath}");
            ShowFeedback("Scenario Manager not found");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });
            ShowFeedback("Scenario Manager launched");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MenuController] Failed to launch Scenario Manager: {ex.Message}");
            ShowFeedback("Failed to launch Scenario Manager");
        }
    }

    /// <summary>Triggers calibrate-steering (same as the Calibrate keybind).</summary>
    public void OnCalibrateSteering()
    {
        if (_tiltSteering != null)
        {
            _tiltSteering.Calibrate();
            ShowFeedback("Steering calibrated");
        }
        else
        {
            Debug.LogWarning("[MenuController] No TiltSteeringProvider found in scene.");
        }
    }

    /// <summary>Restarts the scenario and sends the SUMO restart command</summary>
    public void OnRestartScenario()
    {
        ExchangeData.SendCommand("{\"type\":\"command\",\"command\":\"RESTART_SIMULATION\"}");
        ShowFeedback("Restarting...");

        if (_scenarioManager != null)
            _scenarioManager.RestartScenario();
        else
            Debug.LogWarning("[MenuController] No ScenarioManager found in scene.");
    }

    /// <summary>Toggles the XR Interaction Simulator on or off.</summary>
    public void OnToggleInteractionSimulator()
    {
        if (xrInteractionSimulator != null)
        {
            bool next = !xrInteractionSimulator.activeSelf;
            xrInteractionSimulator.SetActive(next);
            ShowFeedback(next ? "XR Simulator: ON" : "XR Simulator: OFF");
        }
        else
        {
            Debug.LogWarning("[MenuController] No XR Interaction Simulator assigned.");
        }
    }

    /// <summary>Hides or shows the XR Interaction Simulator HUD overlay without disabling the simulator input.</summary>
    public void OnToggleSimulatorHUD()
    {
        if (xrSimulatorHUD != null)
        {
            bool next = !xrSimulatorHUD.activeSelf;
            xrSimulatorHUD.SetActive(next);
            ShowFeedback(next ? "Simulator HUD: ON" : "Simulator HUD: OFF");
        }
        else
        {
            Debug.LogWarning("[MenuController] No XR Simulator HUD assigned.");
        }
    }

    /// <summary>Shows a brief feedback message in the menu panel, then fades it out.</summary>
    public void ShowFeedback(string message)
    {
        if (feedbackText == null) return;

        if (_feedbackCoroutine != null)
            StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(FeedbackRoutine(message));
    }

    private IEnumerator FeedbackRoutine(string message)
    {
        feedbackText.text = message;

        // hold for most of the duration, then fade alpha out
        float holdTime = feedbackDuration * 0.7f;
        float fadeTime = feedbackDuration * 0.3f;

        yield return new WaitForSeconds(holdTime);

        float elapsed = 0f;
        Color c = feedbackText.color;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            feedbackText.color = c;
            yield return null;
        }

        feedbackText.text = "";
        c.a = 1f;
        feedbackText.color = c;
        _feedbackCoroutine = null;
    }

    /// <summary>Quits the application (also stops Play mode in the Editor).</summary>
    public void OnExit()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}

