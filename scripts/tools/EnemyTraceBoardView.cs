using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Lightweight debug renderer for one side of the enemy trace comparison.
/// It draws the static logical maze, pivoting gates from the trace, the player, and enemies.
///
/// v0.7.04 adds a small single-enemy visual diagnostic overlay.  The overlay is intentionally
/// read-only: it highlights slot 0, draws a recent trail while the replay advances, and shows
/// the current slot/work RAM values.  It does not affect comparison logic or simulation state.
/// </summary>
public partial class EnemyTraceBoardView : Control
{
    private const int DefaultMazeWidth = 11;
    private const int DefaultMazeHeight = 11;
    private const float ArcadeCellSize = 16.0f;

    private const string PlayerSpriteSheetPath = "res://assets/sprites/player/ladybug_spritesheet.png";
    private static readonly Vector2 PlayerSpriteFrameSize = new(64.0f, 64.0f);

    // Debug-render tuning only. This does not change the traced gameplay coordinate.
    // The sprite is deliberately drawn above the raw gameplay anchor, while the white
    // cross keeps showing the exact x/y read from MAME.
    private static readonly Vector2 DefaultPlayerSpriteVisualOffsetArcade = new(0.0f, 2.0f);
    private Vector2 _playerSpriteVisualOffsetArcade = DefaultPlayerSpriteVisualOffsetArcade;

    // The source player frames are 64x64. For the debug board we intentionally draw
    // them at 32x32 pixels, i.e. an exact 1/2 scale, then snap the destination to
    // whole screen pixels. This avoids Photoshop-visible blur from arbitrary scaling.
    private const float PlayerSpriteDebugDisplaySizePixels = 32.0f;

    private const string EnemyLevel1SpriteSheetPath = "res://assets/sprites/enemies/enemy_level1.png";
    private static readonly Vector2 EnemySpriteFrameSize = new(64.0f, 64.0f);

    // Level-1 enemy graphics use the same six-frame layout as the main Lady Bug project:
    // frames 0,1,2 = move_right; frames 3,4,5 = move_up.
    // Left/down are drawn by mirroring those base rows.
    private const float EnemySpriteDebugDisplaySizePixels = 32.0f;

    // After converting MAME Y with the 0xDD mirror, the enemy's gameplay anchor is
    // almost centered in the debug board. A small positive Y visual offset places
    // the level-1 sprite closer to the middle of horizontal corridors.
    private static readonly Vector2 EnemySpriteVisualOffsetArcade = new(0.0f, 1.0f);

    private const int FocusEnemySlot = 0;
    private const int FocusTrailMaxPoints = 80;
    private const int FocusTrailBreakThresholdTicks = 4;
    private static readonly Vector2 DenReleaseMarkerMame = new(0x58, 0x86);

    private int _mazeWidth = DefaultMazeWidth;
    private int _mazeHeight = DefaultMazeHeight;
    private int[] _wallMasks = new int[DefaultMazeWidth * DefaultMazeHeight];
    private EnemyTraceFrame? _snapshot;

    private Texture2D? _playerSpriteSheet;
    private bool _playerSpriteLoadAttempted;
    private Texture2D? _enemyLevel1SpriteSheet;
    private bool _enemySpriteLoadAttempted;
    private bool _showPlayerDebugMarkers;
    private bool _showInactiveEnemySlots;
    private bool _showSingleEnemyFocus = true;
    private int _lastFocusedSnapshotTick = int.MinValue;
    private readonly List<FocusedEnemyTrailPoint> _focusedEnemyTrail = new();

    public string BoardTitle { get; set; } = "Board";

    public Vector2 PlayerSpriteVisualOffsetArcade => _playerSpriteVisualOffsetArcade;

    public bool ShowPlayerDebugMarkers => _showPlayerDebugMarkers;

    public bool ShowInactiveEnemySlots => _showInactiveEnemySlots;

    public bool ShowSingleEnemyFocus => _showSingleEnemyFocus;

