using System;
using System.Collections;
using System.Collections.Generic;
#if !UNITY_WEBGL
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using Debug = UnityEngine.Debug;

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
    [Tooltip("Absolute path override for ScenarioManager.exe. Leave empty to use automatic defaults: simulation/build/ScenarioManager.exe in the Editor, ScenarioManager.exe beside the game .exe in a build.")]
    public string scenarioManagerExePath = "";

    [Header("Input Mode")]
    [Tooltip("The XR Interaction Simulator root GameObject. Place it in the scene disabled; auto-enabled when no real XR device is detected.")]
    public GameObject xrInteractionSimulator;
    [Tooltip("The XR Interaction Simulator UI overlay. Toggle independently of the simulator itself.")]
    public GameObject xrSimulatorHUD;
    [Tooltip("When true, the simulator is not auto-configured at Start; call RunSimulatorSetup() (e.g. from the main menu Play button) instead.")]
    public bool deferSimulatorSetup = false;

    [Header("Button Labels")]
    [Tooltip("TMP label on the Display toggle button; set automatically to show current state.")]
    public TextMeshProUGUI displayButtonLabel;
    [Tooltip("TMP label on the Controller Visuals toggle button; set automatically to show current state.")]
    public TextMeshProUGUI controllerButtonLabel;
    [Tooltip("TMP label on the XR Simulator toggle button; set automatically to show current state.")]
    public TextMeshProUGUI simulatorButtonLabel;
    [Tooltip("TMP label on the Acceleration toggle button; set automatically to show current mode.")]
    public TextMeshProUGUI accelerationButtonLabel;
    [Tooltip("TMP label on the Vignette toggle button; set automatically to show the current level.")]
    public TextMeshProUGUI vignetteButtonLabel;
    [Tooltip("TMP label on the Max Speed row; set automatically to show the current value.")]
    public TextMeshProUGUI maxSpeedLabel;
    [Tooltip("TMP label on the Master Volume row; set automatically to show the current value.")]
    public TextMeshProUGUI masterVolumeLabel;
    [Tooltip("TMP label on the Warning Volume row; set automatically to show the current value.")]
    public TextMeshProUGUI warningVolumeLabel;

    [Header("Sliders")]
    [Tooltip("Sets the top speed for both vehicles. Counts whole 5 km/h steps, so its range is min/max divided by 5.")]
    public Slider maxSpeedSlider;
    [Tooltip("Sets the volume of everything. Counts whole percent.")]
    public Slider masterVolumeSlider;
    [Tooltip("Sets the volume of the evaluator's warning tone and voice prompts. Counts whole percent.")]
    public Slider warningVolumeSlider;

    [Header("Feedback")]
    [Tooltip("TMP text element inside the menu panel that shows brief action feedback.")]
    public TextMeshProUGUI feedbackText;
    [Tooltip("How long (seconds) before the feedback message fades out.")]
    public float feedbackDuration = 2f;

    [Header("Input")]
    [Tooltip("Assign InputSystem_Actions asset. The ToggleMenu action is resolved from the Driving map.")]
    public InputActionAsset inputActions;

    public event Action<bool> MenuOpenChanged;

    public bool ToggleMenuKeyEnabled { get; set; } = true;

    private InputAction _toggleMenuAction;
    private Coroutine _feedbackCoroutine;
    private ScenarioManager _scenarioManager;
    private DrivingEvaluator _drivingEvaluator;
    private TiltSteeringProvider _tiltSteering;
    private Fps _fpsDisplay;
    private ScenarioLabel _scenarioLabel;
#if !UNITY_WEBGL
    private Process _scenarioManagerProcess;
