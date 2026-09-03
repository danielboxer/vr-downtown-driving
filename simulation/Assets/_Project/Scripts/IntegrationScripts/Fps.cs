using TMPro;
using UnityEngine;

/// <summary>
/// </summary>
public class Fps : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────  FPS fields
    private float currentFps;
    private float smoothedFps;
    private float smoothingFactor = 0.1f;       // weight of recent frames

    // ───────────────────────────────────────────────────────────────  GUI fields
    private float displayedFps;
    private const float guiUpdateInterval = 0.5f;
    private float guiTimer;

    [Header("UI")]
    [Tooltip("TMP Text element that displays the FPS counter.")]
    [SerializeField] private TextMeshProUGUI fpsText;

    private void OnEnable()
    {
        if (fpsText != null)
            fpsText.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (fpsText != null)
            fpsText.gameObject.SetActive(false);
    }

    // ────────────────────────────────────────────────────────────────────────────
    private void Start()
    {
        displayedFps = 0f;           // avoid showing 0 initially
    }

    // ────────────────────────────────────────────────────────────────────────────
    private void Update()
    {
        currentFps = 1f / Time.unscaledDeltaTime;
        smoothedFps = (smoothingFactor * currentFps) + ((1f - smoothingFactor) * smoothedFps);

        // Slow the GUI refresh rate a little
        guiTimer += Time.deltaTime;
        if (guiTimer >= guiUpdateInterval)
        {
            displayedFps = smoothedFps;
            guiTimer = 0f;

            if (fpsText != null)
            {
                fpsText.text = "FPS: " + Mathf.Round(displayedFps);
            }
        }
    }
}
