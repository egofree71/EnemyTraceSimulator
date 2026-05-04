/// <summary>
/// Gate state produced by a simulation source.
/// </summary>
public sealed class SimulationGateState
{
    public int GateId { get; set; }
    public string? Orientation { get; set; }
    public int PivotX { get; set; } = -1;
    public int PivotY { get; set; } = -1;
}
