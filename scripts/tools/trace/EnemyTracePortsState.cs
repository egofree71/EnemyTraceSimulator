/// <summary>
/// Input port and DIP switch values decoded from the MAME trace.
/// </summary>
public sealed class EnemyTracePortsState
{
    public int in0_9000 { get; set; } = -1;
    public int in1_9001 { get; set; } = -1;
    public int dsw0_9002 { get; set; } = -1;
    public int dsw1_9003 { get; set; } = -1;
}
