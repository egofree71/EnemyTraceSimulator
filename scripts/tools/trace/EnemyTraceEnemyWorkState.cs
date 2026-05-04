using System.Collections.Generic;

/// <summary>
/// Enemy movement work RAM decoded from the MAME trace.
/// These values are diagnostic, but they are essential for understanding fallback,
/// rejection masks, and chase decision behavior.
/// </summary>
public sealed class EnemyTraceEnemyWorkState
{
    public int tempDir { get; set; } = -1;
    public int tempX { get; set; } = -1;
    public int tempY { get; set; } = -1;
    public int rejectedMask { get; set; } = -1;
    public int fallbackMask { get; set; } = -1;
    public List<int> preferred { get; set; } = new();
    public List<int> chaseTimers { get; set; } = new();
    public int chaseRoundRobin { get; set; } = -1;
}
