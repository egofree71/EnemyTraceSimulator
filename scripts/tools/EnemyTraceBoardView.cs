using Godot;
using System;

/// <summary>
/// Lightweight clean renderer for one side of the enemy trace comparison.
///
/// It draws only the maze, pivoting gates, the player, and enemies.  The old
/// single-enemy release overlay, den marker, E0 ring, trail, HUD, player cross,
/// and yellow turn-target line are intentionally removed from the normal board.
/// </summary>
public partial class EnemyTraceBoardView : Control
{
    private const int DefaultMazeWidth = 11;
    private const int DefaultMazeHeight = 11;
    private const float ArcadeCellSize = 16.0f;

    private const string PlayerSpriteSheetPath = "res://assets/sprites/player/ladybug_spritesheet.png";
    private static readonly Vector2 PlayerSpriteFrameSize = new(64.0f, 64.0f);
    private static readonly Vector2 DefaultPlayerSpriteVisualOffsetArcade = new(0.0f, 2.0f);
    private const float PlayerSpriteDebugDisplaySizePixels = 32.0f;

    private const string EnemyLevel1SpriteSheetPath = "res://assets/sprites/enemies/enemy_level1.png";
    private static readonly Vector2 EnemySpriteFrameSize = new(64.0f, 64.0f);
    private const float EnemySpriteDebugDisplaySizePixels = 32.0f;
    private static readonly Vector2 EnemySpriteVisualOffsetArcade = new(0.0f, 1.0f);

    private int _mazeWidth = DefaultMazeWidth;
    private int _mazeHeight = DefaultMazeHeight;
    private int[] _wallMasks = new int[DefaultMazeWidth * DefaultMazeHeight];
    private EnemyTraceFrame? _snapshot;

    private Texture2D? _playerSpriteSheet;
    private bool _playerSpriteLoadAttempted;
    private Texture2D? _enemyLevel1SpriteSheet;
    private bool _enemySpriteLoadAttempted;
    private bool _showInactiveEnemySlots;
    private Vector2 _playerSpriteVisualOffsetArcade = DefaultPlayerSpriteVisualOffsetArcade;

    public string BoardTitle { get; set; } = "Board";

    public Vector2 PlayerSpriteVisualOffsetArcade => _playerSpriteVisualOffsetArcade;

    public bool ShowPlayerDebugMarkers => false;

    public bool ShowInactiveEnemySlots => _showInactiveEnemySlots;

    public void SetShowInactiveEnemySlots(bool show)
    {
        _showInactiveEnemySlots = show;
        QueueRedraw();
    }

    public void ToggleInactiveEnemySlots()
    {
        SetShowInactiveEnemySlots(!_showInactiveEnemySlots);
    }

    /// <summary>
    /// Kept as a compatibility no-op for older UI code. The player raw cross and
    /// yellow target trace are deliberately no longer rendered by this board.
    /// </summary>
    public void SetShowPlayerDebugMarkers(bool show)
    {
        QueueRedraw();
    }

