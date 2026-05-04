using System.Collections.Generic;

/// <summary>
/// Enemy movement diagnostic state produced by a simulation source.
/// </summary>
public sealed class SimulationEnemyWorkState
{
    public int TempDir { get; set; } = -1;
    public int TempX { get; set; } = -1;
    public int TempY { get; set; } = -1;
    public int RejectedMask { get; set; } = -1;
    public int FallbackMask { get; set; } = -1;
    public List<int> Preferred { get; } = new();
    public List<int> ChaseTimers { get; } = new();
    public int ChaseRoundRobin { get; set; } = -1;
}
