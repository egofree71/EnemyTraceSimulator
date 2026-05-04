/// <summary>
/// Actor state produced by a simulation source.
/// Coordinates use the same raw MAME coordinate convention as the reference trace.
/// </summary>
public sealed class SimulationActorState
{
    public int Slot { get; set; }
    public int Raw { get; set; } = -1;
    public int X { get; set; }
    public int Y { get; set; }
    public string? Direction { get; set; }
    public bool Active { get; set; }
    public bool HasKnownPosition => X != 0 || Y != 0;
}
