using System.Collections.Generic;

/// <summary>
/// One decoded frame from the MAME Lady Bug JSONL trace.
/// </summary>
public sealed class EnemyTraceFrame
{
    public int frame { get; set; }
    public EnemyTraceActor? player { get; set; }
    public List<EnemyTraceActor>? enemies { get; set; }
    public List<EnemyTraceGateState>? gates { get; set; }
}
