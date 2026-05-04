/// <summary>
/// Timer and global state bytes decoded from the MAME trace.
/// Property names keep the original RAM address where this is the clearest label.
/// </summary>
public sealed class EnemyTraceTimersState
{
    public int timer61B4 { get; set; } = -1;
    public int timer61B5 { get; set; } = -1;
    public int timer61B6 { get; set; } = -1;
    public int timer61B7 { get; set; } = -1;
    public int timer61B8 { get; set; } = -1;
    public int timer61B9 { get; set; } = -1;
    public int freeze61E1 { get; set; } = -1;
    public int collectibleColorCounter6199 { get; set; } = -1;
}
