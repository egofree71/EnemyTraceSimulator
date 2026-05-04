using System.Collections.Generic;

/// <summary>
/// One decoded frame from the MAME Lady Bug JSONL trace.
/// </summary>
public sealed class EnemyTraceFrame
{
    public string? schema { get; set; }
    public int frame { get; set; }
    public string? phase { get; set; }
    public int mameFrame { get; set; } = -1;
    public string? pc { get; set; }
    public string? r { get; set; }
    public EnemyTraceActor? player { get; set; }
    public List<EnemyTraceActor>? enemies { get; set; }
    public List<EnemyTraceGateState>? gates { get; set; }
    public EnemyTraceEnemyWorkState? enemyWork { get; set; }
    public EnemyTraceTimersState? timers { get; set; }
    public EnemyTracePortsState? ports { get; set; }
    public EnemyTraceRawMemoryState? rawMemory { get; set; }
    public string? logicalMaze6200_62AF { get; set; }
}