    public void SetShowInactiveEnemySlots(bool show)
    {
        _showInactiveEnemySlots = show;
        QueueRedraw();
    }

    public void ToggleInactiveEnemySlots()
    {
        SetShowInactiveEnemySlots(!_showInactiveEnemySlots);
    }

    public void SetShowPlayerDebugMarkers(bool show)
    {
        _showPlayerDebugMarkers = show;
        QueueRedraw();
    }

    public void TogglePlayerDebugMarkers()
    {
        SetShowPlayerDebugMarkers(!_showPlayerDebugMarkers);
    }

    public void SetShowSingleEnemyFocus(bool show)
    {
        _showSingleEnemyFocus = show;
        QueueRedraw();
    }

    public void ToggleSingleEnemyFocus()
    {
        SetShowSingleEnemyFocus(!_showSingleEnemyFocus);
    }

    public void SetPlayerSpriteVisualOffsetArcade(Vector2 offsetArcade)
    {
        _playerSpriteVisualOffsetArcade = offsetArcade;
        QueueRedraw();
    }

    public void NudgePlayerSpriteVisualOffsetArcade(Vector2 deltaArcade)
    {
        SetPlayerSpriteVisualOffsetArcade(_playerSpriteVisualOffsetArcade + deltaArcade);
    }

    public void ResetPlayerSpriteVisualOffsetArcade()
    {
        SetPlayerSpriteVisualOffsetArcade(DefaultPlayerSpriteVisualOffsetArcade);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Keep pixel art sharp when Control._Draw() renders the spritesheet.
        // This is especially important because the simulator board is used for
        // visual comparison against MAME screenshots.
        TextureFilter = TextureFilterEnum.Nearest;

        EnsurePlayerSpriteLoaded();
        EnsureEnemySpriteLoaded();
        QueueRedraw();
    }