    public void TogglePlayerDebugMarkers()
    {
        QueueRedraw();
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
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        DrawString(font, new Vector2(8, 18), BoardTitle, HorizontalAlignment.Left, -1, 16, Colors.White);

        Rect2 boardRect = ComputeBoardRect();
        DrawRect(boardRect, new Color(0.025f, 0.025f, 0.035f, 1.0f), true);
        DrawRect(boardRect, new Color(0.20f, 0.20f, 0.30f, 1.0f), false, 2.0f);

        if (_showInactiveEnemySlots)
        {
            DrawString(
                font,
                boardRect.Position + new Vector2(8, 18),
                "debug enemies: inactive known slots visible",
                HorizontalAlignment.Left,
                -1,
                12,
                new Color(0.75f, 0.75f, 1.0f, 1.0f));
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
        DrawActors(origin, cell);
    }

    private void EnsurePlayerSpriteLoaded()
    {
        if (_playerSpriteLoadAttempted)
            return;

        _playerSpriteLoadAttempted = true;

        if (ResourceLoader.Exists(PlayerSpriteSheetPath))
            _playerSpriteSheet = ResourceLoader.Load<Texture2D>(PlayerSpriteSheetPath);
    }

    private void EnsureEnemySpriteLoaded()
    {
        if (_enemySpriteLoadAttempted)
            return;

        _enemySpriteLoadAttempted = true;

        if (ResourceLoader.Exists(EnemyLevel1SpriteSheetPath))
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
            if (!enemy.active && (!_showInactiveEnemySlots || !enemy.HasKnownPosition))
                continue;

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
            DrawPlayerSprite(cell, actor, rawCenter);
        else
            DrawDebugActor(origin, cell, actor, new Color(1.0f, 0.25f, 0.15f, 1.0f), "P", true);
    }

    private void DrawPlayerSprite(float cell, EnemyTraceActor actor, Vector2 rawCenter)
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

        DrawSpriteRegion(_playerSpriteSheet, destination, source, frame.FlipH, frame.FlipV, Colors.White);
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

    private Vector2 ArcadePointToBoard(Vector2 origin, float cell, int mameX, int mameY)
    {
        int godotArcadeY = MameTraceCoordinates.MameToGodotArcadeY(mameY);

        float localX = mameX / ArcadeCellSize * cell;
        float localY = godotArcadeY / ArcadeCellSize * cell;
        return origin + new Vector2(localX, localY);
    }

    private static Vector2 ArcadeDeltaToBoard(float cell, Vector2 arcadeDelta)
    {
        return arcadeDelta / ArcadeCellSize * cell;
    }

    private static PlayerSpriteFrame ResolvePlayerFrame(string? dir)
    {
        return NormalizeDir(dir) switch
        {
            0x01 => new PlayerSpriteFrame(new Vector2(0, 0), true, false),
            0x02 => new PlayerSpriteFrame(new Vector2(192, 0), false, true),
            0x04 => new PlayerSpriteFrame(new Vector2(0, 0), false, false),
            0x08 => new PlayerSpriteFrame(new Vector2(192, 0), false, false),
            _ => new PlayerSpriteFrame(new Vector2(192, 0), false, false)
        };
    }

    private EnemySpriteFrame ResolveEnemyFrame(string? dir, int slot)
    {
        int frameInAnimation = GetEnemyAnimationFrame(slot);
        float rightFrameX = frameInAnimation * EnemySpriteFrameSize.X;
        float upFrameX = (3 + frameInAnimation) * EnemySpriteFrameSize.X;

        return NormalizeDir(dir) switch
        {
            0x01 => new EnemySpriteFrame(new Vector2(rightFrameX, 0), true, false),
            0x02 => new EnemySpriteFrame(new Vector2(upFrameX, 0), false, true),
            0x04 => new EnemySpriteFrame(new Vector2(rightFrameX, 0), false, false),
            0x08 => new EnemySpriteFrame(new Vector2(upFrameX, 0), false, false),
            _ => new EnemySpriteFrame(new Vector2(rightFrameX, 0), false, false)
        };
    }

    private int GetEnemyAnimationFrame(int slot)
    {
        int tick = _snapshot?.frame ?? 0;
        return Mathf.PosMod((tick / 8) + slot, 3);
    }

    private static Vector2 PlayerDirectionToVector(string? dir)
    {
        return NormalizeDir(dir) switch
        {
            0x01 => Vector2.Left,
            0x02 => Vector2.Down,
            0x04 => Vector2.Right,
            0x08 => Vector2.Up,
            _ => Vector2.Zero
        };
    }

    private static Vector2 EnemyDirectionToVector(string? dir)
    {
        return NormalizeDir(dir) switch
        {
            0x01 => Vector2.Left,
            0x02 => Vector2.Down,
            0x04 => Vector2.Right,
            0x08 => Vector2.Up,
            _ => Vector2.Zero
        };
    }

    private static int NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return 0;

        string value = dir.Trim().ToLowerInvariant();
        return value switch
        {
            "left" => 0x01,
            "down" => 0x02,
            "right" => 0x04,
            "up" => 0x08,
            _ => int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out int parsed)
                ? parsed & 0x0F
                : 0
        };
    }

    private readonly record struct PlayerSpriteFrame(Vector2 SourcePosition, bool FlipH, bool FlipV);
    private readonly record struct EnemySpriteFrame(Vector2 SourcePosition, bool FlipH, bool FlipV);
}
