using UnityEngine;
using UnityEngine.UI; // Required for working with Unity UI Text
using UnityStandardAssets.Vehicles.Car;


public class Speedometer : MonoBehaviour
{
    public GameObject TrafficObject;           // Assign via Inspector.
    public float updateInterval = 0.1f;          // Interval in seconds at which to update speed.

    [Tooltip("Optional: assign CarUserControl to show 'R' instead of speed when in reverse.")]
    public CarUserControl carUserControl;

    private Rigidbody rb;
    private Text m_text;
    private float timeSinceLastUpdate = 0f;
    private float m_Speed = 0f;

    [Tooltip("Seconds per flash half-cycle for the turn signal arrow. Lower = faster. Match to your blinker sound interval.")]
    public float signalFlashInterval = 0.5f;

    // Flash state for turn signal display
    private float _signalFlashTimer = 0f;
    private bool _signalFlashVisible = false;

    void Start()
    {
        rb = TrafficObject.GetComponent<Rigidbody>();
        m_text = GetComponentInChildren<Text>();

        if (m_text == null)
        {
            Debug.LogWarning("Text component not found in children.");
        }
    }

    void Update()
    {
        // Accumulate time
        timeSinceLastUpdate += Time.deltaTime;

        // Advance the turn-signal flash timer every frame for accurate timing
        _signalFlashTimer += Time.deltaTime;
        if (_signalFlashTimer >= signalFlashInterval)
        {
            _signalFlashVisible = !_signalFlashVisible;
            _signalFlashTimer -= signalFlashInterval;
        }

        // Check if it's time to update the speed
        if (timeSinceLastUpdate >= updateInterval)
        {
            // Calculate speed in km/h (example: velocity.magnitude is m/s, multiply by 3.6 to get km/h)
            m_Speed = Mathf.Round(rb.linearVelocity.magnitude * 3.6f);

            // Update the text if reference is available
            if (m_text != null)
            {
                bool isReverse = carUserControl != null && carUserControl.IsReverse;
                bool isLeft = carUserControl != null && carUserControl.IsLeftSignalOn;
                bool isRight = carUserControl != null && carUserControl.IsRightSignalOn;

                if (isReverse || isLeft || isRight)
                {
                    // Build display: "< R" / "R >" / "R" / "<" / ">" — signal arrows flash, R stays solid
                    string arrow = "";
                    if (isLeft)
                        arrow = _signalFlashVisible ? "<" : " ";
                    else if (isRight)
                        arrow = _signalFlashVisible ? ">" : " ";

                    if (isReverse && (isLeft || isRight))
                        m_text.text = isLeft ? $"{arrow} R" : $"R {arrow}";
                    else if (isReverse)
                        m_text.text = "R";
                    else
                        m_text.text = arrow.Trim();
                }
                else
                {
                    // Format to always show two digits, e.g. "05", "10"
                    m_text.text = string.Format("{0:00} km/h", m_Speed);
                }
            }

            // Reset the timer
            timeSinceLastUpdate = 0f;
        }
    }
}