    public void LoadMazeFromResource(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"Maze file not found: {path}");
            QueueRedraw();
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"Could not open maze file: {path}");
            QueueRedraw();
            return;
        }

        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(file.GetAsText());
            System.Text.Json.JsonElement root = document.RootElement;
            _mazeWidth = root.GetProperty("width").GetInt32();
            _mazeHeight = root.GetProperty("height").GetInt32();

            System.Text.Json.JsonElement cells = root.GetProperty("cells");
            _wallMasks = new int[_mazeWidth * _mazeHeight];

            int index = 0;
            foreach (System.Text.Json.JsonElement cell in cells.EnumerateArray())
            {
                if (index >= _wallMasks.Length)
                    break;

                _wallMasks[index++] = cell.GetInt32();
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Maze JSON parse error: {ex.Message}");
        }

        QueueRedraw();
    }

    public void SetSnapshot(EnemyTraceFrame snapshot)
    {
        _snapshot = snapshot;
        UpdateFocusedEnemyTrail(snapshot);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        DrawString(font, new Vector2(8, 18), BoardTitle, HorizontalAlignment.Left, -1, 16, Colors.White);

        Rect2 boardRect = ComputeBoardRect();
        DrawRect(boardRect, new Color(0.025f, 0.025f, 0.035f, 1.0f), true);
        DrawRect(boardRect, new Color(0.20f, 0.20f, 0.30f, 1.0f), false, 2.0f);

        if (_showPlayerDebugMarkers)
        {
            string debugMarkerText =
                $"debug player | offset=({_playerSpriteVisualOffsetArcade.X:0},{_playerSpriteVisualOffsetArcade.Y:0}) | P raw | T target";
            DrawString(font, boardRect.Position + new Vector2(8, 18), debugMarkerText, HorizontalAlignment.Left, -1, 12, new Color(0.20f, 0.95f, 1.0f, 1.0f));
        }

        if (_showInactiveEnemySlots)
        {
            DrawString(font, boardRect.Position + new Vector2(8, _showPlayerDebugMarkers ? 34 : 18), "debug enemies: inactive known slots visible", HorizontalAlignment.Left, -1, 12, new Color(0.75f, 0.75f, 1.0f, 1.0f));
        }

        if (_mazeWidth <= 0 || _mazeHeight <= 0)
            return;

        float cell = Mathf.Min(boardRect.Size.X / _mazeWidth, boardRect.Size.Y / _mazeHeight);
        Vector2 origin = boardRect.Position + new Vector2(
            (boardRect.Size.X - cell * _mazeWidth) * 0.5f,
            (boardRect.Size.Y - cell * _mazeHeight) * 0.5f);

        DrawGrid(origin, cell);
        DrawMazeWalls(origin, cell);
        DrawGates(origin, cell);
        DrawFocusedEnemyTrail(origin, cell);
        DrawActors(origin, cell);
        DrawFocusedEnemyMarker(origin, cell);
        DrawFocusedEnemyHud(boardRect, font);
    }

    private void EnsurePlayerSpriteLoaded()
    {
        if (_playerSpriteLoadAttempted)
            return;

        _playerSpriteLoadAttempted = true;

        if (!ResourceLoader.Exists(PlayerSpriteSheetPath))
            return;

        _playerSpriteSheet = ResourceLoader.Load<Texture2D>(PlayerSpriteSheetPath);
    }

    private void EnsureEnemySpriteLoaded()
    {
        if (_enemySpriteLoadAttempted)
            return;

        _enemySpriteLoadAttempted = true;

        if (!ResourceLoader.Exists(EnemyLevel1SpriteSheetPath))
            return;

        _enemyLevel1SpriteSheet = ResourceLoader.Load<Texture2D>(EnemyLevel1SpriteSheetPath);
    }

    private Rect2 ComputeBoardRect()
    {
        const float titleHeight = 28.0f;
        const float margin = 8.0f;

        return new Rect2(
            margin,
            titleHeight,
            Mathf.Max(1.0f, Size.X - margin * 2.0f),
            Mathf.Max(1.0f, Size.Y - titleHeight - margin));
    }

    private void DrawGrid(Vector2 origin, float cell)
    {
        var gridColor = new Color(0.16f, 0.16f, 0.22f, 1.0f);

        for (int x = 0; x <= _mazeWidth; x++)
        {
            float px = origin.X + x * cell;
            DrawLine(new Vector2(px, origin.Y), new Vector2(px, origin.Y + _mazeHeight * cell), gridColor, 1.0f);
        }

        for (int y = 0; y <= _mazeHeight; y++)
        {
            float py = origin.Y + y * cell;
            DrawLine(new Vector2(origin.X, py), new Vector2(origin.X + _mazeWidth * cell, py), gridColor, 1.0f);
        }
    }

    private void DrawMazeWalls(Vector2 origin, float cell)
    {
        var wallColor = new Color(0.55f, 0.20f, 0.95f, 1.0f);
        const float wallThickness = 3.0f;

        for (int y = 0; y < _mazeHeight; y++)
        {
            for (int x = 0; x < _mazeWidth; x++)
            {
                int mask = GetWallMask(x, y);
                Vector2 topLeft = origin + new Vector2(x * cell, y * cell);
                Vector2 topRight = topLeft + new Vector2(cell, 0);
                Vector2 bottomLeft = topLeft + new Vector2(0, cell);
                Vector2 bottomRight = topLeft + new Vector2(cell, cell);

                // WallFlags in the current project: Up=1, Down=2, Left=4, Right=8.
                if ((mask & 1) != 0)
                    DrawLine(topLeft, topRight, wallColor, wallThickness);
                if ((mask & 2) != 0)
                    DrawLine(bottomLeft, bottomRight, wallColor, wallThickness);
                if ((mask & 4) != 0)
                    DrawLine(topLeft, bottomLeft, wallColor, wallThickness);
                if ((mask & 8) != 0)
                    DrawLine(topRight, bottomRight, wallColor, wallThickness);
            }
        }
    }

    private int GetWallMask(int x, int y)
    {
        int index = y * _mazeWidth + x;
        if (index < 0 || index >= _wallMasks.Length)
            return 0;

        return _wallMasks[index];
    }

    private void DrawGates(Vector2 origin, float cell)
    {
        if (_snapshot?.gates == null)
            return;

        Color gateColor = new(0.20f, 1.0f, 0.20f, 1.0f);
        Color gateOutline = new(0.02f, 0.18f, 0.02f, 1.0f);

        foreach (EnemyTraceGateState gate in _snapshot.gates)
        {
            if (gate.pivot_x < 0 || gate.pivot_y < 0)
                continue;

            // GatePivot is a logical maze pivot/intersection, not the top-left of a cell.
            // A Lady Bug gate spans two logical cells and is centered on this pivot.
            // The previous debug renderer used (pivot + 0.5 cell), which made gates look
            // one-cell wide and shifted down/right.
            Vector2 center = origin + new Vector2(gate.pivot_x * cell, gate.pivot_y * cell);
            bool isHorizontal = string.Equals(gate.orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
            bool isVertical = string.Equals(gate.orientation, "Vertical", StringComparison.OrdinalIgnoreCase);

            float thickness = Mathf.Max(4.0f, cell * 0.12f);
            Vector2 size;
            if (isHorizontal)
                size = new Vector2(cell * 2.0f, thickness);
            else if (isVertical)
                size = new Vector2(thickness, cell * 2.0f);
            else
                size = new Vector2(cell * 0.30f, cell * 0.30f);

            Rect2 rect = new(center - size * 0.5f, size);
            DrawRect(rect, gateColor, true);
            DrawRect(rect, gateOutline, false, 1.0f);
        }
    }

    private void DrawActors(Vector2 origin, float cell)
    {
        if (_snapshot == null)
            return;

        if (_snapshot.player != null && _snapshot.player.active)
            DrawPlayer(origin, cell, _snapshot.player);

        if (_snapshot.enemies == null)
            return;

        foreach (EnemyTraceActor enemy in _snapshot.enemies)
        {
            if (!enemy.active)
            {
                if (!_showInactiveEnemySlots || !enemy.HasKnownPosition)
                    continue;
            }

            DrawEnemy(origin, cell, enemy);
        }
    }

    private void DrawEnemy(Vector2 origin, float cell, EnemyTraceActor actor)
    {
        EnsureEnemySpriteLoaded();

        if (_enemyLevel1SpriteSheet == null)
        {
            string label = actor.slot.ToString();
            float alpha = actor.active ? 1.0f : 0.55f;
            DrawDebugActor(origin, cell, actor, new Color(0.20f, 0.85f, 1.0f, alpha), label, false);
            return;
        }

        Vector2 rawCenter = ArcadePointToBoard(origin, cell, actor.x, actor.y);
        EnemySpriteFrame frame = ResolveEnemyFrame(actor.dir, actor.slot);
        Vector2 spriteCenter = rawCenter + ArcadeDeltaToBoard(cell, EnemySpriteVisualOffsetArcade);

        float size = EnemySpriteDebugDisplaySizePixels;
        Vector2 destinationPosition = spriteCenter - new Vector2(size, size) * 0.5f;
        destinationPosition = new Vector2(Mathf.Round(destinationPosition.X), Mathf.Round(destinationPosition.Y));

        Rect2 destination = new(destinationPosition, new Vector2(size, size));
        Rect2 source = new(frame.SourcePosition, EnemySpriteFrameSize);
        Color modulate = actor.active ? Colors.White : new Color(1.0f, 1.0f, 1.0f, 0.55f);

        DrawSpriteRegion(_enemyLevel1SpriteSheet, destination, source, frame.FlipH, frame.FlipV, modulate);
    }

    private void DrawPlayer(Vector2 origin, float cell, EnemyTraceActor actor)
    {
        EnsurePlayerSpriteLoaded();

        Vector2 rawCenter = ArcadePointToBoard(origin, cell, actor.x, actor.y);

        if (_playerSpriteSheet != null)
            DrawPlayerSprite(origin, cell, actor, rawCenter);
        else
            DrawDebugActor(origin, cell, actor, new Color(1.0f, 0.25f, 0.15f, 1.0f), "P", true);

        if (!_showPlayerDebugMarkers)
            return;

        // Draw the gameplay debug markers last, above the sprite.
        // They are intentionally oversized and outlined because the player sprite
        // has bright red/yellow pixels that can easily hide a thin white cross.
        if (actor.HasTurnTarget)
        {
            Vector2 targetCenter = ArcadePointToBoard(origin, cell, actor.turnTargetX, actor.turnTargetY);
            DrawLine(rawCenter, targetCenter, Colors.Black, 4.0f);
            DrawLine(rawCenter, targetCenter, new Color(1.0f, 0.92f, 0.20f, 1.0f), 2.0f);
            DrawDebugMarker(targetCenter, new Color(1.0f, 0.92f, 0.20f, 1.0f), "T", cell);
        }

        DrawDebugMarker(rawCenter, new Color(0.20f, 0.95f, 1.0f, 1.0f), "P", cell);
    }

    private void DrawPlayerSprite(Vector2 origin, float cell, EnemyTraceActor actor, Vector2 rawCenter)
    {
        if (_playerSpriteSheet == null)
            return;

        PlayerSpriteFrame frame = ResolvePlayerFrame(actor.dir);
        Vector2 spriteCenter = rawCenter + ArcadeDeltaToBoard(cell, _playerSpriteVisualOffsetArcade);

        float size = PlayerSpriteDebugDisplaySizePixels;
        Vector2 destinationPosition = spriteCenter - new Vector2(size, size) * 0.5f;
        destinationPosition = new Vector2(Mathf.Round(destinationPosition.X), Mathf.Round(destinationPosition.Y));

        Rect2 destination = new(destinationPosition, new Vector2(size, size));
        Rect2 source = new(frame.SourcePosition, PlayerSpriteFrameSize);

        DrawSpriteRegion(_playerSpriteSheet, destination, source, Colors.White, frame.FlipH, frame.FlipV);
    }

    private void DrawSpriteRegion(Texture2D texture, Rect2 destination, Rect2 source, Color modulate, bool flipH, bool flipV)
    {
        DrawSpriteRegion(texture, destination, source, flipH, flipV, modulate);
    }

    private void DrawSpriteRegion(Texture2D texture, Rect2 destination, Rect2 source, bool flipH, bool flipV, Color modulate)
    {
        if (!flipH && !flipV)
        {
            DrawTextureRectRegion(texture, destination, source, modulate, false, true);
            return;
        }

        Vector2 center = destination.GetCenter();
        Vector2 size = destination.Size;
        Rect2 localDestination = new(-size * 0.5f, size);

        DrawSetTransform(center, 0.0f, new Vector2(flipH ? -1.0f : 1.0f, flipV ? -1.0f : 1.0f));
        DrawTextureRectRegion(texture, localDestination, source, modulate, false, true);
        DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
    }

    private void DrawDebugActor(
        Vector2 origin,
        float cell,
        EnemyTraceActor actor,
        Color color,
        string label,
        bool isPlayer)
    {
        Vector2 center = ArcadePointToBoard(origin, cell, actor.x, actor.y);

        float radius = Mathf.Clamp(cell * 0.18f, 5.0f, 12.0f);
        DrawCircle(center, radius, color);
        DrawArc(center, radius, 0.0f, Mathf.Tau, 32, Colors.Black, 2.0f);

        Font font = GetThemeDefaultFont();
        DrawString(font, center + new Vector2(-4, 5), label, HorizontalAlignment.Left, -1, 12, Colors.Black);

        Vector2 direction = isPlayer ? PlayerDirectionToVector(actor.dir) : EnemyDirectionToVector(actor.dir);
        if (direction != Vector2.Zero)
            DrawLine(center, center + direction * radius * 1.6f, Colors.White, 2.0f);
    }

    private void UpdateFocusedEnemyTrail(EnemyTraceFrame snapshot)
    {
        if (!_showSingleEnemyFocus)
            return;

        int tick = snapshot.frame;
        if (_lastFocusedSnapshotTick != int.MinValue)
        {
            if (tick < _lastFocusedSnapshotTick || tick - _lastFocusedSnapshotTick > FocusTrailBreakThresholdTicks)
                _focusedEnemyTrail.Clear();

            if (tick == _lastFocusedSnapshotTick)
                return;
        }

        _lastFocusedSnapshotTick = tick;

        EnemyTraceActor? focus = FindEnemyBySlot(snapshot, FocusEnemySlot);
        if (focus == null || !focus.active || !focus.HasKnownPosition)
            return;

        _focusedEnemyTrail.Add(new FocusedEnemyTrailPoint(tick, focus.x & 0xFF, focus.y & 0xFF, focus.raw & 0xFF, focus.dir));

        while (_focusedEnemyTrail.Count > FocusTrailMaxPoints)
            _focusedEnemyTrail.RemoveAt(0);
    }

    private void DrawFocusedEnemyTrail(Vector2 origin, float cell)
    {
        if (!_showSingleEnemyFocus)
            return;

        Vector2 denCenter = ArcadePointToBoard(origin, cell, (int)DenReleaseMarkerMame.X, (int)DenReleaseMarkerMame.Y);
        DrawDebugMarker(denCenter, new Color(0.45f, 0.95f, 1.0f, 1.0f), "D", cell);

        if (_focusedEnemyTrail.Count == 0)
            return;

        Color lineColor = new(1.0f, 0.84f, 0.18f, 0.58f);
        Color dotColor = new(1.0f, 0.84f, 0.18f, 0.82f);

        Vector2? previous = null;
        foreach (FocusedEnemyTrailPoint point in _focusedEnemyTrail)
        {
            Vector2 center = ArcadePointToBoard(origin, cell, point.X, point.Y);
            if (previous.HasValue)
                DrawLine(previous.Value, center, lineColor, 2.0f);

            DrawCircle(center, Mathf.Clamp(cell * 0.055f, 2.0f, 4.0f), dotColor);
            previous = center;
        }
    }

    private void DrawFocusedEnemyMarker(Vector2 origin, float cell)
    {
        if (!_showSingleEnemyFocus || _snapshot == null)
            return;

        EnemyTraceActor? focus = FindEnemyBySlot(_snapshot, FocusEnemySlot);
        if (focus == null || !focus.active || !focus.HasKnownPosition)
            return;

        Vector2 center = ArcadePointToBoard(origin, cell, focus.x, focus.y);
        float radius = Mathf.Clamp(cell * 0.34f, 10.0f, 22.0f);
        Color markerColor = new(1.0f, 0.84f, 0.18f, 1.0f);

        DrawArc(center, radius + 2.0f, 0.0f, Mathf.Tau, 40, Colors.Black, 4.0f);
        DrawArc(center, radius, 0.0f, Mathf.Tau, 40, markerColor, 2.0f);

        Vector2 direction = EnemyDirectionToVector(focus.dir);
        if (direction != Vector2.Zero)
        {
            DrawLine(center, center + direction * radius * 1.25f, Colors.Black, 5.0f);
            DrawLine(center, center + direction * radius * 1.25f, markerColor, 3.0f);
        }

        Font font = GetThemeDefaultFont();
        DrawString(font, center + new Vector2(radius + 4.0f, -radius), "E0", HorizontalAlignment.Left, -1, 13, markerColor);
    }

    private void DrawFocusedEnemyHud(Rect2 boardRect, Font font)
    {
        if (!_showSingleEnemyFocus || _snapshot == null)
            return;

        EnemyTraceActor? focus = FindEnemyBySlot(_snapshot, FocusEnemySlot);
        EnemyTraceEnemyWorkState? work = _snapshot.enemyWork;

        string activeText = focus == null
            ? "slot0: absent"
            : $"slot0: raw={Hex(focus.raw)} dir={DirLabel(focus.dir)} xy=({Hex(focus.x)},{Hex(focus.y)}) active={focus.active}";

        string workText = work == null
            ? "work: none"
            : $"work: tmp={Hex(work.tempDir)}:({Hex(work.tempX)},{Hex(work.tempY)}) rej={Hex(work.rejectedMask)} fb={Hex(work.fallbackMask)} pref=[{FormatPreferred(work.preferred)}]";

        string tickText = $"tick={_snapshot.frame} mameFrame={_snapshot.mameFrame}";
        string trailText = $"single-enemy release focus: E0 trail={_focusedEnemyTrail.Count}";

        const float padding = 8.0f;
        const float lineHeight = 15.0f;
        float panelWidth = Mathf.Min(430.0f, boardRect.Size.X - 16.0f);
        float panelHeight = padding * 2.0f + lineHeight * 4.0f;
        Vector2 panelPos = boardRect.Position + new Vector2(8.0f, boardRect.Size.Y - panelHeight - 8.0f);
        Rect2 panel = new(panelPos, new Vector2(panelWidth, panelHeight));

        DrawRect(panel, new Color(0.0f, 0.0f, 0.0f, 0.62f), true);
        DrawRect(panel, new Color(1.0f, 0.84f, 0.18f, 0.65f), false, 1.0f);

        Vector2 textPos = panel.Position + new Vector2(padding, padding + 11.0f);
        DrawString(font, textPos, trailText, HorizontalAlignment.Left, -1, 12, new Color(1.0f, 0.84f, 0.18f, 1.0f));
        DrawString(font, textPos + new Vector2(0, lineHeight), tickText, HorizontalAlignment.Left, -1, 12, Colors.White);
        DrawString(font, textPos + new Vector2(0, lineHeight * 2.0f), activeText, HorizontalAlignment.Left, -1, 12, Colors.White);
        DrawString(font, textPos + new Vector2(0, lineHeight * 3.0f), workText, HorizontalAlignment.Left, -1, 12, Colors.White);
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

    private static string FormatPreferred(List<int>? values)
    {
        if (values == null || values.Count == 0)
            return string.Empty;

        int count = Math.Min(values.Count, 4);
        var parts = new string[count];
        for (int i = 0; i < count; i++)
            parts[i] = Hex(values[i]);

        return string.Join(",", parts);
    }

    private static string Hex(int value)
    {
        return value < 0 ? "--" : (value & 0xFF).ToString("X2");
    }

    private static string DirLabel(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return "--";

        string lower = dir.ToLowerInvariant();
        return lower switch
        {
            "left" or "01" or "1" => "01/L",
            "up" or "08" or "8" => "08/U",
            "right" or "04" or "4" => "04/R",
            "down" or "02" or "2" => "02/D",
            _ => dir
        };
    }

    private Vector2 ArcadePointToBoard(Vector2 origin, float cell, int mameX, int mameY)
    {
        int godotArcadeY = MameTraceCoordinates.MameToGodotArcadeY(mameY);

        float localX = mameX / ArcadeCellSize * cell;
        float localY = godotArcadeY / ArcadeCellSize * cell;
        return origin + new Vector2(localX, localY);
    }

    private static Vector2 ArcadeDeltaToBoard(float cell, Vector2 arcadeDelta)
    {
        // Sprite visual offsets are authored in Godot-style arcade pixels.
        // They are not MAME RAM coordinates, so they are not mirrored here.
        return arcadeDelta / ArcadeCellSize * cell;
    }

    private void DrawDebugMarker(Vector2 center, Color color, string label, float cell)
    {
        float halfSize = Mathf.Clamp(cell * 0.20f, 7.0f, 12.0f);
        float thickness = 3.0f;

        // Black outline first, then the colored marker on top.
        DrawLine(center + new Vector2(-halfSize, 0), center + new Vector2(halfSize, 0), Colors.Black, thickness + 2.0f);
        DrawLine(center + new Vector2(0, -halfSize), center + new Vector2(0, halfSize), Colors.Black, thickness + 2.0f);
        DrawCircle(center, Mathf.Max(3.0f, halfSize * 0.42f), Colors.Black);

        DrawLine(center + new Vector2(-halfSize, 0), center + new Vector2(halfSize, 0), color, thickness);
        DrawLine(center + new Vector2(0, -halfSize), center + new Vector2(0, halfSize), color, thickness);
        DrawCircle(center, Mathf.Max(2.0f, halfSize * 0.30f), color);

        Font font = GetThemeDefaultFont();
        Vector2 labelPos = center + new Vector2(halfSize + 3.0f, -halfSize - 2.0f);
        DrawString(font, labelPos + new Vector2(1, 1), label, HorizontalAlignment.Left, -1, 12, Colors.Black);
        DrawString(font, labelPos, label, HorizontalAlignment.Left, -1, 12, color);
    }

    private static PlayerSpriteFrame ResolvePlayerFrame(string? dir)
    {
        // Player RAM direction encoding, as observed at 0x6198:
        // 01 = left, 02 = down, 04 = right, 08 = up.
        //
        // The debug spritesheet provides a right-facing base and an up-facing base.
        // Left and down are drawn by mirroring those base frames.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => new PlayerSpriteFrame(new Vector2(0, 0), true, false),
            "down" or "02" or "2" => new PlayerSpriteFrame(new Vector2(192, 0), false, true),
            "right" or "04" or "4" => new PlayerSpriteFrame(new Vector2(0, 0), false, false),
            "up" or "08" or "8" => new PlayerSpriteFrame(new Vector2(192, 0), false, false),
            _ => new PlayerSpriteFrame(new Vector2(192, 0), false, false)
        };
    }

    private EnemySpriteFrame ResolveEnemyFrame(string? dir, int slot)
    {
        int frameInAnimation = GetEnemyAnimationFrame(slot);
        float rightFrameX = frameInAnimation * EnemySpriteFrameSize.X;
        float upFrameX = (3 + frameInAnimation) * EnemySpriteFrameSize.X;

        // Raw vertical movement in the MAME trace is expressed in mirrored MAME Y.
        // Therefore 08 increases MAME Y and appears upward on the debug board,
        // while 02 decreases MAME Y and appears downward.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => new EnemySpriteFrame(new Vector2(rightFrameX, 0), true, false),
            "down" or "02" or "2" => new EnemySpriteFrame(new Vector2(upFrameX, 0), false, true),
            "right" or "04" or "4" => new EnemySpriteFrame(new Vector2(rightFrameX, 0), false, false),
            "up" or "08" or "8" => new EnemySpriteFrame(new Vector2(upFrameX, 0), false, false),
            _ => new EnemySpriteFrame(new Vector2(rightFrameX, 0), false, false)
        };
    }

    private int GetEnemyAnimationFrame(int slot)
    {
        int tick = _snapshot?.frame ?? 0;
        return Mathf.PosMod((tick / 8) + Mathf.Max(slot, 0), 3);
    }

    private static Vector2 PlayerDirectionToVector(string? dir)
    {
        // Player RAM direction encoding, as observed at 0x6198:
        // 01 = left, 02 = down, 04 = right, 08 = up.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => Vector2.Left,
            "down" or "02" or "2" => Vector2.Down,
            "right" or "04" or "4" => Vector2.Right,
            "up" or "08" or "8" => Vector2.Up,
            _ => Vector2.Zero
        };
    }

    private static Vector2 EnemyDirectionToVector(string? dir)
    {
        // Direction vector for the raw MAME trace after Y mirroring.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => Vector2.Left,
            "down" or "02" or "2" => Vector2.Down,
            "right" or "04" or "4" => Vector2.Right,
            "up" or "08" or "8" => Vector2.Up,
            _ => Vector2.Zero
        };
    }

    private readonly record struct PlayerSpriteFrame(Vector2 SourcePosition, bool FlipH, bool FlipV);
    private readonly record struct EnemySpriteFrame(Vector2 SourcePosition, bool FlipH, bool FlipV);
    private readonly record struct FocusedEnemyTrailPoint(int Tick, int X, int Y, int Raw, string? Dir);
}
