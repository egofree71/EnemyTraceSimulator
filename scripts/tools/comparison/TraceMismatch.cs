/// <summary>
/// One mismatch found while comparing a MAME reference frame with a simulation frame.
/// </summary>
public sealed class TraceMismatch
{
    public TraceMismatchKind Kind { get; init; }
    public int FrameIndex { get; init; }
    public int Tick { get; init; }
    public int? Slot { get; init; }
    public int? GateId { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public enum TraceMismatchKind
{
    Frame,
    Player,
    Enemy,
    Gate,
    Count
}
