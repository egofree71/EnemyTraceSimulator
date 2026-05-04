using Godot;
using System;
using System.Collections.Generic;
using System.Text;
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
    private Button? _settingsButton;
    private Button? _launchMameLuaButton;
    private Button? _runSimulationButton;
    private Button? _pauseResumeButton;
    private Button? _stepButton;
    private SpinBox? _tickSpinBox;
    private Button? _dumpFrameButton;
    private Button? _findFrameButton;
    private Button? _compareButton;


    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private List<EnemyTraceFrame> _frames = new();
    private int _currentFrameIndex;
    private bool _isRunning;
    private bool _isPaused = true;
    private bool _isLaunchingMame;
    private bool _isUpdatingTickSpinBox;
    private double _playbackAccumulator;

    public override void _Ready()
    {
        BindInterface();
        ConnectButtons();
        LoadDefaultMazeInBoards();

        Log("Enemy trace simulator UI ready.");
        Log("v0.6.0: simulation adapter interface added.");
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

        if (keyEvent.Keycode == Key.E)
        {
            ToggleInactiveEnemySlots();
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
            _simulationBoard.BoardTitle = "Simulation C# / Godot";

        if (_mameBoard != null)
            _mameBoard.BoardTitle = "Trace MAME";

        ConfigurePlaybackButtons();
    }

    private void ConfigurePlaybackButtons()
    {
        ConfigurePlaybackButton(
            _settingsButton,
            "⚙",
            "Éditer config/mame_trace_settings.json");

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

        ConfigureTickSpinBox();

        ConfigureTextButton(
            _dumpFrameButton,
            "Dump",
            "Afficher dans la console le détail de la frame courante");

        ConfigureTextButton(
            _findFrameButton,
            "Find",
            "Ouvrir les helpers de navigation dans la trace");

        ConfigureTextButton(
            _compareButton,
            "Compare",
            "Comparer la trace MAME avec une simulation de référence temporaire");

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

    private void UpdatePauseResumeButtonText()
    {
        if (_pauseResumeButton == null)
            return;

        _pauseResumeButton.Text = _isRunning && !_isPaused ? PauseButtonText : ResumeButtonText;
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

    private void ToggleInactiveEnemySlots()
    {
        bool show = !(_mameBoard?.ShowInactiveEnemySlots
                      ?? _simulationBoard?.ShowInactiveEnemySlots
                      ?? false);

        _simulationBoard?.SetShowInactiveEnemySlots(show);
        _mameBoard?.SetShowInactiveEnemySlots(show);

        Log(show
            ? "Inactive enemy slots visible. These are diagnostic only."
            : "Inactive enemy slots hidden.");
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


    private void LogFrameMetadata(EnemyTraceFrame frame)
    {
        Log($"Frame 0 metadata: schema={frame.schema} phase={frame.phase} mameFrame={frame.mameFrame} pc={frame.pc} r={frame.r}");
    }

    private void LogRawMemorySummary(EnemyTraceFrame frame)
    {
        string summary = BuildRawMemorySummary(frame);
        Log(summary);
    }

    private static string BuildRawMemorySummary(EnemyTraceFrame frame)
    {
        if (frame.rawMemory == null)
            return "Frame memory blocks: none";

        return $"Frame memory blocks: " +
               $"maze={frame.rawMemory.LogicalMazeByteCount} bytes, " +
               $"ram={frame.rawMemory.RamByteCount} bytes, " +
               $"vram={frame.rawMemory.VramByteCount} bytes, " +
               $"color={frame.rawMemory.ColorByteCount} bytes";
    }

    private void LogTraceSummary()
    {
        if (_frames.Count == 0)
            return;

        int firstActiveEnemyFrame = FindFirstFrameWithActiveEnemy();
        if (firstActiveEnemyFrame >= 0)
        {
            EnemyTraceFrame frame = _frames[firstActiveEnemyFrame];
            Log($"First active enemy frame: index={firstActiveEnemyFrame}, tick={frame.frame}, active={CountActiveEnemies(frame)}");
        }
        else
        {
            Log("No active enemy found in trace.");
        }

        Log("Use Dump to inspect the current frame in a separate window.");
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

    private static string FormatEnemyWork(EnemyTraceEnemyWorkState? enemyWork)
    {
        if (enemyWork == null)
            return string.Empty;

        return $" | work tmp={enemyWork.tempDir:X2}:({enemyWork.tempX:X2},{enemyWork.tempY:X2}) " +
               $"rej={enemyWork.rejectedMask:X2} fb={enemyWork.fallbackMask:X2} " +
               $"pref=[{FormatHexList(enemyWork.preferred)}] chase=[{FormatHexList(enemyWork.chaseTimers)}] " +
               $"rr={enemyWork.chaseRoundRobin:X2}";
    }

    private static string FormatHexList(List<int>? values)
    {
        if (values == null || values.Count == 0)
            return string.Empty;

        var formatted = new List<string>(values.Count);
        foreach (int value in values)
            formatted.Add(value < 0 ? "--" : value.ToString("X2"));

        return string.Join(",", formatted);
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
        return JsonSerializer.Deserialize<MameTraceSettings>(json, SettingsJsonOptions)
               ?? new MameTraceSettings();
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

        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), path));
    }

    private void ShowMameSettingsDialog(MameTraceSettings settings)
    {
        var dialog = new Window
        {
            Title = "MAME trace settings",
            Transient = true,
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(560, 420)
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

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        scroll.AddChild(grid);
        root.AddChild(scroll);

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

        var okButton = new Button
        {
            Text = "OK",
            CustomMinimumSize = new Vector2(90, 34)
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            CustomMinimumSize = new Vector2(90, 34)
        };

        okButton.Pressed += () =>
        {
            try
            {
                MameTraceSettings edited = BuildSettingsFromFields(fields);
                SaveMameTraceSettings(edited);
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

        Vector2 viewportSize = GetViewportRect().Size;
        int width = Math.Clamp((int)viewportSize.X - 40, 560, 780);
        int height = Math.Clamp((int)viewportSize.Y - 80, 420, 560);

        dialog.PopupCentered(new Vector2I(width, height));
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

        var lineEdit = new LineEdit
        {
            Text = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

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

        var checkBox = new CheckBox
        {
            ButtonPressed = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        grid.AddChild(checkBox);
        return checkBox;
    }

    private static Label MakeSettingsLabel(string text)
    {
        return new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(210, 0)
        };
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
            _frames = MameTraceLoader.Load(path, text);
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
            ConfigureTickSpinBoxForLoadedTrace();
            Log($"Trace loaded but contains no frame: {path}");
            UpdateStatus();
            return;
        }

        ConfigureTickSpinBoxForLoadedTrace();
        ApplyCurrentFrame();
        Log($"Trace loaded: {path}");
        Log($"Frames: {_frames.Count}");
        Log($"Gates in first frame: {_frames[0].gates?.Count ?? 0}");
        LogFrameMetadata(_frames[0]);
        LogRawMemorySummary(_frames[0]);
        Log($"Active enemies in frame 0: {CountActiveEnemies(_frames[0])}");
        Log($"Known enemy slots in frame 0: {CountKnownEnemies(_frames[0])}");

        EnemyTraceActor? player = _frames[0].player;
        if (player != null)
            Log($"Player frame 0: mame=({player.x:X2},{player.y:X2}) godot=({player.x:X2},{MameTraceCoordinates.MameToGodotArcadeY(player.y):X2}) target=({player.turnTargetX:X2},{player.turnTargetY:X2}) dir={player.dir}");

        LogTraceSummary();
        UpdateStatus();
    }

    private void OnTickSpinBoxValueChanged(double value)
    {
        if (_isUpdatingTickSpinBox)
            return;

        if (_frames.Count == 0)
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

        ApplyCurrentFrame();
        UpdateStatus();

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
            MinSize = new Vector2I(480, 260)
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
            Text = "v0.5 validates the comparison pipeline before the real C# enemy simulation adapter is connected.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(explanation);

        AddSimulationAdapterButton(root, new IdentityTraceSimulationAdapter(), "Run identity comparison");
        AddSimulationAdapterButton(root, new InjectedMismatchSimulationAdapter(), "Run injected mismatch test");

        var closeRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(closeRow);

        var closeButton = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(90, 34)
        };
        closeButton.Pressed += compareWindow.QueueFree;
        compareWindow.CloseRequested += compareWindow.QueueFree;
        closeRow.AddChild(closeButton);

        AddChild(compareWindow);
        compareWindow.PopupCentered(new Vector2I(540, 300));
    }

    private void AddSimulationAdapterButton(VBoxContainer root, IEnemySimulationAdapter adapter, string text)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = adapter.Description,
            CustomMinimumSize = new Vector2(240, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        button.Pressed += () => RunComparison(adapter);
        root.AddChild(button);
    }

    private void RunComparison(IEnemySimulationAdapter adapter)
    {
        SimulationAdapterResult simulationResult = adapter.Run(_frames);
        TraceComparisonResult result = TraceComparisonRunner.Compare(_frames, simulationResult.Frames);

        Log($"Comparison [{adapter.Name}]: comparedFrames={result.ComparedFrameCount}, mismatches={result.MismatchCount}");

        if (!string.IsNullOrWhiteSpace(simulationResult.Summary))
            Log($"Simulation source: {simulationResult.Summary}");

        if (!result.HasMismatch || result.FirstMismatch == null)
        {
            Log(adapter.ExpectedToMismatch
                ? "Comparison result: no mismatch found, but this adapter expected one."
                : "Comparison result: no mismatch. Pipeline is valid.");
            return;
        }

        TraceMismatch mismatch = result.FirstMismatch;
        Log($"First mismatch: frameIndex={mismatch.FrameIndex}, tick={mismatch.Tick}, kind={mismatch.Kind}, field={mismatch.Field}, expected={mismatch.Expected}, actual={mismatch.Actual}");

        if (mismatch.FrameIndex >= 0 && mismatch.FrameIndex < _frames.Count)
        {
            _currentFrameIndex = mismatch.FrameIndex;
            _isRunning = true;
            _isPaused = true;
            _playbackAccumulator = 0;
            ApplyCurrentFrame();
            UpdateStatus();
        }
    }

    private void OnDumpFramePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        ShowDumpWindow(_currentFrameIndex);
    }

    private void ShowDumpWindow(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _frames.Count)
            return;

        EnemyTraceFrame frame = _frames[frameIndex];

        var dumpWindow = new Window
        {
            Title = $"MAME trace dump - tick {frame.frame}",
            Transient = false,
            Exclusive = false,
            Unresizable = false,
            MinSize = new Vector2I(620, 420)
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        dumpWindow.AddChild(margin);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var textEdit = new TextEdit
        {
            Text = BuildFrameDump(frameIndex),
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddChild(textEdit);

        var buttonRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(buttonRow);

        var closeButton = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(90, 34)
        };
        closeButton.Pressed += dumpWindow.QueueFree;
        dumpWindow.CloseRequested += dumpWindow.QueueFree;
        buttonRow.AddChild(closeButton);

        AddChild(dumpWindow);

        Vector2 viewportSize = GetViewportRect().Size;
        int width = Math.Clamp((int)viewportSize.X - 80, 720, 1100);
        int height = Math.Clamp((int)viewportSize.Y - 80, 520, 780);

        dumpWindow.PopupCentered(new Vector2I(width, height));
        Log($"Opened frame dump window for tick {frame.frame}.");
    }

    private string BuildFrameDump(int frameIndex)
    {
        var sb = new StringBuilder();

        if (frameIndex < 0 || frameIndex >= _frames.Count)
            return string.Empty;

        EnemyTraceFrame frame = _frames[frameIndex];

        sb.AppendLine("---- current frame diagnostic dump ----");
        sb.AppendLine($"Index={frameIndex + 1}/{_frames.Count}");
        sb.AppendLine($"tick={frame.frame}");
        sb.AppendLine($"schema={frame.schema}");
        sb.AppendLine($"phase={frame.phase}");
        sb.AppendLine($"mameFrame={frame.mameFrame}");
        sb.AppendLine($"pc={frame.pc}");
        sb.AppendLine($"r={frame.r}");
        sb.AppendLine();

        AppendPlayerDump(sb, frame.player);
        AppendEnemiesDump(sb, frame);
        AppendGatesDump(sb, frame);
        AppendGateChangesDump(sb, frameIndex);
        AppendEnemyWorkDump(sb, frame.enemyWork);
        AppendTimersDump(sb, frame.timers);
        AppendPortsDump(sb, frame.ports);
        sb.AppendLine(BuildRawMemorySummary(frame));
        sb.AppendLine("---------------------------------------");

        return sb.ToString();
    }

    private void AppendPlayerDump(StringBuilder sb, EnemyTraceActor? player)
    {
        if (player == null)
        {
            sb.AppendLine("Player: none");
            sb.AppendLine();
            return;
        }

        string target = player.HasTurnTarget
            ? $" target=({player.turnTargetX:X2},{player.turnTargetY:X2}) targetGodot=({player.turnTargetX:X2},{MameTraceCoordinates.MameToGodotArcadeY(player.turnTargetY):X2})"
            : string.Empty;

        sb.AppendLine("Player:");
        sb.AppendLine($"  raw={player.raw:X2} active={player.active}");
        sb.AppendLine($"  mame=({player.x:X2},{player.y:X2}) godot=({player.x:X2},{MameTraceCoordinates.MameToGodotArcadeY(player.y):X2})");
        sb.AppendLine($"  dir={player.dir} sprite={player.sprite:X2} attr={player.attr:X2}{target}");
        sb.AppendLine();
    }

    private void AppendEnemiesDump(StringBuilder sb, EnemyTraceFrame frame)
    {
        if (frame.enemies == null || frame.enemies.Count == 0)
        {
            sb.AppendLine("Enemies: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Enemies: {frame.enemies.Count} slot records, active={CountActiveEnemies(frame)}, known={CountKnownEnemies(frame)}");

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            string activeFlag = enemy.active ? "active" : "inactive";
            string knownFlag = enemy.HasKnownPosition ? "known" : "empty";
            sb.AppendLine($"  E{enemy.slot}: {activeFlag}/{knownFlag} raw={enemy.raw:X2} mame=({enemy.x:X2},{enemy.y:X2}) godot=({enemy.x:X2},{MameTraceCoordinates.MameToGodotArcadeY(enemy.y):X2}) dir={enemy.dir} sprite={enemy.sprite:X2} attr={enemy.attr:X2}");
        }

        sb.AppendLine();
    }

    private void AppendGatesDump(StringBuilder sb, EnemyTraceFrame frame)
    {
        if (frame.gates == null || frame.gates.Count == 0)
        {
            sb.AppendLine("Gates: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Gates: {frame.gates.Count}");

        int max = Math.Min(frame.gates.Count, 24);
        for (int i = 0; i < max; i++)
        {
            EnemyTraceGateState gate = frame.gates[i];
            sb.AppendLine($"  Gate {gate.gate_id}: {gate.orientation} pivot=({gate.pivot_x},{gate.pivot_y})");
        }

        if (frame.gates.Count > max)
            sb.AppendLine($"  ... {frame.gates.Count - max} more gates not shown");

        sb.AppendLine();
    }

    private void AppendGateChangesDump(StringBuilder sb, int frameIndex)
    {
        List<GateChange> changes = GetGateChangesFromPreviousFrame(frameIndex);

        if (changes.Count == 0)
        {
            sb.AppendLine("Gate changes from previous frame: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Gate changes from previous frame: {changes.Count}");
        foreach (GateChange change in changes)
        {
            sb.AppendLine($"  Gate {change.GateId}: {change.PreviousOrientation} -> {change.CurrentOrientation} pivot=({change.PivotX},{change.PivotY})");
        }

        sb.AppendLine();
    }

    private string BuildGateChangeSummary(int frameIndex)
    {
        if (frameIndex <= 0 || frameIndex >= _frames.Count)
            return "Gate changes: none";

        EnemyTraceFrame frame = _frames[frameIndex];
        List<GateChange> changes = GetGateChangesFromPreviousFrame(frameIndex);

        if (changes.Count == 0)
            return $"Gate changes at tick {frame.frame}: none";

        var parts = new List<string>(changes.Count);
        foreach (GateChange change in changes)
            parts.Add($"G{change.GateId}:{change.PreviousOrientation}->{change.CurrentOrientation}");

        return $"Gate changes at tick {frame.frame}: {string.Join(", ", parts)}";
    }

    private List<GateChange> GetGateChangesFromPreviousFrame(int frameIndex)
    {
        var changes = new List<GateChange>();

        if (frameIndex <= 0 || frameIndex >= _frames.Count)
            return changes;

        List<EnemyTraceGateState>? previousGates = _frames[frameIndex - 1].gates;
        List<EnemyTraceGateState>? currentGates = _frames[frameIndex].gates;

        if (previousGates == null || currentGates == null)
            return changes;

        var previousById = new Dictionary<int, EnemyTraceGateState>();
        foreach (EnemyTraceGateState previousGate in previousGates)
            previousById[previousGate.gate_id] = previousGate;

        foreach (EnemyTraceGateState currentGate in currentGates)
        {
            if (!previousById.TryGetValue(currentGate.gate_id, out EnemyTraceGateState? previousGate))
                continue;

            string previousOrientation = previousGate.orientation ?? string.Empty;
            string currentOrientation = currentGate.orientation ?? string.Empty;

            if (string.Equals(previousOrientation, currentOrientation, StringComparison.OrdinalIgnoreCase))
                continue;

            changes.Add(new GateChange(
                currentGate.gate_id,
                previousOrientation,
                currentOrientation,
                currentGate.pivot_x,
                currentGate.pivot_y));
        }

        return changes;
    }

    private static void AppendEnemyWorkDump(StringBuilder sb, EnemyTraceEnemyWorkState? enemyWork)
    {
        if (enemyWork == null)
        {
            sb.AppendLine("EnemyWork: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("EnemyWork:");
        sb.AppendLine($"  tempDir={enemyWork.tempDir:X2} temp=({enemyWork.tempX:X2},{enemyWork.tempY:X2})");
        sb.AppendLine($"  rejected={enemyWork.rejectedMask:X2} fallback={enemyWork.fallbackMask:X2}");
        sb.AppendLine($"  preferred=[{FormatHexList(enemyWork.preferred)}]");
        sb.AppendLine($"  chaseTimers=[{FormatHexList(enemyWork.chaseTimers)}]");
        sb.AppendLine($"  roundRobin={enemyWork.chaseRoundRobin:X2}");
        sb.AppendLine();
    }

    private static void AppendTimersDump(StringBuilder sb, EnemyTraceTimersState? timers)
    {
        if (timers == null)
        {
            sb.AppendLine("Timers: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Timers:");
        sb.AppendLine($"  61B4={timers.timer61B4:X2} 61B5={timers.timer61B5:X2} 61B6={timers.timer61B6:X2}");
        sb.AppendLine($"  61B7={timers.timer61B7:X2} 61B8={timers.timer61B8:X2} 61B9={timers.timer61B9:X2}");
        sb.AppendLine($"  freeze61E1={timers.freeze61E1:X2} color6199={timers.collectibleColorCounter6199:X2}");
        sb.AppendLine();
    }

    private static void AppendPortsDump(StringBuilder sb, EnemyTracePortsState? ports)
    {
        if (ports == null)
        {
            sb.AppendLine("Ports: none");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Ports:");
        sb.AppendLine($"  IN0/9000={ports.in0_9000:X2}");
        sb.AppendLine($"  IN1/9001={ports.in1_9001:X2}");
        sb.AppendLine($"  DSW0/9002={ports.dsw0_9002:X2}");
        sb.AppendLine($"  DSW1/9003={ports.dsw1_9003:X2}");
        sb.AppendLine();
    }

    private void OnFindFramePressed()
    {
        if (_frames.Count == 0)
        {
            Log("No trace loaded.");
            return;
        }

        ShowFindFrameWindow();
    }

    private void ShowFindFrameWindow()
    {
        var findWindow = new Window
        {
            Title = "Trace navigation helpers",
            Transient = false,
            Exclusive = false,
            Unresizable = false,
            MinSize = new Vector2I(460, 300)
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        findWindow.AddChild(margin);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var explanation = new Label
        {
            Text = "Jump to useful frames in the loaded MAME trace.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(explanation);

        var slotRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        slotRow.AddChild(new Label
        {
            Text = "Enemy slot:",
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(110, 0)
        });

        var slotSpin = new SpinBox
        {
            MinValue = 0,
            MaxValue = 3,
            Step = 1,
            Rounded = true,
            Value = 0,
            CustomMinimumSize = new Vector2(90, 34)
        };
        slotRow.AddChild(slotSpin);
        root.AddChild(slotRow);

        var conditionRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        conditionRow.AddChild(new Label
        {
            Text = "Condition:",
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(110, 0)
        });

        var conditionOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        conditionOption.AddItem("enemyWork rejectedMask != 0");
        conditionOption.AddItem("enemyWork fallbackMask != 0");
        conditionOption.AddItem("enemyWork tempDir == value");
        conditionOption.AddItem("any active enemy dir == value");
        conditionOption.AddItem("slot dir == value");
        conditionOption.AddItem("slot active and dir == value");
        conditionOption.AddItem("player dir == value");
        conditionRow.AddChild(conditionOption);

        var conditionValue = new LineEdit
        {
            Text = "08",
            PlaceholderText = "hex / dir",
            CustomMinimumSize = new Vector2(90, 34)
        };
        conditionRow.AddChild(conditionValue);
        root.AddChild(conditionRow);

        var conditionButtonRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        var findConditionButton = new Button
        {
            Text = "Find condition",
            CustomMinimumSize = new Vector2(150, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        findConditionButton.Pressed += () =>
            JumpToConditionFrame(conditionOption.Selected, (int)slotSpin.Value, conditionValue.Text, startAfterCurrentFrame: false);

        var findNextConditionButton = new Button
        {
            Text = "Find next",
            CustomMinimumSize = new Vector2(120, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        findNextConditionButton.Pressed += () =>
            JumpToConditionFrame(conditionOption.Selected, (int)slotSpin.Value, conditionValue.Text, startAfterCurrentFrame: true);

        conditionButtonRow.AddChild(findConditionButton);
        conditionButtonRow.AddChild(findNextConditionButton);
        root.AddChild(conditionButtonRow);

        var buttonGrid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        buttonGrid.AddThemeConstantOverride("h_separation", 8);
        buttonGrid.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(buttonGrid);

        AddFindButton(buttonGrid, "First active enemy", () =>
            JumpToFoundFrame("first active enemy", FindFirstActiveEnemyFrame()));

        AddFindButton(buttonGrid, "First direction change", () =>
            JumpToFoundFrame("first direction change", FindFirstEnemyDirectionChangeFrame()));

        AddFindButton(buttonGrid, "First active for slot", () =>
            JumpToFoundFrame($"first active enemy for slot {(int)slotSpin.Value}", FindFirstActiveEnemyFrameForSlot((int)slotSpin.Value)));

        AddFindButton(buttonGrid, "Direction change for slot", () =>
            JumpToFoundFrame($"first direction change for slot {(int)slotSpin.Value}", FindFirstEnemyDirectionChangeFrameForSlot((int)slotSpin.Value)));

        AddFindButton(buttonGrid, "First gate change", () =>
            JumpToFirstGateChangeFrame());

        AddFindButton(buttonGrid, "Current frame dump", () =>
            ShowDumpWindow(_currentFrameIndex));

        var closeRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(closeRow);

        var closeButton = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(90, 34)
        };
        closeButton.Pressed += findWindow.QueueFree;
        findWindow.CloseRequested += findWindow.QueueFree;
        closeRow.AddChild(closeButton);

        AddChild(findWindow);
        findWindow.PopupCentered(new Vector2I(620, 520));
    }

    private void JumpToFirstGateChangeFrame()
    {
        int frameIndex = FindFirstGateChangeFrame();
        JumpToFoundFrame("first gate change", frameIndex);

        if (frameIndex >= 0)
            Log(BuildGateChangeSummary(frameIndex));
    }

    private void JumpToConditionFrame(int conditionIndex, int slot, string valueText, bool startAfterCurrentFrame)
    {
        int frameIndex = FindConditionFrame(conditionIndex, slot, valueText, startAfterCurrentFrame, out string description);

        if (frameIndex < 0)
        {
            Log($"Find: {description} not found.");
            return;
        }

        JumpToFoundFrame(description, frameIndex);
    }

    private int FindConditionFrame(int conditionIndex, int slot, string valueText, bool startAfterCurrentFrame, out string description)
    {
        int startIndex = startAfterCurrentFrame
            ? Math.Min(_currentFrameIndex + 1, _frames.Count)
            : 0;

        description = conditionIndex switch
        {
            0 => "enemyWork rejectedMask != 0",
            1 => "enemyWork fallbackMask != 0",
            2 => $"enemyWork tempDir == {valueText}",
            3 => $"any active enemy dir == {valueText}",
            4 => $"slot {slot} dir == {valueText}",
            5 => $"slot {slot} active and dir == {valueText}",
            6 => $"player dir == {valueText}",
            _ => "unknown condition"
        };

        for (int i = startIndex; i < _frames.Count; i++)
        {
            if (FrameMatchesCondition(_frames[i], conditionIndex, slot, valueText))
                return i;
        }

        return -1;
    }

    private static bool FrameMatchesCondition(EnemyTraceFrame frame, int conditionIndex, int slot, string valueText)
    {
        return conditionIndex switch
        {
            0 => frame.enemyWork?.rejectedMask > 0,
            1 => frame.enemyWork?.fallbackMask > 0,
            2 => ByteValueMatches(frame.enemyWork?.tempDir, valueText),
            3 => AnyActiveEnemyDirectionMatches(frame, valueText),
            4 => EnemySlotDirectionMatches(frame, slot, valueText, requireActive: false),
            5 => EnemySlotDirectionMatches(frame, slot, valueText, requireActive: true),
            6 => DirectionStringMatches(frame.player?.dir, valueText),
            _ => false
        };
    }

    private static bool AnyActiveEnemyDirectionMatches(EnemyTraceFrame frame, string valueText)
    {
        if (frame.enemies == null)
            return false;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.active && DirectionStringMatches(enemy.dir, valueText))
                return true;
        }

        return false;
    }

    private static bool EnemySlotDirectionMatches(EnemyTraceFrame frame, int slot, string valueText, bool requireActive)
    {
        EnemyTraceActor? enemy = FindEnemyBySlot(frame, slot);

        if (enemy == null)
            return false;

        if (requireActive && !enemy.active)
            return false;

        return DirectionStringMatches(enemy.dir, valueText);
    }

    private static bool DirectionStringMatches(string? actualDirection, string valueText)
    {
        if (string.IsNullOrWhiteSpace(actualDirection))
            return false;

        string actual = actualDirection.Trim();
        string expected = valueText.Trim();

        if (TryParseTraceByte(actual, out int actualByte) &&
            TryParseTraceByte(expected, out int expectedByte))
        {
            return actualByte == expectedByte;
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ByteValueMatches(int? actualValue, string valueText)
    {
        if (!actualValue.HasValue || actualValue.Value < 0)
            return false;

        return TryParseTraceByte(valueText, out int expectedValue) &&
               actualValue.Value == expectedValue;
    }

    private static bool TryParseTraceByte(string? text, out int value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value);

        bool looksHex = trimmed.Length <= 2;
        foreach (char c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
        }

        if (looksHex)
            return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out value);

        return int.TryParse(trimmed, out value);
    }

    private static void AddFindButton(GridContainer grid, string text, Action handler)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(210, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        button.Pressed += handler;
        grid.AddChild(button);
    }

    private void JumpToFoundFrame(string description, int frameIndex)
    {
        if (frameIndex < 0)
        {
            Log($"Find: {description} not found.");
            return;
        }

        _currentFrameIndex = frameIndex;
        _isRunning = true;
        _isPaused = true;
        _playbackAccumulator = 0;

        ApplyCurrentFrame();
        UpdateStatus();

        EnemyTraceFrame frame = _frames[_currentFrameIndex];
        Log($"Find: {description} -> index={frameIndex}, tick={frame.frame}");
    }

    private int FindFirstActiveEnemyFrame()
    {
        for (int i = 0; i < _frames.Count; i++)
        {
            if (CountActiveEnemies(_frames[i]) > 0)
                return i;
        }

        return -1;
    }

    private int FindFirstActiveEnemyFrameForSlot(int slot)
    {
        for (int i = 0; i < _frames.Count; i++)
        {
            EnemyTraceActor? enemy = FindEnemyBySlot(_frames[i], slot);
            if (enemy is { active: true })
                return i;
        }

        return -1;
    }

    private int FindFirstEnemyDirectionChangeFrame()
    {
        var previousDirections = new Dictionary<int, string?>();

        for (int i = 0; i < _frames.Count; i++)
        {
            EnemyTraceFrame frame = _frames[i];
            if (frame.enemies == null)
                continue;

            foreach (EnemyTraceActor enemy in frame.enemies)
            {
                if (!enemy.active)
                    continue;

                if (previousDirections.TryGetValue(enemy.slot, out string? previousDirection) &&
                    !string.Equals(previousDirection, enemy.dir, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                previousDirections[enemy.slot] = enemy.dir;
            }
        }

        return -1;
    }

    private int FindFirstEnemyDirectionChangeFrameForSlot(int slot)
    {
        string? previousDirection = null;
        bool hasPrevious = false;

        for (int i = 0; i < _frames.Count; i++)
        {
            EnemyTraceActor? enemy = FindEnemyBySlot(_frames[i], slot);
            if (enemy is not { active: true })
                continue;

            if (hasPrevious &&
                !string.Equals(previousDirection, enemy.dir, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }

            previousDirection = enemy.dir;
            hasPrevious = true;
        }

        return -1;
    }

    private int FindFirstGateChangeFrame()
    {
        for (int i = 1; i < _frames.Count; i++)
        {
            if (GetGateChangesFromPreviousFrame(i).Count > 0)
                return i;
        }

        return -1;
    }

    private static EnemyTraceActor? FindEnemyBySlot(EnemyTraceFrame frame, int slot)
    {
        if (frame.enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.slot == slot)
                return enemy;
        }

        return null;
    }

    private readonly record struct GateChange(
        int GateId,
        string PreviousOrientation,
        string CurrentOrientation,
        int PivotX,
        int PivotY);

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
        SyncTickSpinBoxToCurrentFrame();
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
        int activeEnemies = CountActiveEnemies(frame);
        _statusLabel.Text = $"Frame {_currentFrameIndex + 1}/{_frames.Count} | Tick {frame.frame} | Ennemis actifs {activeEnemies} | {state}";
        _statusLabel.TooltipText = _statusLabel.Text;
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

internal sealed class MameSettingsEditorFields
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
    public CheckBox ExitWhenDone { get; init; } = null!;
    public CheckBox PauseWhenDone { get; init; } = null!;
    public CheckBox FlushEveryTraceLine { get; init; } = null!;
    public CheckBox IncludeFullMemoryEachFrame { get; init; } = null!;
    public CheckBox IncludeLogicalMazeEachFrame { get; init; } = null!;
}