#endif
    private Coroutine _controllerVisibilitySyncCoroutine;

    private bool _displayVisible = true;
    private bool _menuOpen = false;
    private bool _controllersVisible = false;
    private Renderer[] _controllerRenderers = System.Array.Empty<Renderer>();
    private float _nextControllerVisibilitySyncTime;
    private const float ControllerVisibilitySyncInterval = 0.25f;

    private void Awake()
    {
        if (inputActions != null)
            _toggleMenuAction = inputActions.FindActionMap("Driving", false)?.FindAction("ToggleMenu", false);
    }

    private void Start()
    {
        _scenarioManager = FindFirstObjectByType<ScenarioManager>();
        _fpsDisplay = FindFirstObjectByType<Fps>();
        _scenarioLabel = FindFirstObjectByType<ScenarioLabel>();
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged += OnScenarioChanged;

        // cached so the visuals can be toggled without disabling the TrackedPoseDrivers
        CacheControllerRenderers();
        SetControllerRenderersVisible(_controllersVisible);

        SetMouseOwnedMenuPointer();

        // wait for the XR display subsystem, Quest 3 via SteamVR is not active yet when Start runs
        if (xrInteractionSimulator != null && !deferSimulatorSetup)
            StartCoroutine(AutoConfigureSimulator());

        _displayVisible = true;
        _menuOpen = false;
        RefreshUI();

        // the simulator label is set at the end of AutoConfigureSimulator once its state resolves
        SetToggleLabel(displayButtonLabel, "Display", _displayVisible);
        SetToggleLabel(controllerButtonLabel, "Controllers", _controllersVisible);
        SyncSettingControls();
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

    private void OnDestroy()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged -= OnScenarioChanged;
    }

    private void LateUpdate()
    {
        if (_controllersVisible)
            return;

        if (Time.unscaledTime >= _nextControllerVisibilitySyncTime)
        {
            _nextControllerVisibilitySyncTime = Time.unscaledTime + ControllerVisibilitySyncInterval;
            CacheControllerRenderers();
        }

        SetControllerRenderersVisible(false);
    }

    private void OnToggleMenuPerformed(InputAction.CallbackContext ctx)
    {
        if (!ToggleMenuKeyEnabled) return;
        OnToggleMenu();
    }

    private void RefreshUI()
    {
        if (menuPanel != null) menuPanel.SetActive(_menuOpen);
        // the icon switches between gear (closed) and X (open)
        if (menuToggleButton != null) menuToggleButton.SetActive(_displayVisible);
        if (_fpsDisplay != null) _fpsDisplay.enabled = _displayVisible;
        if (_scenarioLabel != null) _scenarioLabel.enabled = _displayVisible;
        if (menuToggleButtonIcon != null)
            menuToggleButtonIcon.sprite = _menuOpen ? closeMenuSprite : openMenuSprite;
        if (xrSimulatorHUD != null) xrSimulatorHUD.SetActive(_displayVisible);
    }


    public void OnToggleMenu()
    {
        SetMenuOpen(!_menuOpen);
    }

    public void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        if (open) SyncSettingControls();
        RefreshUI();
        MenuOpenChanged?.Invoke(_menuOpen);
    }

    public void RunSimulatorSetup()
    {
        if (xrInteractionSimulator != null)
            StartCoroutine(AutoConfigureSimulator());
    }

    public void OnToggleDisplay()
    {
        _displayVisible = !_displayVisible;
        RefreshUI();
        ShowFeedback(_displayVisible ? "Display on" : "Display hidden");
        SetToggleLabel(displayButtonLabel, "Display", _displayVisible);
    }

    public void OnOpenScenarioManager()
    {
#if UNITY_WEBGL
        ShowFeedback("Scenario Manager unavailable in web build");
#else
        // rooted scenarioManagerExePath wins, otherwise simulation/build in the Editor and beside the game .exe standalone
        bool hasAbsoluteOverride = !string.IsNullOrEmpty(scenarioManagerExePath)
            && System.IO.Path.IsPathRooted(scenarioManagerExePath);
        string exePath;
        if (hasAbsoluteOverride)
        {
            exePath = scenarioManagerExePath;
        }
        else if (Application.isEditor)
        {
            exePath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "build", "ScenarioManager.exe"));
        }
        else
        {
            exePath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "ScenarioManager.exe"));
        }

        // WaitForSingleObject on the stored handle, Process.GetProcessesByName is unreliable in Mono standalone builds
        bool alreadyRunning = IsScenarioManagerRunning();
        if (alreadyRunning)
        {
            ShowFeedback("Scenario Manager already open");
            return;
        }

        if (!System.IO.File.Exists(exePath))
        {
            Debug.LogWarning($"[MenuController] Scenario Manager executable not found at: {exePath}");
            // TMP treats the escape sequences in Windows paths as tab and vertical tab
            ShowFeedback($"Not found: {exePath.Replace("\\", "\\\\")}");
            return;
        }

        try
        {
            // Mono's Process.Start silently fails on Windows in Unity builds
            bool launched = WinLaunchDetached(exePath, System.IO.Path.GetDirectoryName(exePath));
            if (launched)
                ShowFeedback("Scenario Manager launching...");
            else
                ShowFeedback($"Launch failed (error {Marshal.GetLastWin32Error()})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MenuController] Failed to launch Scenario Manager: {ex.Message}");
            ShowFeedback($"Launch failed: {ex.Message}");
        }
