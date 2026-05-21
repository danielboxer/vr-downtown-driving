using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
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
    [Tooltip("Absolute path override for ScenarioManager.exe. Leave empty to use automatic defaults: simulation/build/ScenarioManager.exe in the Editor, ScenarioManager.exe beside the game .exe in a build.")]
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
    private ScenarioLabel _scenarioLabel;
    private Process _scenarioManagerProcess;

    // whether the HUD elements (FPS counter + toggle button) are shown
    private bool _displayVisible = true;
    // whether the menu panel is currently open
    private bool _menuOpen = false;
    // whether the physical XR controller visuals are shown
    private bool _controllersVisible = false;
    // cached renderers on the Left Hand and Right Hand XR controller visual prefabs
    private Renderer[] _controllerRenderers = System.Array.Empty<Renderer>();

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
        _scenarioLabel = FindFirstObjectByType<ScenarioLabel>();

        // Cache renderers on the XR controller visual prefabs so they can be
        // hidden during the study without disabling the TrackedPoseDrivers.
        var xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null)
        {
            Transform offset = xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : xrOrigin.transform;
            var renderers = new List<Renderer>();
            Transform leftHand = offset.Find("Left Hand");
            Transform rightHand = offset.Find("Right Hand");
            if (leftHand != null)
                renderers.AddRange(leftHand.GetComponentsInChildren<Renderer>(true));
            if (rightHand != null)
                renderers.AddRange(rightHand.GetComponentsInChildren<Renderer>(true));
            _controllerRenderers = renderers.ToArray();
        }

        // Apply the default hidden state to all cached controller renderers.
        foreach (var r in _controllerRenderers)
            r.enabled = _controllersVisible;

        // Auto-enable the interaction simulator when no real XR device is running.
        // Use a coroutine so we can wait for the XR display subsystem to finish
        // initializing (some headsets, e.g. Quest 3 via SteamVR, are not yet active
        // when Start() runs, which would incorrectly enable the simulator).
        if (xrInteractionSimulator != null)
            StartCoroutine(AutoConfigureSimulator());

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
        if (_scenarioLabel != null) _scenarioLabel.enabled = _displayVisible;
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
        // Absolute Inspector override takes priority.
        // In the Editor, default to simulation/build/ScenarioManager.exe.
        // In a standalone build, default to ScenarioManager.exe beside the game .exe.
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

        // Don't open a second instance if one is already running.
        // Uses WaitForSingleObject on the stored handle — Process.GetProcessesByName is
        // also unreliable in Mono Unity standalone builds.
        bool alreadyRunning = IsScenarioManagerRunning();
        if (alreadyRunning)
        {
            ShowFeedback("Scenario Manager already open");
            return;
        }

        if (!System.IO.File.Exists(exePath))
        {
            Debug.LogWarning($"[MenuController] Scenario Manager executable not found at: {exePath}");
            // Escape backslashes before passing to TMP: \t and \v in Windows paths
            // are interpreted as tab/vertical-tab escape sequences by TMP's text parser.
            ShowFeedback($"Not found: {exePath.Replace("\\", "\\\\")}");
            return;
        }

        try
        {
            // Use P/Invoke CreateProcess directly — Mono's Process.Start is broken on
            // Windows in Unity builds and silently fails even for simple executables.
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
            // Block enabling the simulator while a real XR headset is active. The
            // simulator removes the real HMD from the Input System on enable, which
            // breaks TrackedPoseDriver head tracking for the rest of the play session
            // even after the simulator is turned off again.
            bool hasActiveDisplay = false;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                if (d.running) { hasActiveDisplay = true; break; }
            }

            if (hasActiveDisplay && !xrInteractionSimulator.activeSelf)
            {
                ShowFeedback("Headset connected, simulator unavailable");
                return;
            }

            bool next = !xrInteractionSimulator.activeSelf;
            xrInteractionSimulator.SetActive(next);
            ShowFeedback(next ? "XR Simulator: ON" : "XR Simulator: OFF");
        }
        else
        {
            Debug.LogWarning("[MenuController] No XR Interaction Simulator assigned.");
        }
    }

    /// <summary>Hides or shows the XR controller visual models (Left Hand / Right Hand renderers) without affecting tracking.</summary>
    public void OnToggleControllerVisuals()
    {
        _controllersVisible = !_controllersVisible;
        foreach (var r in _controllerRenderers)
            r.enabled = _controllersVisible;
        ShowFeedback(_controllersVisible ? "Controllers: visible" : "Controllers: hidden");
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

    // Waits up to 3 seconds for the XR display subsystem to start running, then
    // enables the simulator only if no real XR display is active. Using
    // XRDisplaySubsystem.running is more reliable than XRSettings.isDeviceActive
    // because some headsets (e.g. Quest 3 via SteamVR) are not yet active at
    // the time Start() executes.
    private IEnumerator AutoConfigureSimulator()
    {
        var displays = new List<XRDisplaySubsystem>();
        float timeout = 3f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            SubsystemManager.GetSubsystems(displays);
            bool hasRunningDisplay = false;
            foreach (var d in displays)
            {
                if (d.running) { hasRunningDisplay = true; break; }
            }

            if (hasRunningDisplay)
            {
                // Real headset confirmed active: keep simulator disabled.
                xrInteractionSimulator.SetActive(false);
                break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // If we timed out with no running display, enable the simulator.
        if (!xrInteractionSimulator.activeSelf)
        {
            var displays2 = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays2);
            bool hasRunningDisplay = false;
            foreach (var d in displays2)
            {
                if (d.running) { hasRunningDisplay = true; break; }
            }
            xrInteractionSimulator.SetActive(!hasRunningDisplay);
        }

        // Auto-find the simulator HUD now that the prefab Awake has run.
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

        // Sync HUD visibility with the current display state.
        if (xrSimulatorHUD != null)
            xrSimulatorHUD.SetActive(xrInteractionSimulator.activeSelf && _displayVisible);
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

    // ── Win32 P/Invoke — bypasses Mono's broken Process.Start on Windows builds ──

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

    // WAIT_TIMEOUT means the process is still running.
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // Keep hProcess open so IsScenarioManagerRunning can poll it without using
    // Mono's Process.GetProcessesByName (also unreliable in Unity standalone builds).
    private static IntPtr _scenarioManagerHandle = IntPtr.Zero;

    /// <summary>Launches <paramref name="exePath"/> as a detached process via Win32 CreateProcess.</summary>
    private static bool WinLaunchDetached(string exePath, string workingDir)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var cmdLine = new StringBuilder($"\"{exePath}\"");
        bool ok = CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero,
            false, 0, IntPtr.Zero, workingDir, ref si, out var pi);
        if (ok)
        {
            // Keep hProcess open so we can check if it's still running.
            _scenarioManagerHandle = pi.hProcess;
            CloseHandle(pi.hThread);
        }
        return ok;
    }

    /// <summary>Returns true if the last launched ScenarioManager process is still running.</summary>
    private static bool IsScenarioManagerRunning()
    {
        if (_scenarioManagerHandle == IntPtr.Zero) return false;
        const uint WAIT_TIMEOUT = 0x00000102;
        return WaitForSingleObject(_scenarioManagerHandle, 0) == WAIT_TIMEOUT;
    }
}

