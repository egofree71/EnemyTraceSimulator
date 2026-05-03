using Godot;
using System;

/// <summary>
/// Lightweight debug renderer for one side of the enemy trace comparison.
/// It draws the static logical maze, pivoting gates from the trace, the player, and enemies.
/// </summary>
public partial class EnemyTraceBoardView : Control
{
    private const int DefaultMazeWidth = 11;
    private const int DefaultMazeHeight = 11;
    private const float ArcadeCellSize = 16.0f;

    // The rendered debug maze is the 11-row gameplay maze.
    // Actor RAM coordinates are in the larger 11 x 16 arcade logical space,
    // whose visible gameplay area starts 3 cells lower.
    private const float ActorArcadeYOffset = 0x30;

    private const string PlayerSpriteSheetPath = "res://assets/sprites/player/ladybug_spritesheet.png";
    private static readonly Vector2 PlayerSpriteFrameSize = new(64.0f, 64.0f);

    // Debug-render tuning only. This does not change the traced gameplay coordinate.
    // The sprite is deliberately drawn above the raw gameplay anchor, while the white
    // cross keeps showing the exact x/y read from MAME.
    private static readonly Vector2 DefaultPlayerSpriteVisualOffsetArcade = new(0.0f, -7.0f);
    private Vector2 _playerSpriteVisualOffsetArcade = DefaultPlayerSpriteVisualOffsetArcade;

    // The source player frames are 64x64. For the debug board we intentionally draw
    // them at 32x32 pixels, i.e. an exact 1/2 scale, then snap the destination to
    // whole screen pixels. This avoids Photoshop-visible blur from arbitrary scaling.
    private const float PlayerSpriteDebugDisplaySizePixels = 32.0f;

    private int _mazeWidth = DefaultMazeWidth;
    private int _mazeHeight = DefaultMazeHeight;
    private int[] _wallMasks = new int[DefaultMazeWidth * DefaultMazeHeight];
    private EnemyTraceFrame? _snapshot;

    private Texture2D? _playerSpriteSheet;
    private bool _playerSpriteLoadAttempted;
    private bool _showPlayerDebugMarkers;

    public string BoardTitle { get; set; } = "Board";

    public Vector2 PlayerSpriteVisualOffsetArcade => _playerSpriteVisualOffsetArcade;

    public bool ShowPlayerDebugMarkers => _showPlayerDebugMarkers;

    public void SetShowPlayerDebugMarkers(bool show)
    {
        _showPlayerDebugMarkers = show;
        QueueRedraw();
    }

    public void TogglePlayerDebugMarkers()
    {
        SetShowPlayerDebugMarkers(!_showPlayerDebugMarkers);
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

        if (_showPlayerDebugMarkers)
        {
            string debugMarkerText =
                $"debug player | offset=({_playerSpriteVisualOffsetArcade.X:0},{_playerSpriteVisualOffsetArcade.Y:0}) | P raw | T target";
            DrawString(font, boardRect.Position + new Vector2(8, 18), debugMarkerText, HorizontalAlignment.Left, -1, 12, new Color(0.20f, 0.95f, 1.0f, 1.0f));
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

        if (!ResourceLoader.Exists(PlayerSpriteSheetPath))
            return;

        _playerSpriteSheet = ResourceLoader.Load<Texture2D>(PlayerSpriteSheetPath);
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
                continue;

            string label = enemy.slot.ToString();
            DrawDebugActor(origin, cell, enemy, new Color(0.20f, 0.85f, 1.0f, 1.0f), label, false);
        }
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

        DrawTextureRectRegion(_playerSpriteSheet, destination, source, Colors.White, false, true);
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

    private Vector2 ArcadePointToBoard(Vector2 origin, float cell, int arcadeX, int arcadeY)
    {
        float localX = arcadeX / ArcadeCellSize * cell;
        float localY = (arcadeY - ActorArcadeYOffset) / ArcadeCellSize * cell;
        return origin + new Vector2(localX, localY);
    }

    private static Vector2 ArcadeDeltaToBoard(float cell, Vector2 arcadeDelta)
    {
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
        // Player direction encoding, as seen in the trace: 08 = up, 02 = down.
        // For now the debug renderer uses the first static frame of each animation.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => new PlayerSpriteFrame(new Vector2(0, 0)),
            "down" or "02" or "2" => new PlayerSpriteFrame(new Vector2(192, 0)),
            "right" or "04" or "4" => new PlayerSpriteFrame(new Vector2(0, 0)),
            "up" or "08" or "8" => new PlayerSpriteFrame(new Vector2(192, 0)),
            _ => new PlayerSpriteFrame(new Vector2(192, 0))
        };
    }

    private static Vector2 PlayerDirectionToVector(string? dir)
    {
        // Player RAM direction encoding.
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
        // Enemy direction encoding from the reverse-engineering notes.
        return dir?.ToLowerInvariant() switch
        {
            "left" or "01" or "1" => Vector2.Left,
            "up" or "02" or "2" => Vector2.Up,
            "right" or "04" or "4" => Vector2.Right,
            "down" or "08" or "8" => Vector2.Down,
            _ => Vector2.Zero
        };
    }

    private readonly record struct PlayerSpriteFrame(Vector2 SourcePosition);
}
