using System.Collections.Generic;

/// <summary>
/// One preferred[] polling-diff event decoded from the MAME trace.
///
/// The Lua trace script compares 0x61C4..0x61C7 once per captured frame. If a
/// value differs from the previous frame, it records a change event.
/// </summary>
/// <remarks>
/// This is deliberately safer than a MAME write tap. On the current setup,
/// write-tap experiments interfered with the save-state post-load notifier.
/// The trade-off is precision: this event tells us what changed between two
/// frames, but not the exact CPU instruction that performed the write.
/// </remarks>
public sealed class EnemyTracePreferredChangeEvent
{
    /// <summary>Trace tick where the new preferred[] value was observed.</summary>
    public int tick { get; set; }

    /// <summary>MAME video frame counter sampled at the frame boundary.</summary>
    public int mameFrame { get; set; } = -1;

    /// <summary>Frame-boundary PC diagnostic only; not exact write PC.</summary>
    public string? pc { get; set; }

    /// <summary>Frame-boundary R-register diagnostic only; not exact LD A,R value.</summary>
    public string? r { get; set; }

    /// <summary>Preferred[] RAM address, usually 61C4..61C7.</summary>
    public string? addr { get; set; }

    /// <summary>Preferred[] slot index: 0 for 61C4, 1 for 61C5, etc.</summary>
    public int slot { get; set; }

    /// <summary>Value observed in the previous captured frame.</summary>
    public int old { get; set; } = -1;

    /// <summary>Value observed in the current captured frame.</summary>
    public int @new { get; set; } = -1;

    /// <summary>Full preferred[] snapshot before the transition.</summary>
    public List<int> preferredBefore { get; set; } = new();

    /// <summary>Full preferred[] snapshot after the transition.</summary>
    public List<int> preferredAfter { get; set; } = new();
}
