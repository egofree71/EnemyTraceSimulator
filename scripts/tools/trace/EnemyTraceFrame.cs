using System.Collections.Generic;

/// <summary>
/// One decoded frame from the MAME Lady Bug JSONL trace.
///
/// This class is a DTO: it mirrors the trace file and should stay tolerant of
/// schema growth. It intentionally does not implement gameplay logic.
/// </summary>
public sealed class EnemyTraceFrame
{
    /// <summary>Trace schema/version string written by the Lua script.</summary>
    public string? schema { get; set; }

    /// <summary>
    /// Logical trace tick. The Lua script also accepts older "frame" naming, but
    /// the current trace uses "tick".
    /// </summary>
    public int frame { get; set; }

    /// <summary>Capture phase, for example post_load_tick0 or frame_done.</summary>
    public string? phase { get; set; }

    /// <summary>MAME video frame counter sampled by the Lua script.</summary>
    public int mameFrame { get; set; } = -1;

    /// <summary>
    /// CPU program counter sampled at the frame boundary.
    ///
    /// Important: this is not necessarily the PC of a specific write to RAM.
    /// </summary>
    public string? pc { get; set; }

    /// <summary>
    /// Z80 R register sampled at the frame boundary.
    ///
    /// Important: this is not necessarily the R value used by an internal LD A,R
    /// inside the enemy decision routine.
    /// </summary>
    public string? r { get; set; }

    /// <summary>Decoded player state for this frame.</summary>
    public EnemyTraceActor? player { get; set; }

    /// <summary>
    /// Decoded enemy slots. Slots may contain inactive or stale state, so gameplay
    /// code must check the active flag before treating a slot as visible.
    /// </summary>
    public List<EnemyTraceActor>? enemies { get; set; }

    /// <summary>Decoded rotating gate states for the diagnostic board.</summary>
    public List<EnemyTraceGateState>? gates { get; set; }

    /// <summary>
    /// Shared enemy scratch/work RAM captured from 0x61BD onward.
    ///
    /// This does not belong permanently to one enemy slot. With multiple active
    /// enemies it represents the enemy currently being processed by the arcade
    /// routine.
    /// </summary>
    public EnemyTraceEnemyWorkState? enemyWork { get; set; }

    /// <summary>
    /// Optional polling-diff events for 0x61C4..0x61C7 preferred[] changes.
    ///
    /// These events come from the safe Lua polling trace. They are frame-level
    /// before/after transitions, not exact CPU write-tap events.
    /// </summary>
    public List<EnemyTracePreferredChangeEvent>? preferredChangeEvents { get; set; }

    /// <summary>Decoded global timer bytes used by the enemy subsystem.</summary>
    public EnemyTraceTimersState? timers { get; set; }

    /// <summary>Decoded input/DIP port state.</summary>
    public EnemyTracePortsState? ports { get; set; }

    /// <summary>
    /// Optional raw memory blocks included by the Lua trace for diagnostics.
    /// These are intentionally kept separate from typed state.
    /// </summary>
    public EnemyTraceRawMemoryState? rawMemory { get; set; }

    /// <summary>
    /// Optional compact hex dump of the 0x6200..0x62AF logical maze. Kept for
    /// backwards compatibility with earlier traces.
    /// </summary>
    public string? logicalMaze6200_62AF { get; set; }
}
