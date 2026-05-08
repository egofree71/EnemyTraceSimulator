using System;
using System.Collections.Generic;

/// <summary>
/// MAME-side logical maze oracle for v0.9.0b.
///
/// It reads rawMemory.logicalMaze6200_62AF from the currently inspected frame.
/// The arcade table is laid out at 0x6200 with a 16-byte row stride and 11 useful
/// columns per row. The source helper at 0x45DC converts enemy pixel coordinates
/// to a table offset as:
///
///   column = D >> 4
///   row    = 0x0A - (((E - 0x30) & 0xFF) >> 4)
///   offset = row * 16 + column
///
/// This is the important v0.9.0b correction: do not use raw MAME Y as a direct
/// top-down row. MAME actor Y is vertically mirrored compared with the simulator
/// / Godot debug board. The 0x45DC formula performs the arcade-side row flip.
///
/// High nibble = allowed enemy directions.
/// Low nibble  = BFS / guidance data.
/// </summary>
public sealed class LadyBugMameLogicalMazeOracle : IEnemyMazeCollisionOracle
{
    private const string SourceName = "MAME logical maze 0x6200..0x62AF";
    private const int UsefulColumns = 11;
    private const int UsefulRows = 11;
    private const int RowStride = 16;
    private const int ExpectedByteCount = 0xB0;

    private readonly int[]? _logicalMaze;

    public LadyBugMameLogicalMazeOracle(EnemyTraceFrame frame)
    {
        _logicalMaze = TryParseLogicalMaze(frame);
    }

    public bool HasMemory => _logicalMaze != null;

    public EnemyCollisionProbeResult Probe(int x, int y, int direction)
    {
        if (_logicalMaze == null)
        {
            return EnemyCollisionProbeResult.MissingMemory(
                SourceName,
                "frame.rawMemory.logicalMaze6200_62AF is missing or invalid");
        }

        int dir = direction & 0x0F;
        if (LadyBugDirectionBits.ToGodotVector(dir) == Godot.Vector2I.Zero)
            return EnemyCollisionProbeResult.InvalidDirection(SourceName, direction);

        int mameX = x & 0xFF;
        int mameY = y & 0xFF;
        int column = mameX >> 4;
        int row = MameYToLogicalMazeRow(mameY);

        if (IsLairExitZone(column, row))
        {
            bool allowedBySpecialLairRule = dir == LadyBugDirectionBits.Up;

            return new EnemyCollisionProbeResult
            {
                Allowed = allowedBySpecialLairRule,
                BlockKind = "lair-exit-zone",
                Source = SourceName,
                CellX = column,
                CellY = row,
                Details =
                    $"mame=({mameX:X2},{mameY:X2}) row={row} col={column} " +
                    $"dir={LadyBugDirectionBits.ToLabel(dir)} " +
                    "special-case: enemy starts in den; left/down/right are den walls; exit is up"
            };
        }

        if (column < 0 || column >= UsefulColumns || row < 0 || row >= UsefulRows)
        {
            return new EnemyCollisionProbeResult
            {
                Allowed = false,
                BlockKind = "out-of-bounds",
                Source = SourceName,
                CellX = column,
                CellY = row,
                Details = $"mame=({mameX:X2},{mameY:X2}) row={row} col={column}"
            };
        }

        int offset = row * RowStride + column;
        int packed = _logicalMaze[offset] & 0xFF;
        int allowedMask = (packed >> 4) & 0x0F;
        bool allowed = (allowedMask & dir) != 0;

        return new EnemyCollisionProbeResult
        {
            Allowed = allowed,
            BlockKind = allowed ? "none" : "fixed-wall",
            Source = SourceName,
            CellX = column,
            CellY = row,
            Details =
                $"offset=0x{offset:X2} byte={packed:X2} allowedMask={allowedMask:X1} " +
                $"mame=({mameX:X2},{mameY:X2}) row={row} col={column} " +
                $"dir={LadyBugDirectionBits.ToLabel(dir)}"
        };
    }

    public bool IsDecisionCenter(int x, int y)
    {
        return (x & 0x0F) == 0x08 &&
               (y & 0x0F) == 0x06;
    }

    /// <summary>
    /// Source-equivalent row calculation from 0x45DC.
    /// Valid enemy maze Y values are normally in the 0x30..0xD0-ish range.
    /// </summary>
    public static int MameYToLogicalMazeRow(int mameY)
    {
        int eMinus30 = ((mameY & 0xFF) - 0x30) & 0xFF;
        int coarseFromBottom = eMinus30 >> 4;
        return 0x0A - coarseFromBottom;
    }

    private static bool IsLairExitZone(int column, int row)
    {
        // Raw MAME den center around x=0x58, y=0x86 maps through the 0x45DC
        // coordinate helper to logical cell (5,5), matching the Godot lair cell.
        return column == 5 && row == 5;
    }

    private static int[]? TryParseLogicalMaze(EnemyTraceFrame frame)
    {
        string? hex = frame.rawMemory?.logicalMaze6200_62AF;

        if (string.IsNullOrWhiteSpace(hex))
            hex = frame.logicalMaze6200_62AF;

        if (string.IsNullOrWhiteSpace(hex))
            return null;

        hex = RemoveWhitespace(hex);

        if ((hex.Length & 1) != 0)
            return null;

        int byteCount = hex.Length / 2;
        if (byteCount < ExpectedByteCount)
            return null;

        var result = new int[ExpectedByteCount];

        for (int i = 0; i < ExpectedByteCount; i++)
        {
            string token = hex.Substring(i * 2, 2);
            if (!int.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out int value))
                return null;

            result[i] = value & 0xFF;
        }

        return result;
    }

    private static string RemoveWhitespace(string text)
    {
        var chars = new List<char>(text.Length);

        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
                chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}
