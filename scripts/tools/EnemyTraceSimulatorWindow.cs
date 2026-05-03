using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// UI shell used to compare the current Godot/C# enemy simulation with an exported MAME trace.
///
/// v0.2.1 keeps the MAME launcher from v0.2.0 and now parses / displays pivoting gate states
/// from the trace on both debug boards.
/// </summary>
public partial class EnemyTraceSimulatorWindow : Control
{
    private const double PlaybackTickSeconds = 1.0 / 60.0;
    private static readonly Vector2 PlaybackButtonSize = new(58, 38);
    private const int PlaybackButtonFontSize = 23;

    private const string RestartButtonText = "↺";
    private const string ResumeButtonText = "▶";
    private const string PauseButtonText = "❚❚";
    private const string StepButtonText = "▶|";
    private const string DefaultMameConfigPath = "res://config/mame_trace_settings.json";
    private const string DefaultTracePath = "res://traces/mame/ladybug_sequence_v8_trace.jsonl";

    private string _currentTracePath = DefaultTracePath;
    private EnemyTraceBoardView? _simulationBoard;
    private EnemyTraceBoardView? _mameBoard;
    private TextEdit? _console;
    private Label? _statusLabel;
    private Button? _launchMameLuaButton;
    private Button? _runSimulationButton;
    private Button? _pauseResumeButton;
    private Button? _stepButton;

    private static readonly JsonDocumentOptions TraceJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private List<EnemyTraceFrame> _frames = new();
    private int _currentFrameIndex;
    private bool _isRunning;
    private bool _isPaused = true;
    private bool _isLaunchingMame;
    private double _playbackAccumulator;

