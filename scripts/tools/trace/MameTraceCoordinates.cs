/// <summary>
/// Coordinate helpers for converting raw MAME actor coordinates into the
/// Godot/debug-board arcade coordinate convention used by the simulator.
/// </summary>
public static class MameTraceCoordinates
{
    /// <summary>
    /// MAME actor Y is mirrored compared with the debug-board arcade Y.
    /// Observed examples: top of maze ~= 0xD6, bottom of maze ~= 0x36.
    /// </summary>
    public const int MameYMirror = 0xDD;

    public static int MameToGodotArcadeY(int mameY)
    {
        return MameYMirror - mameY;
    }

    public static int GodotToMameArcadeY(int godotArcadeY)
    {
        return MameYMirror - godotArcadeY;
    }
}
