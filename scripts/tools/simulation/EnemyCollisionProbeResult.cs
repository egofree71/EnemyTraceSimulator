/// <summary>
/// Result of one functional enemy collision probe.
///
/// v0.9.0a intentionally keeps this small and diagnostic-oriented. The goal is
/// not to reproduce the whole arcade movement routine here, but to answer one
/// focused question:
///
/// At this enemy pixel position, in this enemy direction, would this oracle allow
/// a one-pixel movement step?
/// </summary>
public sealed class EnemyCollisionProbeResult
{
    public bool Allowed { get; init; }

    /// <summary>
    /// Broad classification:
    /// - none
    /// - fixed-wall
    /// - out-of-bounds
    /// - lair-exit-zone
    /// - missing-memory
    /// - invalid-direction
    /// </summary>
    public string BlockKind { get; init; } = "none";

    /// <summary>
    /// Human-readable name of the oracle that produced this result.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Logical cell X used by the oracle, if applicable.
    /// </summary>
    public int CellX { get; init; } = -1;

    /// <summary>
    /// Logical cell Y used by the oracle, if applicable.
    /// </summary>
    public int CellY { get; init; } = -1;

    /// <summary>
    /// Optional address/index/tile/mask details for debugging.
    /// </summary>
    public string Details { get; init; } = string.Empty;

    public static EnemyCollisionProbeResult MissingMemory(string source, string details)
    {
        return new EnemyCollisionProbeResult
        {
            Allowed = false,
            BlockKind = "missing-memory",
            Source = source,
            Details = details
        };
    }

    public static EnemyCollisionProbeResult InvalidDirection(string source, int direction)
    {
        return new EnemyCollisionProbeResult
        {
            Allowed = false,
            BlockKind = "invalid-direction",
            Source = source,
            Details = $"dir={direction:X2}"
        };
    }
}
