using System;

/// <summary>
/// v0.9.3 MAME-side source-first logical maze oracle.
///
/// This uses the reverse-engineered enemy movement validator at 0x3911 instead
/// of treating the runtime navigation table at 0x6200..0x62AF as the final
/// collision verdict.
///
/// Important distinction:
/// - 0x6200..0x62AF is the runtime navigation/BFS map observed in RAM.
/// - 0x3911 validates a candidate enemy direction against the packed ROM table
///   at 0x0DA2.
///
/// The final movement validator used by the enemy decision path is conceptually:
///
///     source logical validator 0x3911
///     + local tile / door validator 0x4130
///
/// This class provides only the 0x3911 part. 0x4130 is still supplied by
/// LadyBugMameLocalTile4130Oracle.
/// </summary>
public sealed class LadyBugMameSource3911Oracle : IEnemyMazeCollisionOracle
{
    private const string SourceName = "MAME source 0x3911 / ROM 0x0DA2";

    public EnemyCollisionProbeResult Probe(int x, int y, int direction)
    {
        int dir = direction & 0x0F;
        if (!IsValidDirection(dir))
            return EnemyCollisionProbeResult.InvalidDirection(SourceName, direction);

        int mameX = x & 0xFF;
        int mameY = y & 0xFF;
        int column = mameX >> 4;
        int row = LadyBugMameLogicalMazeOracle.MameYToLogicalMazeRow(mameY);

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
                    "special-case: initial den exit rule"
            };
        }

        try
        {
            LadyBugEnemyDecisionModel.LogicalMazeValidationResult result =
                LadyBugEnemyDecisionModel.ValidateLogicalMazeDirection(
                    dir,
                    mameX,
                    mameY,
                    LadyBugStaticMazeRomTable.Table0DA2);

            return new EnemyCollisionProbeResult
            {
                Allowed = result.Accepted,
                BlockKind = result.Accepted ? "none" : "source-3911-wall",
                Source = SourceName,
                CellX = column,
                CellY = row,
                Details =
                    $"tableIndex=0x{result.TableIndex:X2} byte={result.PackedByte:X2} " +
                    $"allowedMask={result.AllowedMask:X1} candidate={LadyBugDirectionBits.ToLabel(dir)} " +
                    $"mame=({mameX:X2},{mameY:X2}) row={row} col={column}"
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new EnemyCollisionProbeResult
            {
                Allowed = false,
                BlockKind = "source-3911-out-of-range",
                Source = SourceName,
                CellX = column,
                CellY = row,
                Details = $"mame=({mameX:X2},{mameY:X2}) row={row} col={column} {ex.Message}"
            };
        }
    }

    public bool IsDecisionCenter(int x, int y)
    {
        return LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(x, y);
    }

    private static bool IsValidDirection(int direction)
    {
        int dir = direction & 0x0F;
        return dir == LadyBugDirectionBits.Left ||
               dir == LadyBugDirectionBits.Up ||
               dir == LadyBugDirectionBits.Right ||
               dir == LadyBugDirectionBits.Down;
    }

    private static bool IsLairExitZone(int column, int row)
    {
        return column == 5 && row == 5;
    }
}
