using UnityEngine;

/// <summary>
///   FPS on-screen display.
/// </summary>
public class Fps : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────  FPS fields
    private float currentFps;
    private float smoothedFps;
    private float smoothingFactor = 0.1f;       // weight of recent frames
    [SerializeField] private int fontSize = 25;          // GUI font size

    // ───────────────────────────────────────────────────────────────  GUI fields
    private float latestSmoothedFps;
    private float displayedFps;
    private const float guiUpdateInterval = 0.5f;
    private float guiTimer;

    // ────────────────────────────────────────────────────────────  references
    private ExchangeData _ExchangeData;

    // ────────────────────────────────────────────────────────────────────────────
    private void Start()
    {
        // --------------------------------------------------------  file location
        //string sumoDataDir = LocateOrCreateResultsFolder();
        //filePath = Path.Combine(sumoDataDir, "FPS_Report.txt");
        //fpsWriter = new StreamWriter(filePath, append: false);
        //fpsWriter.WriteLine("unity_time;FPS");

        // --------------------------------------------------------  other setup
        _ExchangeData = GetComponent<ExchangeData>() ?? gameObject.AddComponent<ExchangeData>();

        displayedFps = 0f;           // avoid showing 0 initially
        GUI.depth = 2;
    }

    // ────────────────────────────────────────────────────────────────────────────
    private void Update()
    {
        currentFps = 1f / Time.unscaledDeltaTime;
        smoothedFps = (smoothingFactor * currentFps) + ((1f - smoothingFactor) * smoothedFps);

        latestSmoothedFps = smoothedFps;

        // Slow the GUI refresh rate a little
        guiTimer += Time.deltaTime;
        if (guiTimer >= guiUpdateInterval)
        {
            displayedFps = smoothedFps;
            guiTimer = 0f;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUIStyle style = new GUIStyle
        {
            fontSize = fontSize,
            normal = { textColor = Color.white }
        };

        GUI.Label(new Rect(5, 5, 200, 25), "FPS: " + Mathf.Round(displayedFps), style);
    }
}