    public override void _Ready()
    {
        BindInterface();
        ConnectButtons();
        LoadDefaultMazeInBoards();

        Log("Enemy trace simulator UI ready.");
        Log("v0.2.16: clean view by default. Use Ctrl+D to toggle player debug markers; Ctrl+arrows adjust sprite offset.");
        Log($"MAME config: {DefaultMameConfigPath}");
        Log($"Trace par défaut: {DefaultTracePath}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        if (!keyEvent.CtrlPressed)
            return;

        if (keyEvent.Keycode == Key.D)
        {
            TogglePlayerDebugMarkers();
            GetViewport().SetInputAsHandled();
            return;
        }

        Vector2 delta = keyEvent.Keycode switch
        {
            Key.Left => new Vector2(-1.0f, 0.0f),
            Key.Right => new Vector2(1.0f, 0.0f),
            Key.Up => new Vector2(0.0f, -1.0f),
            Key.Down => new Vector2(0.0f, 1.0f),
            _ => Vector2.Zero
        };

        if (delta != Vector2.Zero)
        {
            NudgePlayerSpriteOffset(delta);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode == Key.Home)
        {
            ResetPlayerSpriteOffset();
            GetViewport().SetInputAsHandled();
        }
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
        // The toolbar intentionally does not expose path fields anymore.
        // MAME uses DefaultMameConfigPath, and the trace loader uses either
        // the last generated trace path or DefaultTracePath.
        _simulationBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/SimulationBoard");
        _mameBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/MameTraceBoard");
        _console = GetNodeOrNull<TextEdit>("Root/MainLayout/Console");
        _statusLabel = GetNodeOrNull<Label>("Root/MainLayout/PlaybackControls/StatusLabel");
        _launchMameLuaButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/LaunchMameLuaButton");
        _runSimulationButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/RunSimulationButton");
        _pauseResumeButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/PauseResumeButton");
        _stepButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/StepButton");

        if (_simulationBoard != null)
            _simulationBoard.BoardTitle = "Simulation C# / Godot";

        if (_mameBoard != null)
            _mameBoard.BoardTitle = "Trace MAME";

        ConfigurePlaybackButtons();
    }

    private void ConfigurePlaybackButtons()
    {
        ConfigurePlaybackButton(
            _runSimulationButton,
            RestartButtonText,
            "Relancer la simulation depuis le début");

        ConfigurePlaybackButton(
            _pauseResumeButton,
            ResumeButtonText,
            "Mettre en pause ou reprendre la simulation");

        ConfigurePlaybackButton(
            _stepButton,
            StepButtonText,
            "Avancer d’un tick");

        UpdatePauseResumeButtonText();
    }

    private static void ConfigurePlaybackButton(Button? button, string text, string tooltipText)
    {
        if (button == null)
            return;

        button.Text = text;
        button.TooltipText = tooltipText;
        button.CustomMinimumSize = PlaybackButtonSize;
        button.AddThemeFontSizeOverride("font_size", PlaybackButtonFontSize);
    }

    private void UpdatePauseResumeButtonText()
    {
        if (_pauseResumeButton == null)
            return;

        _pauseResumeButton.Text = _isRunning && !_isPaused ? PauseButtonText : ResumeButtonText;
    }

    private void ConnectButtons()
    {
        ConnectButton("Root/MainLayout/PlaybackControls/LaunchMameLuaButton", OnLaunchMameLuaPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/LoadTraceButton", OnLoadTracePressed);
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

    private void NudgePlayerSpriteOffset(Vector2 deltaArcade)
    {
        _simulationBoard?.NudgePlayerSpriteVisualOffsetArcade(deltaArcade);
        _mameBoard?.NudgePlayerSpriteVisualOffsetArcade(deltaArcade);

        Vector2 offset = _mameBoard?.PlayerSpriteVisualOffsetArcade
                         ?? _simulationBoard?.PlayerSpriteVisualOffsetArcade
                         ?? Vector2.Zero;
        Log($"Player sprite visual offset arcade = ({offset.X:0}, {offset.Y:0})");
    }

    private void ResetPlayerSpriteOffset()
    {
        _simulationBoard?.ResetPlayerSpriteVisualOffsetArcade();
        _mameBoard?.ResetPlayerSpriteVisualOffsetArcade();

        Vector2 offset = _mameBoard?.PlayerSpriteVisualOffsetArcade
                         ?? _simulationBoard?.PlayerSpriteVisualOffsetArcade
                         ?? Vector2.Zero;
        Log($"Player sprite visual offset arcade reset = ({offset.X:0}, {offset.Y:0})");
    }

    private void TogglePlayerDebugMarkers()
    {
        bool show = !(_mameBoard?.ShowPlayerDebugMarkers
                      ?? _simulationBoard?.ShowPlayerDebugMarkers
                      ?? false);

        _simulationBoard?.SetShowPlayerDebugMarkers(show);
        _mameBoard?.SetShowPlayerDebugMarkers(show);

        Log(show
            ? "Player debug markers visible. Cyan P = raw x/y, yellow T = turn target."
            : "Player debug markers hidden.");
    }

    private async void OnLaunchMameLuaPressed()
    {
        if (_isLaunchingMame)
        {
            Log("MAME launch already in progress.");
            return;
        }

        string configPath = DefaultMameConfigPath;

        _isLaunchingMame = true;
        if (_launchMameLuaButton != null)
            _launchMameLuaButton.Disabled = true;

        Log($"Launching MAME from config: {configPath}");

        try
        {
            MameTraceLaunchResult result = await MameTraceLauncher.LaunchAsync(configPath);

            foreach (string message in result.Messages)
                Log(message);

            if (!string.IsNullOrWhiteSpace(result.TracePath))
            {
                _currentTracePath = MameTraceLauncher.ToDisplayPath(result.TracePath);
                Log($"Trace générée prête à charger: {_currentTracePath}");
            }
        }
        catch (Exception ex)
        {
            Log($"MAME launch failed: {ex.Message}");
        }
        finally
        {
            _isLaunchingMame = false;
            if (_launchMameLuaButton != null)
                _launchMameLuaButton.Disabled = false;
        }
    }

    private void OnLoadTracePressed()
    {
        string path = string.IsNullOrWhiteSpace(_currentTracePath) ? DefaultTracePath : _currentTracePath;

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

        try
        {
            string text = file.GetAsText();
            _frames = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? ParseJsonLinesTrace(text)
                : ParseJsonTraceFile(text);
        }
        catch (Exception ex)
        {
            Log($"Trace parse error: {ex.Message}");
            return;
        }

        _currentFrameIndex = 0;
        _isRunning = false;
        _isPaused = true;
        _playbackAccumulator = 0;
        UpdatePauseResumeButtonText();

        if (_frames.Count == 0)
        {
            Log($"Trace loaded but contains no frame: {path}");
            UpdateStatus();
            return;
        }

        ApplyCurrentFrame();
        Log($"Trace loaded: {path}");
        Log($"Frames: {_frames.Count}");
        Log($"Gates in first frame: {_frames[0].gates?.Count ?? 0}");

        EnemyTraceActor? player = _frames[0].player;
        if (player != null)
            Log($"Player first frame: x={player.x:X2} y={player.y:X2} target=({player.turnTargetX:X2},{player.turnTargetY:X2}) dir={player.dir}");

        UpdateStatus();
    }

    private static List<EnemyTraceFrame> ParseJsonTraceFile(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text, TraceJsonDocumentOptions);
        JsonElement root = document.RootElement;

        var frames = new List<EnemyTraceFrame>();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("frames", out JsonElement framesElement))
        {
            foreach (JsonElement frameElement in framesElement.EnumerateArray())
                frames.Add(ParseFrame(frameElement));
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            frames.Add(ParseFrame(root));
        }

        return frames;
    }

    private static List<EnemyTraceFrame> ParseJsonLinesTrace(string text)
    {
        var frames = new List<EnemyTraceFrame>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            using JsonDocument document = JsonDocument.Parse(line, TraceJsonDocumentOptions);
            frames.Add(ParseFrame(document.RootElement));
        }

        return frames;
    }

