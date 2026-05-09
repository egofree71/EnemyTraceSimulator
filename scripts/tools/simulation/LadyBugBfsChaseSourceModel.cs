using System;
using System.Globalization;

/// <summary>
/// Source-first model of the small part of Lady Bug's BFS/chase system used by
/// the preferred[] override path.
///
/// Relevant arcade path:
///   0x46D8 / 0x471E..0x477D selects a chase/BFS override target.
///   0x45DC converts enemy pixel coordinates in D/E to a logical-maze index.
///   0x4775/0x4776 reads 0x6200 + index and keeps the low nibble.
///   0x477D writes that low nibble into preferred[slot] when it is not 0x0F.
///
/// The trace stores the 0x6200..0x62AF logical maze as a hex string. This class
/// intentionally does not guess from display-space coordinates; it uses the
/// source coordinate-to-index formula validated in v0.9.14b.
/// </summary>
public static class LadyBugBfsChaseSourceModel
{
    public const int LogicalMazeBase = 0x6200;
    public const int LogicalMazeLength = 0x00B0;
    public const int SentinelNoGuidance = 0x0F;

    public static bool TryGetGuidance(
        EnemyTraceFrame frame,
        int enemyX,
        int enemyY,
        out GuidanceResult result)
    {
        result = default;

        if (!TryCompute45DcIndex(enemyX, enemyY, out int index, out int column, out int rowFromTop))
            return false;

        if (!TryReadLogicalMazeByte(frame, index, out int value))
            return false;

        int direction = value & 0x0F;
        result = new GuidanceResult(
            enemyX & 0xFF,
            enemyY & 0xFF,
            column,
            rowFromTop,
            index,
            LogicalMazeBase + index,
            value,
            direction);

        return IsOneHotDirection(direction);
    }

    public static bool TryCompute45DcIndex(
        int enemyX,
        int enemyY,
        out int index,
        out int column,
        out int rowFromTop)
    {
        int x = enemyX & 0xFF;
        int y = enemyY & 0xFF;

        column = x >> 4;
        rowFromTop = 0x0A - ((y - 0x30) >> 4);
        index = (rowFromTop << 4) | column;

        return column >= 0 && column <= 0x0F &&
               rowFromTop >= 0 && rowFromTop <= 0x0A &&
               index >= 0 && index < LogicalMazeLength;
    }

    public static bool TryReadLogicalMazeByte(EnemyTraceFrame frame, int index, out int value)
    {
        value = 0;

        if (frame == null || index < 0 || index >= LogicalMazeLength)
            return false;

        string? hex = frame.rawMemory?.logicalMaze6200_62AF;
        if (string.IsNullOrWhiteSpace(hex))
            hex = frame.logicalMaze6200_62AF;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        hex = hex.Trim();
        int offset = index * 2;
        if (offset < 0 || offset + 2 > hex.Length)
            return false;

        return int.TryParse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public static bool IsOneHotDirection(int direction)
    {
        int d = direction & 0x0F;
        return d == LadyBugMonsterPreferenceSystem.DirLeft ||
               d == LadyBugMonsterPreferenceSystem.DirUp ||
               d == LadyBugMonsterPreferenceSystem.DirRight ||
               d == LadyBugMonsterPreferenceSystem.DirDown;
    }

    public readonly struct GuidanceResult
    {
        public GuidanceResult(
            int enemyX,
            int enemyY,
            int column,
            int rowFromTop,
            int index,
            int address,
            int mazeByte,
            int direction)
        {
            EnemyX = enemyX;
            EnemyY = enemyY;
            Column = column;
            RowFromTop = rowFromTop;
            Index = index;
            Address = address;
            MazeByte = mazeByte;
            Direction = direction;
        }

        public int EnemyX { get; }
        public int EnemyY { get; }
        public int Column { get; }
        public int RowFromTop { get; }
        public int Index { get; }
        public int Address { get; }
        public int MazeByte { get; }
        public int Direction { get; }

        public string ToCompactString()
        {
            return $"enemy=({EnemyX:X2},{EnemyY:X2}) idx={Index:X2} addr={Address:X4} cell=({Column},{RowFromTop}) byte={MazeByte:X2} low={Direction:X2}";
        }
    }
}
