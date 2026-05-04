/// <summary>
/// One actor entry decoded from the MAME trace.
///
/// For enemies, <see cref="raw"/> is the byte at the start of the arcade enemy slot.
/// For the player, it is the player state byte.
/// </summary>
public sealed class EnemyTraceActor
{
    public int slot { get; set; }
    public int raw { get; set; } = -1;
    public int x { get; set; }
    public int y { get; set; }
    public int sprite { get; set; } = -1;
    public int attr { get; set; } = -1;
    public int turnTargetX { get; set; } = -1;
    public int turnTargetY { get; set; } = -1;
    public bool HasTurnTarget => turnTargetX >= 0 && turnTargetY >= 0;
    public bool HasKnownPosition => x != 0 || y != 0;
    public string? dir { get; set; }
    public bool active { get; set; } = true;
}
