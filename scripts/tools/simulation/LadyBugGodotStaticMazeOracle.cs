using System;
using System.Text.Json;
using Godot;

/// <summary>
/// Static Godot-side maze oracle for v0.9.0b.
///
/// This mirrors the static-maze part used by the main LadyBug project:
/// - data/maze.json stores wall masks;
/// - enemy positions use the enemy anchor (8,6), not the player anchor (8,7);
/// - MAME actor Y is converted to the simulator / Godot arcade Y before mapping
///   to a logical cell.
///
/// v0.9.0b deliberately answers the static navigation question at the current
/// logical cell: is this direction allowed by the maze wall mask? It does not use
/// the one-pixel same-cell shortcut, because the MAME 0x6200 map is a direction
/// availability map, not a per-pixel boundary-crossing test.
/// </summary>
public sealed class LadyBugGodotStaticMazeOracle : IEnemyMazeCollisionOracle
{
    private const string SourceName = "Godot static maze.json";
    private const string DefaultMazePath = "res://data/maze.json";

    private const int CellSizeArcade = 16;
    private static readonly Vector2I EnemyAnchorArcade = new(8, 6);

    private readonly int _width;
    private readonly int _height;
    private readonly int[] _wallMasks;

    public LadyBugGodotStaticMazeOracle(string mazePath = DefaultMazePath)
    {
        LoadMaze(mazePath, out _width, out _height, out _wallMasks);
    }

    public EnemyCollisionProbeResult Probe(int x, int y, int direction)
    {
        Vector2I move = LadyBugDirectionBits.ToGodotVector(direction);
        if (move == Vector2I.Zero)
            return EnemyCollisionProbeResult.InvalidDirection(SourceName, direction);

        int mameX = x & 0xFF;
        int mameY = y & 0xFF;
        int godotY = MameTraceCoordinates.MameToGodotArcadeY(mameY) & 0xFF;

        Vector2I currentPixel = new(mameX, godotY);
        Vector2I currentCell = ArcadePixelToLogicalCell(currentPixel);

        if (IsLairExitZone(currentCell))
        {
            bool allowedBySpecialLairRule = (direction & 0x0F) == LadyBugDirectionBits.Up;

            return new EnemyCollisionProbeResult
            {
                Allowed = allowedBySpecialLairRule,
                BlockKind = "lair-exit-zone",
                Source = SourceName,
                CellX = currentCell.X,
                CellY = currentCell.Y,
                Details =
                    $"mame=({mameX:X2},{mameY:X2}) godotY={godotY:X2} " +
                    $"cell=({currentCell.X},{currentCell.Y}) " +
                    $"dir={LadyBugDirectionBits.ToLabel(direction)} " +
                    "special-case: enemy starts in den; left/down/right are den walls; exit is up"
            };
        }

        if (!IsInside(currentCell))
        {
            return new EnemyCollisionProbeResult
            {
                Allowed = false,
                BlockKind = "out-of-bounds",
                Source = SourceName,
                CellX = currentCell.X,
                CellY = currentCell.Y,
                Details = $"mame=({mameX:X2},{mameY:X2}) godotY={godotY:X2}"
            };
        }

        bool allowed = CanMove(currentCell, move);
        int mask = GetWallMask(currentCell.X, currentCell.Y);

        return new EnemyCollisionProbeResult
        {
            Allowed = allowed,
            BlockKind = allowed ? "none" : "fixed-wall",
            Source = SourceName,
            CellX = currentCell.X,
            CellY = currentCell.Y,
            Details =
                $"wallMask={mask:X1} mame=({mameX:X2},{mameY:X2}) " +
                $"godotY={godotY:X2} cell=({currentCell.X},{currentCell.Y}) " +
                $"dir={LadyBugDirectionBits.ToLabel(direction)}"
        };
    }

    public bool IsDecisionCenter(int x, int y)
    {
        return (x & 0x0F) == 0x08 &&
               (y & 0x0F) == 0x06;
    }

    private bool CanMove(Vector2I cell, Vector2I direction)
    {
        if (!IsInside(cell))
            return false;

        int mask = GetWallMask(cell.X, cell.Y);

        if (direction == Vector2I.Up)
            return (mask & 0x01) == 0;

        if (direction == Vector2I.Down)
            return (mask & 0x02) == 0;

        if (direction == Vector2I.Left)
            return (mask & 0x04) == 0;

        if (direction == Vector2I.Right)
            return (mask & 0x08) == 0;

        return false;
    }

    private int GetWallMask(int x, int y)
    {
        int index = y * _width + x;
        if (index < 0 || index >= _wallMasks.Length)
            return 0x0F;

        return _wallMasks[index] & 0x0F;
    }

    private bool IsInside(Vector2I cell)
    {
        return cell.X >= 0 && cell.X < _width &&
               cell.Y >= 0 && cell.Y < _height;
    }

    private static Vector2I ArcadePixelToLogicalCell(Vector2I arcadePixel)
    {
        int halfCell = CellSizeArcade / 2;

        int x = FloorDiv(
            arcadePixel.X - EnemyAnchorArcade.X + halfCell,
            CellSizeArcade);

        int y = FloorDiv(
            arcadePixel.Y - EnemyAnchorArcade.Y + halfCell,
            CellSizeArcade);

        return new Vector2I(x, y);
    }

    private static bool IsLairExitZone(Vector2I cell)
    {
        return cell == new Vector2I(5, 5);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && ((value < 0) != (divisor < 0)))
            quotient--;

        return quotient;
    }

    private static void LoadMaze(string mazePath, out int width, out int height, out int[] wallMasks)
    {
        if (!FileAccess.FileExists(mazePath))
            throw new InvalidOperationException($"Maze file not found: {mazePath}");

        using var file = FileAccess.Open(mazePath, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new InvalidOperationException($"Could not open maze file: {mazePath}");

        using JsonDocument document = JsonDocument.Parse(file.GetAsText());
        JsonElement root = document.RootElement;

        width = root.GetProperty("width").GetInt32();
        height = root.GetProperty("height").GetInt32();

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Invalid maze dimensions in {mazePath}: {width}x{height}");

        JsonElement cells = root.GetProperty("cells");
        wallMasks = new int[width * height];

        int index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            if (index >= wallMasks.Length)
                break;

            wallMasks[index++] = cell.GetInt32() & 0x0F;
        }

        if (index != wallMasks.Length)
            throw new InvalidOperationException($"Maze cell count mismatch in {mazePath}: expected {wallMasks.Length}, got {index}");
    }
}
