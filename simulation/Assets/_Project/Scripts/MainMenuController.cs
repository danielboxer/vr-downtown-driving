using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

/// <summary>
/// Full-screen main menu shown at load. Freezes the sim until Play is pressed, then
/// starts the scenario. Options reuses the in-game MenuController panel; Controls and
/// About are sub-panels with a Back button.
/// </summary>
// Runs after MenuController so hiding the HUD wins over MenuController.Start showing it.
[DefaultExecutionOrder(1000)]
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject controlsPanel;
    public GameObject aboutPanel;

    [Header("Main menu buttons")]
    public Button playButton;
    public Button controlsButton;
    public Button optionsButton;
    public Button aboutButton;
    public Button exitButton;

    [Header("Back buttons")]
    public Button controlsBackButton;
    public Button aboutBackButton;
    [Tooltip("The in-game menu button that returns to the main menu (repurposed Exit button).")]
    public Button menuBackButton;

    [Header("References")]
    public MenuController menuController;
    public ScenarioManager scenarioManager;

    [Tooltip("AudioListener active only while the menu is showing (the ego vehicle's listener takes over during play).")]
    public AudioListener menuAudioListener;

    [Tooltip("Cinemachine flythrough camera rig shown behind the menu; disabled on Play so the ego camera takes over.")]
    public GameObject flythroughRig;

    [Header("HUD hidden while the menu is up")]
    [Tooltip("The in-game menu toggle button. Shown when Options is open so the panel can be closed, hidden otherwise.")]
    public GameObject openMenuButton;
    [Tooltip("Other HUD elements to hide until Play (FPS counter, scenario label, etc).")]
    public GameObject[] hudToHide;

    private bool _started;

    // The menu canvas is screen space, so a headset user has no pointer to click Play with.
    private IDisposable _anyButtonPress;

    private void Awake()
    {
        // Freeze from the first frame so nothing moves behind the menu.
        Time.timeScale = 0f;
        if (scenarioManager != null)
            scenarioManager.deferStart = true;

        if (playButton != null) playButton.onClick.AddListener(Play);
        if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
        if (optionsButton != null) optionsButton.onClick.AddListener(OpenOptions);
        if (aboutButton != null) aboutButton.onClick.AddListener(OpenAbout);
        if (controlsBackButton != null) controlsBackButton.onClick.AddListener(ShowMainMenu);
        if (aboutBackButton != null) aboutBackButton.onClick.AddListener(ShowMainMenu);
        if (menuBackButton != null) menuBackButton.onClick.AddListener(ReturnToMainMenu);
        if (exitButton != null && menuController != null) exitButton.onClick.AddListener(menuController.OnExit);
    }

    private void Start()
    {
        if (menuController != null)
            menuController.MenuOpenChanged += OnMenuOpenChanged;
        ShowMainMenu();
    }

    private void OnEnable()
    {
        _anyButtonPress = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);
    }

    private void OnDisable()
    {
        if (_anyButtonPress != null)
        {
            _anyButtonPress.Dispose();
            _anyButtonPress = null;
        }
    }

    // Main menu only, so a trigger pull aimed at Options or Controls does not skip past it.
    private void OnAnyButtonPressed(UnityEngine.InputSystem.InputControl control)
    {
        if (_started) return;
        if (mainMenuPanel == null || !mainMenuPanel.activeSelf) return;
        if (!VrActive.IsActive) return;
        if (!(control.device is UnityEngine.InputSystem.XR.XRController)) return;
        Play();
    }

    private void OnDestroy()
    {
        if (menuController != null)
            menuController.MenuOpenChanged -= OnMenuOpenChanged;
    }

    private void ShowMainMenu()
    {
        // Esc must not pull up the options panel from here, only the Options button.
        if (menuController != null) menuController.ToggleMenuKeyEnabled = false;
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (controlsPanel != null) controlsPanel.SetActive(false);
        if (aboutPanel != null) aboutPanel.SetActive(false);
        SetHudVisible(false);
        if (menuAudioListener != null) menuAudioListener.enabled = true;
        if (flythroughRig != null) flythroughRig.SetActive(true);
    }

    public void Play()
    {
        _started = true;
        if (menuController != null)
        {
            menuController.ToggleMenuKeyEnabled = true;
            menuController.MenuOpenChanged -= OnMenuOpenChanged;
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(false);
        if (aboutPanel != null) aboutPanel.SetActive(false);

        if (menuAudioListener != null) menuAudioListener.enabled = false;
        if (flythroughRig != null) flythroughRig.SetActive(false);
        SetHudVisible(true);
        Time.timeScale = 1f;
        if (scenarioManager != null)
            scenarioManager.StartScenario();
        if (menuController != null)
            menuController.RunSimulatorSetup();
    }

    /// <summary>Returns to the frozen main menu from the in-game menu (works during or before play).</summary>
    public void ReturnToMainMenu()
    {
        _started = false;
        Time.timeScale = 0f;
        if (scenarioManager != null)
            scenarioManager.StopScenario();
        if (menuController != null)
        {
            menuController.MenuOpenChanged -= OnMenuOpenChanged;
            menuController.MenuOpenChanged += OnMenuOpenChanged;
            // Closing the menu fires MenuOpenChanged(false) -> OnMenuOpenChanged -> ShowMainMenu.
            menuController.SetMenuOpen(false);
        }
        else
        {
            ShowMainMenu();
        }
    }

    public void OpenControls()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(true);
    }

    public void OpenAbout()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (aboutPanel != null) aboutPanel.SetActive(true);
    }

    // Reuse the in-game menu as the options screen. It closes via its own "Back to
    // Main Menu" button; OnMenuOpenChanged returns here on close.
    public void OpenOptions()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (menuController != null)
        {
            // Esc closes the panel again and lands back on the main menu.
            menuController.ToggleMenuKeyEnabled = true;
            menuController.SetMenuOpen(true);
        }
    }

    private void OnMenuOpenChanged(bool open)
    {
        if (_started) return;
        if (!open)
            ShowMainMenu();
        else if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
    }

    private void SetHudVisible(bool visible)
    {
        if (openMenuButton != null) openMenuButton.SetActive(visible);
        foreach (var go in hudToHide)
            if (go != null) go.SetActive(visible);
    }
}