    private static EnemyTraceFrame ParseFrame(JsonElement element)
    {
        var frame = new EnemyTraceFrame
        {
            frame = ReadInt(element, "frame", ReadInt(element, "tick", 0))
        };

        if (element.TryGetProperty("player", out JsonElement playerElement))
            frame.player = ParseActor(playerElement, -1, true);

        if (element.TryGetProperty("enemies", out JsonElement enemiesElement) && enemiesElement.ValueKind == JsonValueKind.Array)
        {
            frame.enemies = new List<EnemyTraceActor>();
            foreach (JsonElement enemyElement in enemiesElement.EnumerateArray())
                frame.enemies.Add(ParseActor(enemyElement, ReadInt(enemyElement, "slot", 0), false));
        }

        if (element.TryGetProperty("gates", out JsonElement gatesElement) && gatesElement.ValueKind == JsonValueKind.Array)
        {
            frame.gates = new List<EnemyTraceGateState>();
            foreach (JsonElement gateElement in gatesElement.EnumerateArray())
                frame.gates.Add(ParseGate(gateElement));
        }

        return frame;
    }

    private static EnemyTraceActor ParseActor(JsonElement element, int defaultSlot, bool defaultActive)
    {
        int raw = ReadInt(element, "raw", -1);

        // The MAME v8 trace does not write collisionActive for the player,
        // but it does write the raw 0x6026 byte. In the arcade object layout,
        // bit 1 is the active / collision-enabled bit. Previously the debug
        // renderer always forced the player to active, so stale/uninitialized
        // player coordinates could appear as a strange red "P" near the
        // top-left of the board when a trace was loaded.
        bool activeFromRaw = raw >= 0 ? (raw & 0x02) != 0 : defaultActive;
        bool activeFallback = ReadBool(element, "collisionActive", activeFromRaw);

        int turnTargetX = ReadInt(element, "turnTargetX", ReadInt(element, "targetX", -1));
        int turnTargetY = ReadInt(element, "turnTargetY", ReadInt(element, "targetY", -1));

        return new EnemyTraceActor
        {
            slot = ReadInt(element, "slot", defaultSlot),
            raw = raw,
            x = ReadInt(element, "x", 0),
            y = ReadInt(element, "y", 0),
            turnTargetX = turnTargetX,
            turnTargetY = turnTargetY,
            dir = ReadString(element, "dir", ReadString(element, "currentDir", string.Empty)),
            active = ReadBool(element, "active", activeFallback)
        };
    }

