using System.Collections.Generic;

/// <summary>
/// One frame produced by a simulation source.
/// v0.5 uses this as the neutral data model for the future C# enemy simulation adapter.
/// </summary>
public sealed class SimulationFrame
{
    public int FrameIndex { get; set; }
    public int Tick { get; set; }
    public string? Schema { get; set; }
    public string? Phase { get; set; }
    public int MameFrame { get; set; } = -1;
    public string? Pc { get; set; }
    public string? R { get; set; }
    public SimulationActorState? Player { get; set; }
    public List<SimulationActorState> Enemies { get; } = new();
    public List<SimulationGateState> Gates { get; } = new();
    public SimulationEnemyWorkState? EnemyWork { get; set; }
    public SimulationTimersState? Timers { get; set; }
    public SimulationPortsState? Ports { get; set; }
}
