using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// UI shell used to compare the current Godot/C# enemy simulation with an exported MAME trace.
///
/// v0.1.1 keeps the interface authored directly in the .tscn file. This is intentional:
/// if the C# assembly is not built yet, the window is still visible instead of looking empty.
/// </summary>
public partial class EnemyTraceSimulatorWindow : Control
{
    private const double PlaybackTickSeconds = 1.0 / 60.0;

    private LineEdit? _luaScriptPath;
    private LineEdit? _tracePath;
    private EnemyTraceBoardView? _simulationBoard;
    private EnemyTraceBoardView? _mameBoard;
    private TextEdit? _console;
    private Label? _statusLabel;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private List<EnemyTraceFrame> _frames = new();
    private int _currentFrameIndex;
    private bool _isRunning;
    private bool _isPaused = true;
    private double _playbackAccumulator;

    public override void _Ready()
    {
        BindInterface();
        ConnectButtons();
        LoadDefaultMazeInBoards();

        Log("Enemy trace simulator UI ready.");
        Log("v0.1.1: the UI is now scene-authored, so it remains visible even before the C# assembly is rebuilt.");
        Log("Load the sample trace, then use simulation / pause / tick controls.");
    }

    public override void _Process(double delta)
    {
        if (!_isRunning || _isPaused || _frames.Count == 0)
            return;

        _playbackAccumulator += delta;

        while (_playbackAccumulator >= PlaybackTickSeconds)
        {
            _playbackAccumulator -= PlaybackTickSeconds;
            StepOneFrame();
        }
    }

    private void BindInterface()
    {
        _luaScriptPath = GetNodeOrNull<LineEdit>("Root/MainLayout/LuaPathRow/LuaScriptPath");
        _tracePath = GetNodeOrNull<LineEdit>("Root/MainLayout/TracePathRow/TracePath");
        _simulationBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/SimulationBoard");
        _mameBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/MameTraceBoard");
        _console = GetNodeOrNull<TextEdit>("Root/MainLayout/Console");
        _statusLabel = GetNodeOrNull<Label>("Root/MainLayout/PlaybackControls/StatusLabel");

        if (_simulationBoard != null)
            _simulationBoard.BoardTitle = "Simulation C# / Godot";

        if (_mameBoard != null)
            _mameBoard.BoardTitle = "Trace MAME";
    }

    private void ConnectButtons()
    {
        ConnectButton("Root/MainLayout/LuaPathRow/LaunchMameLuaButton", OnLaunchMameLuaPressed);
        ConnectButton("Root/MainLayout/TracePathRow/LoadTraceButton", OnLoadTracePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/RunSimulationButton", OnRunSimulationPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/PauseResumeButton", OnPauseResumePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/StepButton", OnStepPressed);
    }

    private void ConnectButton(string path, Action handler)
    {
        Button? button = GetNodeOrNull<Button>(path);
        if (button == null)
        {
            GD.PushWarning($"EnemyTraceSimulator button not found: {path}");
            return;
        }

        button.Pressed += handler;
    }

    private void LoadDefaultMazeInBoards()
    {
        _simulationBoard?.LoadMazeFromResource("res://data/maze.json");
        _mameBoard?.LoadMazeFromResource("res://data/maze.json");
    }

    private void OnLaunchMameLuaPressed()
    {
        Log("MAME/Lua launch requested.");
        Log("Stub only for v0.1.1: the next package should add a configurable external MAME command and process execution.");
        Log($"Lua script path: {_luaScriptPath?.Text ?? string.Empty}");
    }

    private void OnLoadTracePressed()
    {
        string path = _tracePath?.Text.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            Log("No trace path specified.");
            return;
        }

        if (!FileAccess.FileExists(path))
        {
            Log($"Trace file not found: {path}");
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Log($"Could not open trace file: {path}");
            return;
        }

        EnemyTraceFile? trace;

        try
        {
            string json = file.GetAsText();
            trace = JsonSerializer.Deserialize<EnemyTraceFile>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log($"Trace JSON parse error: {ex.Message}");
            return;
        }

        _frames = trace?.frames ?? new List<EnemyTraceFrame>();
        _currentFrameIndex = 0;
        _isRunning = false;
        _isPaused = true;
        _playbackAccumulator = 0;

        if (_frames.Count == 0)
        {
            Log($"Trace loaded but contains no frame: {path}");
            UpdateStatus();
            return;
        }

        ApplyCurrentFrame();
        Log($"Trace loaded: {path}");
        Log($"Frames: {_frames.Count}");
        Log($"Initial gate states present: {trace?.initial_state?.gates?.Count ?? 0}");
        UpdateStatus();
    }

    private void OnRunSimulationPressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded. Load a trace before starting playback.");
            return;
        }

        _isRunning = true;
        _isPaused = false;
        Log("Playback started.");
        UpdateStatus();
    }

    private void OnPauseResumePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        _isPaused = !_isPaused;
        _isRunning = true;
        Log(_isPaused ? "Playback paused." : "Playback resumed.");
        UpdateStatus();
    }

    private void OnStepPressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        _isRunning = true;
        _isPaused = true;
        StepOneFrame();
    }

    private void StepOneFrame()
    {
        if (_frames.Count == 0)
            return;

        _currentFrameIndex++;

        if (_currentFrameIndex >= _frames.Count)
        {
            _currentFrameIndex = _frames.Count - 1;
            _isRunning = false;
            _isPaused = true;
            Log("End of trace reached.");
        }

        ApplyCurrentFrame();
        UpdateStatus();
    }

    private void ApplyCurrentFrame()
    {
        if (_frames.Count == 0)
            return;

        EnemyTraceFrame frame = _frames[_currentFrameIndex];

        // v0.1.1 still shows the same loaded MAME frame on both sides.
        // The left side becomes the output of the C# simulation in the next package.
        _simulationBoard?.SetSnapshot(frame);
        _mameBoard?.SetSnapshot(frame);
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null)
            return;

        if (_frames.Count == 0)
        {
            _statusLabel.Text = "Aucune trace chargée.";
            return;
        }

        EnemyTraceFrame frame = _frames[_currentFrameIndex];
        string state = _isRunning && !_isPaused ? "lecture" : "pause";
        _statusLabel.Text = $"Frame {_currentFrameIndex + 1}/{_frames.Count} | arcade frame={frame.frame} | {state}";
    }

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        GD.Print(line);

        if (_console == null)
            return;

        _console.Text += line + "\n";
        _console.ScrollVertical = _console.GetLineCount();
    }
}

public sealed class EnemyTraceFile
{
    public EnemyTraceMeta? meta { get; set; }
    public EnemyTraceInitialState? initial_state { get; set; }
    public List<EnemyTraceFrame>? frames { get; set; }
}

public sealed class EnemyTraceMeta
{
    public string? game { get; set; }
    public string? source { get; set; }
    public int tick_rate { get; set; }
    public string? notes { get; set; }
}

public sealed class EnemyTraceInitialState
{
    public int frame { get; set; }
    public List<EnemyTraceGateState>? gates { get; set; }
}

public sealed class EnemyTraceFrame
{
    public int frame { get; set; }
    public EnemyTraceActor? player { get; set; }
    public List<EnemyTraceActor>? enemies { get; set; }
    public List<EnemyTraceGateState>? gates { get; set; }
}

public sealed class EnemyTraceActor
{
    public int slot { get; set; }
    public int x { get; set; }
    public int y { get; set; }
    public string? dir { get; set; }
    public bool active { get; set; } = true;
}

public sealed class EnemyTraceGateState
{
    public int gate_id { get; set; }
    public string? orientation { get; set; }
}