    private static EnemyTraceGateState ParseGate(JsonElement element)
    {
        int pivotX = -1;
        int pivotY = -1;

        if (element.TryGetProperty("pivot", out JsonElement pivotElement) && pivotElement.ValueKind == JsonValueKind.Object)
        {
            pivotX = ReadInt(pivotElement, "x", -1);
            pivotY = ReadInt(pivotElement, "y", -1);
        }
        else if (element.TryGetProperty("gatePivot", out JsonElement gatePivotElement) && gatePivotElement.ValueKind == JsonValueKind.Object)
        {
            pivotX = ReadInt(gatePivotElement, "x", -1);
            pivotY = ReadInt(gatePivotElement, "y", -1);
        }

        return new EnemyTraceGateState
        {
            gate_id = ReadInt(element, "gate_id", ReadInt(element, "godotGateId", ReadInt(element, "id", -1))),
            orientation = ReadString(element, "orientation", ReadString(element, "currentOrientation", "Unknown")),
            pivot_x = pivotX,
            pivot_y = pivotY
        };
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int n) => n,
            JsonValueKind.String => ParseIntString(value.GetString(), fallback),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => fallback
        };
    }

    private static int ParseIntString(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        string value = text.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value[2..], 16);

        bool looksHex = value.Length <= 2;
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
        }

        if (looksHex)
            return Convert.ToInt32(value, 16);

        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number when value.TryGetInt32(out int n) => n.ToString("X2"),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out int n) => n != 0,
            JsonValueKind.String => ParseBoolString(value.GetString(), fallback),
            _ => fallback
        };
    }

    private static bool ParseBoolString(string? text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        if (bool.TryParse(text, out bool parsed))
            return parsed;

        return ParseIntString(text, fallback ? 1 : 0) != 0;
    }

    private void OnRunSimulationPressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded. Load a trace before starting playback.");
            return;
        }

        _currentFrameIndex = 0;
        _isRunning = true;
        _isPaused = false;
        _playbackAccumulator = 0;
        ApplyCurrentFrame();
        Log("Playback restarted from the first frame.");
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

        // v0.2.1 still shows the same loaded MAME frame on both sides.
        // The left side becomes the output of the C# simulation in a later package.
        _simulationBoard?.SetSnapshot(frame);
        _mameBoard?.SetSnapshot(frame);
    }

    private void UpdateStatus()
    {
        UpdatePauseResumeButtonText();

        if (_statusLabel == null)
            return;

        if (_frames.Count == 0)
        {
            _statusLabel.Text = "Aucune trace chargée.";
            return;
        }

        EnemyTraceFrame frame = _frames[_currentFrameIndex];
        string state = _isRunning && !_isPaused ? "lecture" : "pause";
        _statusLabel.Text = $"Frame {_currentFrameIndex + 1}/{_frames.Count} | tick={frame.frame} | {state}";
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
    public int raw { get; set; } = -1;
    public int x { get; set; }
    public int y { get; set; }
    public int turnTargetX { get; set; } = -1;
    public int turnTargetY { get; set; } = -1;
    public bool HasTurnTarget => turnTargetX >= 0 && turnTargetY >= 0;
    public string? dir { get; set; }
    public bool active { get; set; } = true;
}

public sealed class EnemyTraceGateState
{
    public int gate_id { get; set; }
    public string? orientation { get; set; }
    public int pivot_x { get; set; } = -1;
    public int pivot_y { get; set; } = -1;
}
