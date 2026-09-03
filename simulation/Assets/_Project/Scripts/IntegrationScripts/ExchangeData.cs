using System.Collections.Concurrent;
using UnityEngine;
#if !UNITY_WEBGL
using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using System.Threading;
using System;
using Stopwatch = System.Diagnostics.Stopwatch;
#endif

[System.Serializable]
public class CommonMessage
{
    public string type;    // "command" or "vehicles"
    public string command; // Used if type == "command"
}

public static class RecordingManager
{
    public static bool startRecordingFromZero = false;
    public static float recordingStartTime = 0f;
}

public class ExchangeData : MonoBehaviour
{
#if !UNITY_WEBGL
    private SimulationController _SimulationController;
#endif

    // drained each loop by the background thread, enqueue via SendCommand
    private static readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();

    public static void SendCommand(string commandJson)
    {
        _commandQueue.Enqueue(commandJson);
    }

#if !UNITY_WEBGL
    // Thread for background communication
    private Thread _communicationThread;
    private bool _isRunning = false;

    // sending ego data every 1 ms is wasted work against a 0.1 s SUMO step
    private double _nextVehicleSendTime;
    private double _sendInterval;
    private static readonly double StopwatchToSeconds = 1.0 / Stopwatch.Frequency;
#endif


    public void Start()
    {
#if !UNITY_WEBGL
        _SimulationController = GetComponent<SimulationController>();

        float step = Mathf.Max(0.02f, _SimulationController.unityStepLength * 0.5f);
        _sendInterval = Mathf.Min(step, 0.1f);

        // Start the communication thread
        _isRunning = true;
        _communicationThread = new Thread(Run);
        _communicationThread.Start();
#endif
    }

#if !UNITY_WEBGL
    void OnDestroy()
    {
        // NetMQConfig.Cleanup() runs in the thread's finally block
        _isRunning = false;
        if (_communicationThread != null && _communicationThread.IsAlive)
        {
            _communicationThread.Join();
        }
    }

    private void Run()
    {
        ForceDotNet.Force();

        try
        {
            using (var subSocket = new SubscriberSocket())
            using (var dealerSocket = new DealerSocket())
            {
                // Connect to SUMO's PUB socket
                subSocket.Connect("tcp://localhost:5556");
                subSocket.Subscribe("");
                subSocket.Options.ReceiveHighWatermark = 1000;

                // Connect to SUMO's ROUTER socket
                dealerSocket.Connect("tcp://localhost:5557");
                dealerSocket.Options.SendHighWatermark = 1000;

                while (_isRunning)
                {
                    try
                    {
                        double nowSeconds = Stopwatch.GetTimestamp() * StopwatchToSeconds;

                        // --- Send Data to SUMO ---
                        if (_SimulationController != null && nowSeconds >= _nextVehicleSendTime)
                        {
                            string vehicleDataJson = _SimulationController.GetVehicleDataJson();

                            // TrySendFrame fails while SUMO is not running yet (HWM full)
                            dealerSocket.TrySendFrame(vehicleDataJson);

                            _nextVehicleSendTime = nowSeconds + _sendInterval;
                        }

                        // queued commands go out immediately, e.g. RESTART_SIMULATION from a keypress
                        while (_commandQueue.TryDequeue(out string commandJson))
                            dealerSocket.TrySendFrame(commandJson);

                        // --- Receive Data from SUMO ---
                        string sumoDataJson;
                        bool gotMessage = subSocket.TryReceiveFrameString(out sumoDataJson);

                        if (gotMessage)
                        {

                            // Enqueue the message to be handled on the main thread
                            _SimulationController.EnqueueOnMainThread(sumoDataJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in background thread loop: {ex.Message}\n{ex.StackTrace}");
                        _isRunning = false;
                        break;
                    }

                    // Sleep briefly to prevent 100% CPU usage
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in background thread: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            NetMQConfig.Cleanup();
            Debug.Log("ExchangeData thread terminated gracefully.");
        }
    }
#endif
}
