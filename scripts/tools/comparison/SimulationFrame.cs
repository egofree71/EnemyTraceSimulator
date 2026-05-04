using System.Collections.Generic;

/// <summary>
/// One frame produced by a simulation source.
/// v0.5 uses this as the neutral data model for the future C# enemy simulation adapter.
/// </summary>
public sealed class SimulationFrame
{
    public int FrameIndex { get; set; }
    public int Tick { get; set; }
    public SimulationActorState? Player { get; set; }
    public List<SimulationActorState> Enemies { get; } = new();
    public List<SimulationGateState> Gates { get; } = new();
}
