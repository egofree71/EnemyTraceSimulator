using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

/// <summary>
/// UI shell used to load a MAME Lady Bug trace, replay it visually beside the
/// current C# simulation candidate, and stop at the first visible divergence.
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

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private string _currentTracePath = DefaultTracePath;
    private EnemyTraceBoardView? _simulationBoard;
    private EnemyTraceBoardView? _mameBoard;
    private TextEdit? _console;
    private Label? _statusLabel;
    private Button? _settingsButton;
    private Button? _launchMameLuaButton;
    private Button? _runSimulationButton;
    private Button? _pauseResumeButton;
    private Button? _stepButton;
    private SpinBox? _tickSpinBox;
    private Button? _dumpFrameButton;
    private Button? _findFrameButton;
    private Button? _compareButton;

    private List<EnemyTraceFrame> _frames = new();
    private List<SimulationFrame> _simulationFrames = new();
    private string _simulationSummary = string.Empty;
    private int _currentFrameIndex;
    private bool _isRunning;
    private bool _isPaused = true;
    private bool _isLaunchingMame;
    private bool _isUpdatingTickSpinBox;
    private double _playbackAccumulator;
    private VisualReplayMismatch _lastVisualMismatch = VisualReplayMismatch.None;

    public override void _Ready()
    {
        BindInterface();
        ConnectButtons();
        LoadDefaultMazeInBoards();

        Log("Enemy trace simulator UI ready.");
        Log("v0.9.10b: source-path single-enemy replay is the default candidate; Compare UI labels cleaned.");
        Log($"MAME config: {DefaultMameConfigPath}");
        Log($"Trace par défaut: {DefaultTracePath}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        if (!keyEvent.CtrlPressed)
            return;

        if (keyEvent.Keycode == Key.E)
        {
            ToggleInactiveEnemySlots();
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

            if (!StepOneFrameAndCompare())
                break;
        }
    }

    private void BindInterface()
    {
        _simulationBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/SimulationBoard");
        _mameBoard = GetNodeOrNull<EnemyTraceBoardView>("Root/MainLayout/BoardComparison/MameTraceBoard");
        _console = GetNodeOrNull<TextEdit>("Root/MainLayout/Console");
        if (_console != null)
            _console.WrapMode = TextEdit.LineWrappingMode.Boundary;

        _statusLabel = GetNodeOrNull<Label>("Root/MainLayout/StatusLabel");
        _settingsButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/SettingsButton");
        _launchMameLuaButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/LaunchMameLuaButton");
        _runSimulationButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/RunSimulationButton");
        _pauseResumeButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/PauseResumeButton");
        _stepButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/StepButton");
        _tickSpinBox = GetNodeOrNull<SpinBox>("Root/MainLayout/PlaybackControls/TickSpinBox");
        _dumpFrameButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/DumpFrameButton");
        _findFrameButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/FindFrameButton");
        _compareButton = GetNodeOrNull<Button>("Root/MainLayout/PlaybackControls/CompareButton");

        if (_simulationBoard != null)
        {
            _simulationBoard.BoardTitle = "Simulation C# / Godot";
            _simulationBoard.SetShowPlayerDebugMarkers(false);
        }

        if (_mameBoard != null)
        {
            _mameBoard.BoardTitle = "Trace MAME";
            _mameBoard.SetShowPlayerDebugMarkers(false);
        }

        ConfigurePlaybackButtons();
    }

    private void ConfigurePlaybackButtons()
    {
        ConfigurePlaybackButton(_settingsButton, "⚙", "Éditer config/mame_trace_settings.json");
        ConfigurePlaybackButton(_runSimulationButton, RestartButtonText, "Relancer la séquence depuis le début");
        ConfigurePlaybackButton(_pauseResumeButton, ResumeButtonText, "Mettre en pause ou reprendre la séquence");
        ConfigurePlaybackButton(_stepButton, StepButtonText, "Avancer d’un tick et comparer");
        ConfigureTickSpinBox();
        ConfigureTextButton(_dumpFrameButton, "Dump", "Afficher dans la console le détail de la frame courante");
        ConfigureTextButton(_findFrameButton, "Find", "Ouvrir les helpers de navigation dans la trace");
        ConfigureTextButton(_compareButton, "Compare", "Comparer la trace MAME avec la simulation candidate");
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

    private static void ConfigureTextButton(Button? button, string text, string tooltipText)
    {
        if (button == null)
            return;

        button.Text = text;
        button.TooltipText = tooltipText;
        button.CustomMinimumSize = new Vector2(90, 38);
    }

    private void ConfigureTickSpinBox()
    {
        if (_tickSpinBox == null)
            return;

        _tickSpinBox.MinValue = 0;
        _tickSpinBox.MaxValue = 0;
        _tickSpinBox.Step = 1;
        _tickSpinBox.AllowGreater = true;
        _tickSpinBox.Rounded = true;
        _tickSpinBox.Editable = false;
        _tickSpinBox.TooltipText = "Entrer un tick et appuyer sur Entrée pour afficher la frame correspondante.";
        _tickSpinBox.CustomMinimumSize = new Vector2(110, 38);
    }

    private void ConnectButtons()
    {
        ConnectButton("Root/MainLayout/PlaybackControls/SettingsButton", OnSettingsPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/LaunchMameLuaButton", OnLaunchMameLuaPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/LoadTraceButton", OnLoadTracePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/RunSimulationButton", OnRunSimulationPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/PauseResumeButton", OnPauseResumePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/StepButton", OnStepPressed);
        ConnectButton("Root/MainLayout/PlaybackControls/DumpFrameButton", OnDumpFramePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/FindFrameButton", OnFindFramePressed);
        ConnectButton("Root/MainLayout/PlaybackControls/CompareButton", OnComparePressed);

        if (_tickSpinBox != null)
            _tickSpinBox.ValueChanged += OnTickSpinBoxValueChanged;
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
            _frames = MameTraceLoader.Load(path, text);
        }
        catch (Exception ex)
        {
            Log($"Trace parse error: {ex.Message}");
            return;
        }

        _currentTracePath = path;
        _currentFrameIndex = 0;
        _isRunning = false;
        _isPaused = true;
        _playbackAccumulator = 0;
        _lastVisualMismatch = VisualReplayMismatch.None;

        BuildSimulationFramesForLoadedTrace();
        ConfigureTickSpinBoxForLoadedTrace();

        if (_frames.Count > 0)
            ApplyCurrentFrame();

        Log($"Trace loaded: {path}");
        Log($"Frames: {_frames.Count}");

        if (_frames.Count > 0)
        {
            Log($"Gates in first frame: {_frames[0].gates?.Count ?? 0}");
            LogRawMemorySummary(_frames[0]);
            Log($"Active enemies in frame 0: {CountActiveEnemies(_frames[0])}");
            Log($"Known enemy slots in frame 0: {CountKnownEnemies(_frames[0])}");

            EnemyTraceActor? player = _frames[0].player;
            if (player != null)
                Log($"Player frame 0: mame=({player.x:X2},{player.y:X2}) godot=({player.x:X2},{MameTraceCoordinates.MameToGodotArcadeY(player.y):X2}) target=({player.turnTargetX:X2},{player.turnTargetY:X2}) dir={player.dir}");

            int firstActiveEnemyFrame = FindFirstFrameWithActiveEnemy();
            if (firstActiveEnemyFrame >= 0)
                Log($"First active enemy frame: index={firstActiveEnemyFrame}, tick={_frames[firstActiveEnemyFrame].frame}, active={CountActiveEnemies(_frames[firstActiveEnemyFrame])}");
        }

        if (!string.IsNullOrWhiteSpace(_simulationSummary))
            Log(_simulationSummary);

        UpdatePauseResumeButtonText();
        UpdateStatus();
    }

    private void BuildSimulationFramesForLoadedTrace()
    {
        _simulationFrames.Clear();
        _simulationSummary = string.Empty;

        if (_frames.Count == 0)
            return;

        try
        {
            IEnemySimulationAdapter adapter = new LadyBugEnemySimulationAdapter();
            SimulationAdapterResult result = adapter.Run(_frames);
            _simulationFrames = result.Frames;
            _simulationSummary = $"Simulation candidate: {adapter.Name}; frames={_simulationFrames.Count}";
        }
        catch (Exception ex)
        {
            _simulationSummary = $"Simulation candidate build failed: {ex.Message}";
            _simulationFrames.Clear();
        }
    }

    private void OnRunSimulationPressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        if (_simulationFrames.Count == 0)
            BuildSimulationFramesForLoadedTrace();

        _currentFrameIndex = 0;
        _isRunning = true;
        _isPaused = false;
        _playbackAccumulator = 0;
        _lastVisualMismatch = VisualReplayMismatch.None;
        ApplyCurrentFrame();
        UpdatePauseResumeButtonText();
        UpdateStatus();

        Log("Visual replay restarted from tick 0.");
        CheckCurrentVisualStateAndPauseIfMismatch(logMatchAtEnd: false);
    }

    private void OnPauseResumePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        if (!_isRunning)
            _isRunning = true;

        _isPaused = !_isPaused;
        _playbackAccumulator = 0;
        UpdatePauseResumeButtonText();
        UpdateStatus();
    }

    private void OnStepPressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        if (_simulationFrames.Count == 0)
            BuildSimulationFramesForLoadedTrace();

        _isRunning = true;
        _isPaused = true;
        _playbackAccumulator = 0;
        StepOneFrameAndCompare();
        UpdatePauseResumeButtonText();
    }

    /// <summary>
    /// Advances one displayed tick and compares the visual state. Returns false
    /// when playback should stop.
    /// </summary>
    private bool StepOneFrameAndCompare()
    {
        if (_frames.Count == 0)
            return false;

        if (_currentFrameIndex >= _frames.Count - 1)
        {
            _isPaused = true;
            Log("Visual replay reached the end of the trace without a new mismatch.");
            UpdatePauseResumeButtonText();
            UpdateStatus();
            return false;
        }

        _currentFrameIndex++;
        ApplyCurrentFrame();
        return !CheckCurrentVisualStateAndPauseIfMismatch(logMatchAtEnd: false);
    }

    private bool CheckCurrentVisualStateAndPauseIfMismatch(bool logMatchAtEnd)
    {
        if (_currentFrameIndex < 0 || _currentFrameIndex >= _frames.Count)
            return false;

        SimulationFrame? simulation = _currentFrameIndex < _simulationFrames.Count
            ? _simulationFrames[_currentFrameIndex]
            : null;

        VisualReplayMismatch mismatch = VisualReplayComparator.Compare(
            simulation,
            _frames[_currentFrameIndex],
            _currentFrameIndex);

        if (!mismatch.HasMismatch)
        {
            if (logMatchAtEnd)
                Log($"Visual replay check: tick {_frames[_currentFrameIndex].frame} matches.");
            return false;
        }

        _lastVisualMismatch = mismatch;
        _isPaused = true;
        _playbackAccumulator = 0;
        UpdatePauseResumeButtonText();
        UpdateStatus();
        Log(mismatch.ToString());
        return true;
    }

    private void ApplyCurrentFrame()
    {
        if (_frames.Count == 0 || _currentFrameIndex < 0 || _currentFrameIndex >= _frames.Count)
            return;

        EnemyTraceFrame reference = _frames[_currentFrameIndex];
        _mameBoard?.SetSnapshot(reference);

        if (_currentFrameIndex < _simulationFrames.Count)
            _simulationBoard?.SetSnapshot(VisualReplayFrameConverter.ToTraceFrame(_simulationFrames[_currentFrameIndex]));
        else
            _simulationBoard?.SetSnapshot(reference);

        SyncTickSpinBoxToCurrentFrame();
        UpdateStatus();
    }

    private void OnTickSpinBoxValueChanged(double value)
    {
        if (_isUpdatingTickSpinBox || _frames.Count == 0)
            return;

        int requestedTick = (int)Math.Round(value);
        JumpToTick(requestedTick, true);
    }

    private void JumpToTick(int requestedTick, bool logNearest)
    {
        if (_frames.Count == 0)
            return;

        int index = FindFrameIndexForTick(requestedTick, out bool exact);
        _currentFrameIndex = index;
        _isRunning = true;
        _isPaused = true;
        _playbackAccumulator = 0;
        _lastVisualMismatch = VisualReplayMismatch.None;
        ApplyCurrentFrame();
        CheckCurrentVisualStateAndPauseIfMismatch(logMatchAtEnd: false);

        if (!exact && logNearest)
        {
            EnemyTraceFrame frame = _frames[_currentFrameIndex];
            Log($"Tick {requestedTick} not found. Showing nearest tick {frame.frame} at frame index {_currentFrameIndex}.");
        }
    }

    private int FindFrameIndexForTick(int requestedTick, out bool exact)
    {
        exact = false;
        if (_frames.Count == 0)
            return 0;

        int bestIndex = 0;
        int bestDistance = Math.Abs(_frames[0].frame - requestedTick);

        for (int i = 0; i < _frames.Count; i++)
        {
            int distance = Math.Abs(_frames[i].frame - requestedTick);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }

            if (_frames[i].frame == requestedTick)
            {
                exact = true;
                return i;
            }
        }

        return bestIndex;
    }

    private void ConfigureTickSpinBoxForLoadedTrace()
    {
        if (_tickSpinBox == null)
            return;

        _isUpdatingTickSpinBox = true;

        if (_frames.Count == 0)
        {
            _tickSpinBox.MinValue = 0;
            _tickSpinBox.MaxValue = 0;
            _tickSpinBox.Value = 0;
            _tickSpinBox.Editable = false;
            _isUpdatingTickSpinBox = false;
            return;
        }

        int minTick = _frames[0].frame;
        int maxTick = _frames[0].frame;
        foreach (EnemyTraceFrame frame in _frames)
        {
            minTick = Math.Min(minTick, frame.frame);
            maxTick = Math.Max(maxTick, frame.frame);
        }

        _tickSpinBox.MinValue = minTick;
        _tickSpinBox.MaxValue = maxTick;
        _tickSpinBox.AllowGreater = true;
        _tickSpinBox.Editable = true;
        _tickSpinBox.Value = _frames[_currentFrameIndex].frame;
        _isUpdatingTickSpinBox = false;
    }

    private void SyncTickSpinBoxToCurrentFrame()
    {
        if (_tickSpinBox == null || _frames.Count == 0)
            return;

        _isUpdatingTickSpinBox = true;
        _tickSpinBox.Value = _frames[_currentFrameIndex].frame;
        _isUpdatingTickSpinBox = false;
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null)
            return;

        if (_frames.Count == 0)
        {
            _statusLabel.Text = "Aucune trace chargée";
            return;
        }

        EnemyTraceFrame frame = _frames[_currentFrameIndex];
        string state = _isPaused ? "pause" : "lecture";
        string mismatch = _lastVisualMismatch.HasMismatch
            ? $" | STOP mismatch: {_lastVisualMismatch.Category}"
            : string.Empty;

        _statusLabel.Text =
            $"Frame {_currentFrameIndex + 1}/{_frames.Count} | Tick {frame.frame} | " +
            $"Ennemis actifs {CountActiveEnemies(frame)} | {state}{mismatch}";
    }

    private void UpdatePauseResumeButtonText()
    {
        if (_pauseResumeButton == null)
            return;

        _pauseResumeButton.Text = _isRunning && !_isPaused ? PauseButtonText : ResumeButtonText;
    }

    private void OnComparePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        ShowComparisonWindow();
    }

    private void ShowComparisonWindow()
    {
        var compareWindow = new Window
        {
            Title = "Trace comparison",
            Transient = false,
            Exclusive = false,
            Unresizable = false,
            MinSize = new Vector2I(660, 440)
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        compareWindow.AddChild(margin);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var explanation = new Label
        {
            Text = "Run the current default Lady Bug candidate adapter and compare the visual replay state. The normal playback buttons already stop on the first visual mismatch.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(explanation);

        var runButton = new Button
        {
            Text = "Run Lady Bug source-path single-enemy replay",
            CustomMinimumSize = new Vector2(340, 36)
        };
        root.AddChild(runButton);

        var output = new TextEdit
        {
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddChild(output);

        runButton.Pressed += () =>
        {
            try
            {
                IEnemySimulationAdapter adapter = new LadyBugEnemySimulationAdapter();
                SimulationAdapterResult result = adapter.Run(_frames);
                string visualSummary = VisualReplayComparator.BuildSummary(result.Frames, _frames);

                var builder = new StringBuilder();
                builder.AppendLine($"Comparison [{adapter.Name}]");
                builder.AppendLine($"simulationFrames={result.Frames.Count}, referenceFrames={_frames.Count}");
                builder.AppendLine(visualSummary);
                builder.AppendLine();
                builder.AppendLine("Adapter summary:");
                builder.AppendLine(result.Summary);
                output.Text = builder.ToString();

                Log($"Comparison [{adapter.Name}]: {visualSummary}");
            }
            catch (Exception ex)
            {
                output.Text = $"Comparison failed: {ex}";
                Log($"Comparison failed: {ex.Message}");
            }
        };

        AddChild(compareWindow);
        compareWindow.CloseRequested += compareWindow.QueueFree;
        compareWindow.PopupCentered(new Vector2I(760, 520));
    }

    private void OnDumpFramePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        EnemyTraceFrame frame = _frames[_currentFrameIndex];
        SimulationFrame? simulation = _currentFrameIndex < _simulationFrames.Count
            ? _simulationFrames[_currentFrameIndex]
            : null;

        var builder = new StringBuilder();
        builder.AppendLine($"Frame index={_currentFrameIndex} tick={frame.frame} mameFrame={frame.mameFrame} phase={frame.phase} pc={frame.pc} r={frame.r}");
        builder.AppendLine(BuildRawMemorySummary(frame));
        builder.AppendLine();
        builder.AppendLine("MAME reference:");
        AppendTraceActors(builder, frame);
        builder.AppendLine();
        builder.AppendLine("Simulation candidate:");
        AppendSimulationActors(builder, simulation);

        Log(builder.ToString());
        ShowTextWindow($"Frame dump tick {frame.frame}", builder.ToString(), new Vector2I(760, 520));
    }

    private void OnFindFramePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        var window = new Window
        {
            Title = "Find frame",
            Transient = false,
            Exclusive = false,
            MinSize = new Vector2I(420, 220)
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        window.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var firstActive = new Button { Text = "Jump to first active enemy" };
        var firstMismatch = new Button { Text = "Jump to first visual mismatch" };
        var currentCheck = new Button { Text = "Check current tick" };
        root.AddChild(firstActive);
        root.AddChild(firstMismatch);
        root.AddChild(currentCheck);

        firstActive.Pressed += () =>
        {
            int index = FindFirstFrameWithActiveEnemy();
            if (index >= 0)
                JumpToFrameIndex(index, true);
        };

        firstMismatch.Pressed += () =>
        {
            int index = FindFirstVisualMismatchFrameIndex();
            if (index >= 0)
                JumpToFrameIndex(index, true);
            else
                Log("No visual mismatch found in the current simulation replay.");
        };

        currentCheck.Pressed += () => CheckCurrentVisualStateAndPauseIfMismatch(logMatchAtEnd: true);

        AddChild(window);
        window.CloseRequested += window.QueueFree;
        window.PopupCentered(new Vector2I(440, 240));
    }

    private int FindFirstVisualMismatchFrameIndex()
    {
        int count = Math.Min(_simulationFrames.Count, _frames.Count);
        for (int i = 0; i < count; i++)
        {
            VisualReplayMismatch mismatch = VisualReplayComparator.Compare(_simulationFrames[i], _frames[i], i);
            if (mismatch.HasMismatch)
            {
                _lastVisualMismatch = mismatch;
                Log(mismatch.ToString());
                return i;
            }
        }

        return -1;
    }

    private void JumpToFrameIndex(int index, bool log)
    {
        if (index < 0 || index >= _frames.Count)
            return;

        _currentFrameIndex = index;
        _isRunning = true;
        _isPaused = true;
        _playbackAccumulator = 0;
        ApplyCurrentFrame();
        CheckCurrentVisualStateAndPauseIfMismatch(logMatchAtEnd: false);

        if (log)
            Log($"Showing frame index={index}, tick={_frames[index].frame}.");
    }

    private async void OnLaunchMameLuaPressed()
    {
        if (_isLaunchingMame)
        {
            Log("MAME launch already in progress.");
            return;
        }

        _isLaunchingMame = true;
        if (_launchMameLuaButton != null)
            _launchMameLuaButton.Disabled = true;

        Log($"Launching MAME from config: {DefaultMameConfigPath}");

        try
        {
            MameTraceLaunchResult result = await MameTraceLauncher.LaunchAsync(DefaultMameConfigPath);
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

    private void OnSettingsPressed()
    {
        try
        {
            MameTraceSettings settings = LoadMameTraceSettings();
            ShowMameSettingsDialog(settings);
        }
        catch (Exception ex)
        {
            Log($"Could not open MAME settings dialog: {ex.Message}");
        }
    }

    private MameTraceSettings LoadMameTraceSettings()
    {
        string configAbsolutePath = ResolveProjectPath(DefaultMameConfigPath);
        if (!System.IO.File.Exists(configAbsolutePath))
        {
            Log($"Settings file not found. A new one will be created: {DefaultMameConfigPath}");
            return new MameTraceSettings();
        }

        string json = System.IO.File.ReadAllText(configAbsolutePath);
        return JsonSerializer.Deserialize<MameTraceSettings>(json, SettingsJsonOptions) ?? new MameTraceSettings();
    }

    private void SaveMameTraceSettings(MameTraceSettings settings)
    {
        string configAbsolutePath = ResolveProjectPath(DefaultMameConfigPath);
        string? directory = System.IO.Path.GetDirectoryName(configAbsolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        System.IO.File.WriteAllText(configAbsolutePath, json + System.Environment.NewLine);
        Log($"MAME settings saved: {DefaultMameConfigPath}");
    }

    private void ShowMameSettingsDialog(MameTraceSettings settings)
    {
        var dialog = new Window
        {
            Title = "MAME trace settings",
            Transient = true,
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(560, 440)
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        dialog.AddChild(margin);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddChild(scroll);

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(grid);

        var fields = new MameSettingsEditorFields
        {
            MameExecutable = AddLineSetting(grid, "MAME executable", settings.MameExecutable),
            Game = AddLineSetting(grid, "Game / driver", settings.Game),
            RomPath = AddLineSetting(grid, "ROM path", settings.RomPath),
            StateDirectory = AddLineSetting(grid, "State directory", settings.StateDirectory),
            StateSubdir = AddLineSetting(grid, "State subdir / statename", settings.StateSubdir),
            SaveState = AddLineSetting(grid, "Save-state", settings.SaveState),
            LuaScriptPath = AddLineSetting(grid, "Lua script path", settings.LuaScriptPath),
            OutputDirectory = AddLineSetting(grid, "Trace output directory", settings.OutputDirectory),
            OutputPrefix = AddLineSetting(grid, "Trace output prefix", settings.OutputPrefix),
            FramesAfterTick0 = AddIntSetting(grid, "Frames after tick 0", settings.FramesAfterTick0, 0, 100000),
            AutobootDelay = AddIntSetting(grid, "Autoboot delay", settings.AutobootDelay, 0, 60),
            Windowed = AddBoolSetting(grid, "Windowed", settings.Windowed),
            EnableDebugger = AddBoolSetting(grid, "Enable debugger", settings.EnableDebugger),
            ExitWhenDone = AddBoolSetting(grid, "Exit when done", settings.ExitWhenDone),
            PauseWhenDone = AddBoolSetting(grid, "Pause when done", settings.PauseWhenDone),
            FlushEveryTraceLine = AddBoolSetting(grid, "Flush every trace line", settings.FlushEveryTraceLine),
            IncludeFullMemoryEachFrame = AddBoolSetting(grid, "Include full memory each frame", settings.IncludeFullMemoryEachFrame),
            IncludeLogicalMazeEachFrame = AddBoolSetting(grid, "Include logical maze each frame", settings.IncludeLogicalMazeEachFrame)
        };

        var buttonRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        buttonRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttonRow);

        var okButton = new Button { Text = "OK", CustomMinimumSize = new Vector2(90, 34) };
        var cancelButton = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(90, 34) };

        okButton.Pressed += () =>
        {
            try
            {
                SaveMameTraceSettings(BuildSettingsFromFields(fields));
                dialog.QueueFree();
            }
            catch (Exception ex)
            {
                Log($"Could not save MAME settings: {ex.Message}");
            }
        };

        cancelButton.Pressed += dialog.QueueFree;
        dialog.CloseRequested += dialog.QueueFree;
        buttonRow.AddChild(okButton);
        buttonRow.AddChild(cancelButton);

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(700, 560));
    }

    private static MameTraceSettings BuildSettingsFromFields(MameSettingsEditorFields fields)
    {
        return new MameTraceSettings
        {
            MameExecutable = fields.MameExecutable.Text.Trim(),
            Game = fields.Game.Text.Trim(),
            RomPath = fields.RomPath.Text.Trim(),
            StateDirectory = fields.StateDirectory.Text.Trim(),
            StateSubdir = fields.StateSubdir.Text.Trim(),
            SaveState = fields.SaveState.Text.Trim(),
            LuaScriptPath = fields.LuaScriptPath.Text.Trim(),
            OutputDirectory = fields.OutputDirectory.Text.Trim(),
            OutputPrefix = fields.OutputPrefix.Text.Trim(),
            FramesAfterTick0 = (int)Math.Round(fields.FramesAfterTick0.Value),
            AutobootDelay = (int)Math.Round(fields.AutobootDelay.Value),
            Windowed = fields.Windowed.ButtonPressed,
            EnableDebugger = fields.EnableDebugger.ButtonPressed,
            ExitWhenDone = fields.ExitWhenDone.ButtonPressed,
            PauseWhenDone = fields.PauseWhenDone.ButtonPressed,
            FlushEveryTraceLine = fields.FlushEveryTraceLine.ButtonPressed,
            IncludeFullMemoryEachFrame = fields.IncludeFullMemoryEachFrame.ButtonPressed,
            IncludeLogicalMazeEachFrame = fields.IncludeLogicalMazeEachFrame.ButtonPressed
        };
    }

    private static LineEdit AddLineSetting(GridContainer grid, string labelText, string value)
    {
        grid.AddChild(MakeSettingsLabel(labelText));
        var lineEdit = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(lineEdit);
        return lineEdit;
    }

    private static SpinBox AddIntSetting(GridContainer grid, string labelText, int value, int minValue, int maxValue)
    {
        grid.AddChild(MakeSettingsLabel(labelText));
        var spinBox = new SpinBox
        {
            MinValue = minValue,
            MaxValue = maxValue,
            Step = 1,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        grid.AddChild(spinBox);
        return spinBox;
    }

    private static CheckBox AddBoolSetting(GridContainer grid, string labelText, bool value)
    {
        grid.AddChild(MakeSettingsLabel(labelText));
        var checkBox = new CheckBox { ButtonPressed = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(checkBox);
        return checkBox;
    }

    private static Label MakeSettingsLabel(string text)
    {
        return new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(230, 0)
        };
    }

    private static string ResolveProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        if (System.IO.Path.IsPathRooted(path))
            return System.IO.Path.GetFullPath(path);

        return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), path));
    }

    private sealed class MameSettingsEditorFields
    {
        public LineEdit MameExecutable { get; init; } = null!;
        public LineEdit Game { get; init; } = null!;
        public LineEdit RomPath { get; init; } = null!;
        public LineEdit StateDirectory { get; init; } = null!;
        public LineEdit StateSubdir { get; init; } = null!;
        public LineEdit SaveState { get; init; } = null!;
        public LineEdit LuaScriptPath { get; init; } = null!;
        public LineEdit OutputDirectory { get; init; } = null!;
        public LineEdit OutputPrefix { get; init; } = null!;
        public SpinBox FramesAfterTick0 { get; init; } = null!;
        public SpinBox AutobootDelay { get; init; } = null!;
        public CheckBox Windowed { get; init; } = null!;
        public CheckBox EnableDebugger { get; init; } = null!;
        public CheckBox ExitWhenDone { get; init; } = null!;
        public CheckBox PauseWhenDone { get; init; } = null!;
        public CheckBox FlushEveryTraceLine { get; init; } = null!;
        public CheckBox IncludeFullMemoryEachFrame { get; init; } = null!;
        public CheckBox IncludeLogicalMazeEachFrame { get; init; } = null!;
    }

    private static int CountActiveEnemies(EnemyTraceFrame frame)
    {
        if (frame.enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.active)
                count++;
        }

        return count;
    }

    private static int CountKnownEnemies(EnemyTraceFrame frame)
    {
        if (frame.enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.HasKnownPosition)
                count++;
        }

        return count;
    }

    private int FindFirstFrameWithActiveEnemy()
    {
        for (int i = 0; i < _frames.Count; i++)
        {
            if (CountActiveEnemies(_frames[i]) > 0)
                return i;
        }

        return -1;
    }

    private static string BuildRawMemorySummary(EnemyTraceFrame frame)
    {
        if (frame.rawMemory == null)
            return "Frame memory blocks: none";

        return $"Frame memory blocks: maze={frame.rawMemory.LogicalMazeByteCount} bytes, " +
               $"ram={frame.rawMemory.RamByteCount} bytes, " +
               $"vram={frame.rawMemory.VramByteCount} bytes, " +
               $"color={frame.rawMemory.ColorByteCount} bytes";
    }

    private void LogRawMemorySummary(EnemyTraceFrame frame)
    {
        Log(BuildRawMemorySummary(frame));
    }

    private static void AppendTraceActors(StringBuilder builder, EnemyTraceFrame frame)
    {
        if (frame.player != null)
            builder.AppendLine($"player xy=({Hex(frame.player.x)},{Hex(frame.player.y)}) dir={frame.player.dir} active={frame.player.active}");

        if (frame.enemies == null)
            return;

        foreach (EnemyTraceActor enemy in frame.enemies)
            builder.AppendLine($"enemy{enemy.slot} raw={Hex(enemy.raw)} xy=({Hex(enemy.x)},{Hex(enemy.y)}) dir={enemy.dir} active={enemy.active}");
    }

    private static void AppendSimulationActors(StringBuilder builder, SimulationFrame? frame)
    {
        if (frame == null)
        {
            builder.AppendLine("missing simulation frame");
            return;
        }

        if (frame.Player != null)
            builder.AppendLine($"player xy=({Hex(frame.Player.X)},{Hex(frame.Player.Y)}) dir={frame.Player.Direction} active={frame.Player.Active}");

        foreach (SimulationActorState enemy in frame.Enemies)
            builder.AppendLine($"enemy{enemy.Slot} raw={Hex(enemy.Raw)} xy=({Hex(enemy.X)},{Hex(enemy.Y)}) dir={enemy.Direction} active={enemy.Active}");
    }

    private void ToggleInactiveEnemySlots()
    {
        bool show = !(_mameBoard?.ShowInactiveEnemySlots ?? _simulationBoard?.ShowInactiveEnemySlots ?? false);
        _simulationBoard?.SetShowInactiveEnemySlots(show);
        _mameBoard?.SetShowInactiveEnemySlots(show);
        Log(show ? "Inactive enemy slots visible." : "Inactive enemy slots hidden.");
    }

    private static string Hex(int value)
    {
        return value < 0 ? "--" : (value & 0xFF).ToString("X2");
    }

    private void ShowTextWindow(string title, string text, Vector2I size)
    {
        var window = new Window
        {
            Title = title,
            Transient = false,
            Exclusive = false,
            MinSize = new Vector2I(500, 320)
        };

        var edit = new TextEdit
        {
            Editable = false,
            Text = text,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        window.AddChild(edit);
        AddChild(window);
        window.CloseRequested += window.QueueFree;
        window.PopupCentered(size);
    }

    private void Log(string message)
    {
        string stamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        GD.Print(stamped);

        if (_console == null)
            return;

        _console.Text += stamped + System.Environment.NewLine;
        _console.ScrollVertical = _console.GetLineCount();
    }
}
