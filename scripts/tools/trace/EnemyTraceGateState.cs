/// <summary>
/// One rotating gate entry decoded from the MAME trace.
/// </summary>
public sealed class EnemyTraceGateState
{
    public int gate_id { get; set; }
    public string? orientation { get; set; }
    public int pivot_x { get; set; } = -1;
    public int pivot_y { get; set; } = -1;
}
