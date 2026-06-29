using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Plays a baked SUMO recording instead of a live SUMO/ZMQ link. The WebGL build
/// can't run SUMO, so traffic is pre-recorded by python/record_scenario.py and
/// replayed here by feeding each record into SimulationController.HandleMessage,
/// the same consumer the live build uses. Auto-added on WebGL by SimulationController.
///
/// Recordings live in StreamingAssets/Recordings/&lt;scenario&gt;.jsonl.gz, one JSON
/// message per line with a leading "t" (seconds from 0).
/// </summary>
[RequireComponent(typeof(SimulationController))]
public class SimulationReplayer : MonoBehaviour
{
    [Tooltip("Replay in the Editor for testing without SUMO. Ignored in real builds: WebGL always replays, desktop never does.")]
    public bool enableInEditor = false;
    [Tooltip("Scenario to switch to when testing replay in the Editor.")]
    public ScenarioId editorScenario = ScenarioId.downtown_car;

    private SimulationController _sim;
    private ScenarioManager _scenarioManager;

    private struct Record { public float t; public string json; }
    private readonly List<Record> _records = new List<Record>();
    private int _cursor;      // next record to play
    private float _clock;     // playback seconds
    private bool _loaded;
    private bool _loading;
    private bool _looping;    // true during the fade/reset at the loop seam
    private ScenarioId _scenario;

    private void Start()
    {
        if (!ShouldReplay())
        {
            enabled = false;
            return;
        }

        _sim = GetComponent<SimulationController>();
        _scenarioManager = GetComponent<ScenarioManager>();

        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged += OnScenarioChanged;

#if UNITY_EDITOR
        // The scene boots the desktop default scenario; switch to the one under test
        // so its ego and recording load. Fires OnScenarioChanged, handled below.
        if (_scenarioManager != null && _scenarioManager.ActiveScenario != editorScenario)
            _scenarioManager.ApplyScenario(editorScenario.ToString());
#endif

        // Cover the case where the scenario was already applied before we subscribed.
        if (_scenarioManager != null)
            BeginLoad(_scenarioManager.ActiveScenario);
    }

    private void OnDestroy()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioChanged -= OnScenarioChanged;
    }

    private void OnScenarioChanged(ScenarioId scenario)
    {
        BeginLoad(scenario);
    }

    private void Update()
    {
        if (!_loaded || _looping)
            return;

        _clock += Time.deltaTime;

        while (_cursor < _records.Count && _records[_cursor].t <= _clock)
        {
            _sim.HandleMessage(_records[_cursor].json);
            _cursor++;
        }

        if (_cursor >= _records.Count)
            StartCoroutine(LoopRestart());
    }

    private bool ShouldReplay()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return true;
#elif UNITY_EDITOR
        return enableInEditor;
#else
        return false;
#endif
    }

    private void BeginLoad(ScenarioId scenario)
    {
        if (scenario == _scenario && (_loading || _loaded))
            return; // already loading or playing this scenario

        StopAllCoroutines();
        _records.Clear();
        _cursor = 0;
        _clock = 0f;
        _loaded = false;
        _loading = true;
        _looping = false;
        _scenario = scenario;
        StartCoroutine(LoadRoutine(scenario));
    }

    private IEnumerator LoadRoutine(ScenarioId scenario)
    {
        string url = Application.streamingAssetsPath + "/Recordings/" + scenario + ".jsonl.gz";
        byte[] gz;

#if UNITY_WEBGL && !UNITY_EDITOR
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SimulationReplayer] No recording for {scenario} ({req.error})");
                _loading = false;
                yield break;
            }
            gz = req.downloadHandler.data;
        }
#else
        if (!File.Exists(url))
        {
            Debug.LogWarning($"[SimulationReplayer] No recording for {scenario} at {url}");
            _loading = false;
            yield break;
        }
        gz = File.ReadAllBytes(url);
        yield return null;
#endif

        ParseRecords(gz);
        _cursor = 0;
        _clock = 0f;
        _loading = false;
        _loaded = true;
        Debug.Log($"[SimulationReplayer] Loaded {_records.Count} records for {scenario}");
    }

    private void ParseRecords(byte[] gzData)
    {
        _records.Clear();
        using (var input = new MemoryStream(gzData))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip, Encoding.UTF8))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                    continue;
                // Skip the config record: this component already owns scenario selection,
                // and feeding it back through HandleMessage would re-enter ApplyScenario.
                if (line.IndexOf("\"type\":\"config\"", System.StringComparison.Ordinal) >= 0)
                    continue;
                _records.Add(new Record { t = ExtractT(line), json = line });
            }
        }
    }

    // Reads the number in the leading {"t":<number>, ... without a full JSON parse.
    private static float ExtractT(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return 0f;
        int start = colon + 1;
        int end = start;
        while (end < line.Length && line[end] != ',' && line[end] != '}')
            end++;
        return float.TryParse(line.Substring(start, end - start),
            NumberStyles.Float, CultureInfo.InvariantCulture, out float t) ? t : 0f;
    }

    // Fade to black, clear traffic, rewind, fade back in. The fade hides the seam
    // where every vehicle would otherwise jump back to its start position.
    private IEnumerator LoopRestart()
    {
        _looping = true;

        if (_scenarioManager != null)
        {
            bool reset = false;
            _scenarioManager.FadeThrough(() =>
            {
                _sim.ClearAllNpcVehicles();
                _cursor = 0;
                _clock = 0f;
                reset = true;
            });
            while (!reset)
                yield return null;
        }
        else
        {
            _sim.ClearAllNpcVehicles();
            _cursor = 0;
            _clock = 0f;
        }

        _looping = false;
    }
}