#endif
    }

    public void OnCalibrateSteering()
    {
        _tiltSteering = ResolveActiveTiltSteering();
        if (_tiltSteering == null)
        {
            Debug.LogWarning("[MenuController] No active TiltSteeringProvider found in scene.");
            ShowFeedback("No active steering controller");
            return;
        }

        if (_tiltSteering.Calibrate())
        {
            ShowFeedback("Steering calibrated");
        }
        else
        {
            Debug.LogWarning($"[MenuController] Steering calibration failed on {_tiltSteering.gameObject.name}: controller input not available.");
            ShowFeedback("Steering calibration failed");
        }
    }

    private TiltSteeringProvider ResolveActiveTiltSteering()
    {
        if (_tiltSteering != null && _tiltSteering.isActiveAndEnabled)
            return _tiltSteering;

        var providers = FindObjectsByType<TiltSteeringProvider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var provider in providers)
        {
            if (provider.isActiveAndEnabled)
                return provider;
        }

        return null;
    }

    public void OnRestartScenario()
    {
        ExchangeData.SendCommand("{\"type\":\"command\",\"command\":\"RESTART_SIMULATION\"}");
        ShowFeedback("Restarting...");

        if (_scenarioManager != null)
            _scenarioManager.RestartScenario();
        else
            Debug.LogWarning("[MenuController] No ScenarioManager found in scene.");
    }

    public void OnToggleInteractionSimulator()
    {
        if (xrInteractionSimulator != null)
        {
            // enabling the simulator removes the real HMD from the Input System and breaks TrackedPoseDriver for the rest of the session
            if (VrActive.IsActive && !xrInteractionSimulator.activeSelf)
            {
                ShowFeedback("Headset connected, simulator unavailable");
                return;
            }

            bool next = !xrInteractionSimulator.activeSelf;
            xrInteractionSimulator.SetActive(next);
            ShowFeedback(next ? "XR Simulator: ON" : "XR Simulator: OFF");
            SetToggleLabel(simulatorButtonLabel, "XR Sim", next);
        }
        else
        {
            Debug.LogWarning("[MenuController] No XR Interaction Simulator assigned.");
        }
    }

    public void OnToggleControllerVisuals()
    {
        // re-cache so the renderers match the current vehicle
        CacheControllerRenderers();

        if (_controllerRenderers.Length == 0)
        {
            ShowFeedback("Controller visuals not found");
            return;
        }

        // derive from the actual renderers, not the flag, so a vehicle switch cannot desync
        bool anyVisible = false;
        foreach (var r in _controllerRenderers)
        {
            if (r.enabled) { anyVisible = true; break; }
        }
        _controllersVisible = !anyVisible;
        SetControllerRenderersVisible(_controllersVisible);
        ShowFeedback(_controllersVisible ? "Controllers: visible" : "Controllers: hidden");
        SetToggleLabel(controllerButtonLabel, "Controllers", _controllersVisible);
    }

    private void OnScenarioChanged(ScenarioId scenario)
    {
        if (_controllerVisibilitySyncCoroutine != null)
            StopCoroutine(_controllerVisibilitySyncCoroutine);
        _controllerVisibilitySyncCoroutine = StartCoroutine(SyncControllerVisibilityAfterScenarioChange());
    }

    private IEnumerator SyncControllerVisibilityAfterScenarioChange()
    {
        yield return null;

        CacheControllerRenderers();
        SetControllerRenderersVisible(_controllersVisible);
        _controllerVisibilitySyncCoroutine = null;
    }

    public void OnToggleAccelerationMode()
    {
        AccelerationSetting.Mode = AccelerationSetting.Mode == AccelerationMode.Instant
            ? AccelerationMode.Gradual
            : AccelerationMode.Instant;
        ShowFeedback($"Acceleration: {AccelerationSetting.Mode}");
        RefreshAccelerationLabel();
    }

    private void RefreshAccelerationLabel()
    {
        if (accelerationButtonLabel != null)
            accelerationButtonLabel.text = $"Acceleration: {AccelerationSetting.Mode}";
    }

    public void OnMaxSpeedChanged(float steps)
    {
        MaxSpeedSetting.Kmh = steps * MaxSpeedSetting.StepKmh;
        RefreshMaxSpeedLabel();
    }

    private void RefreshMaxSpeedLabel()
    {
        if (maxSpeedLabel != null)
            maxSpeedLabel.text = $"Max Speed: {MaxSpeedSetting.Kmh:F0} km/h";
    }

    public void OnCycleVignette()
    {
#if UNITY_WEBGL
        ShowFeedback("Vignette unavailable in web build");
#else
        if (!VrActive.IsActive)
        {
            ShowFeedback("Vignette unavailable without a headset");
            return;
        }

        int levelCount = Enum.GetValues(typeof(VignetteLevel)).Length;
        VignetteSetting.Level = (VignetteLevel)(((int)VignetteSetting.Level + 1) % levelCount);
        ShowFeedback($"Vignette: {VignetteSetting.Level}");
        RefreshVignetteLabel();
#endif
    }

    private void RefreshVignetteLabel()
    {
        if (vignetteButtonLabel != null)
            vignetteButtonLabel.text = $"Vignette: {VignetteSetting.Level}";
    }

    public void OnMasterVolumeChanged(float percent)
    {
        AudioListener.volume = percent / 100f;
        SetPercentLabel(masterVolumeLabel, "Master Volume", percent);
    }

    public void OnWarningVolumeChanged(float percent)
    {
        var evaluator = ResolveDrivingEvaluator();
        if (evaluator != null)
            evaluator.WarningVolume = percent / 100f;
        SetPercentLabel(warningVolumeLabel, "Warning Volume", percent);
    }

    // a scenario restart destroys the evaluator
    private DrivingEvaluator ResolveDrivingEvaluator()
    {
        if (_drivingEvaluator == null)
            _drivingEvaluator = FindFirstObjectByType<DrivingEvaluator>(FindObjectsInactive.Include);
        return _drivingEvaluator;
    }

    private static void SetPercentLabel(TextMeshProUGUI label, string name, float percent)
    {
        if (label != null)
            label.text = $"{name}: {percent:F0}%";
    }

    // without notify, or the sliders push their own values straight back out
    private void SyncSettingControls()
    {
        RefreshAccelerationLabel();
        RefreshVignetteLabel();

        if (maxSpeedSlider != null)
            maxSpeedSlider.SetValueWithoutNotify(MaxSpeedSetting.Kmh / MaxSpeedSetting.StepKmh);
        RefreshMaxSpeedLabel();

        float masterPercent = AudioListener.volume * 100f;
        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(masterPercent);
        SetPercentLabel(masterVolumeLabel, "Master Volume", masterPercent);

        var evaluator = ResolveDrivingEvaluator();
        float warningPercent = (evaluator != null ? evaluator.WarningVolume : 1f) * 100f;
        if (warningVolumeSlider != null)
            warningVolumeSlider.SetValueWithoutNotify(warningPercent);
        SetPercentLabel(warningVolumeLabel, "Warning Volume", warningPercent);
    }

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

    public void ShowFeedback(string message)
    {
        if (feedbackText == null) return;

        if (_feedbackCoroutine != null)
            StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(FeedbackRoutine(message));
    }

    // XRDisplaySubsystem.running beats XRSettings.isDeviceActive, Quest 3 via SteamVR is not active when Start runs
    private IEnumerator AutoConfigureSimulator()
    {
        float timeout = 3f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (VrActive.IsActive)
            {
                xrInteractionSimulator.SetActive(false);
                break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!xrInteractionSimulator.activeSelf)
            xrInteractionSimulator.SetActive(!VrActive.IsActive);

        if (xrSimulatorHUD == null)
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

        if (xrSimulatorHUD != null)
            xrSimulatorHUD.SetActive(xrInteractionSimulator.activeSelf && _displayVisible);

        SetToggleLabel(simulatorButtonLabel, "XR Sim", xrInteractionSimulator.activeSelf);
    }

    // a unified pointer would lock the operator's mouse out, and the screen-space canvas never reaches the headset anyway
    private void SetMouseOwnedMenuPointer()
    {
        var uiModule = FindFirstObjectByType<InputSystemUIInputModule>();
        if (uiModule != null)
            uiModule.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
    }

    private static void SetToggleLabel(TextMeshProUGUI label, string name, bool on)
    {
        if (label != null)
            label.text = $"{name}: {(on ? "ON" : "OFF")}";
    }

    private void CacheControllerRenderers()
    {
        // all active XR Origins, so EgoCar and EgoBike in the same scene both work
        var origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var renderers = new List<Renderer>();
        foreach (var origin in origins)
        {
            Transform offset = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : origin.transform;
            Transform leftHand = offset.Find("Left Hand");
            Transform rightHand = offset.Find("Right Hand");
            if (leftHand != null)
                renderers.AddRange(leftHand.GetComponentsInChildren<Renderer>(true));
            if (rightHand != null)
                renderers.AddRange(rightHand.GetComponentsInChildren<Renderer>(true));
        }
        _controllerRenderers = renderers.ToArray();
    }

    private void SetControllerRenderersVisible(bool visible)
    {
        foreach (var r in _controllerRenderers)
            r.enabled = visible;
    }

    private IEnumerator FeedbackRoutine(string message)
    {
        feedbackText.text = message;

        // unscaled throughout, the main menu holds Time.timeScale at 0
        float holdTime = feedbackDuration * 0.7f;
        float fadeTime = feedbackDuration * 0.3f;

        yield return new WaitForSecondsRealtime(holdTime);

        float elapsed = 0f;
        Color c = feedbackText.color;
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            feedbackText.color = c;
            yield return null;
        }

        feedbackText.text = "";
        c.a = 1f;
        feedbackText.color = c;
        _feedbackCoroutine = null;
    }

    public void OnExit()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

#if !UNITY_WEBGL

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateProcess(
        string lpApplicationName, StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // kept open so IsScenarioManagerRunning can poll it
    private static IntPtr _scenarioManagerHandle = IntPtr.Zero;

    private static bool WinLaunchDetached(string exePath, string workingDir)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var cmdLine = new StringBuilder($"\"{exePath}\"");
        bool ok = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero,
            false, 0, IntPtr.Zero, workingDir, ref si, out var pi);
        if (ok)
        {
            // close the previous handle so a relaunch does not leak it
            if (_scenarioManagerHandle != IntPtr.Zero)
                CloseHandle(_scenarioManagerHandle);
            _scenarioManagerHandle = pi.hProcess;
            CloseHandle(pi.hThread);
        }
        return ok;
    }

    private static bool IsScenarioManagerRunning()
    {
        if (_scenarioManagerHandle == IntPtr.Zero) return false;
        const uint WAIT_TIMEOUT = 0x00000102;
        if (WaitForSingleObject(_scenarioManagerHandle, 0) == WAIT_TIMEOUT)
            return true;

        CloseHandle(_scenarioManagerHandle);
        _scenarioManagerHandle = IntPtr.Zero;
        return false;
    }
#endif
}
