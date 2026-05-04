using System.Collections.Generic;

/// <summary>
/// Comparison result for one frame index.
/// </summary>
public sealed class ComparisonFrame
{
    public int FrameIndex { get; init; }
    public int ReferenceTick { get; init; }
    public int SimulationTick { get; init; }
    public List<TraceMismatch> Mismatches { get; } = new();
    public bool HasMismatch => Mismatches.Count > 0;
}
