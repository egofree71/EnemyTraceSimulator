using System.Collections.Generic;

/// <summary>
/// Default Lady Bug simulation adapter for the v0.9.9b milestone.
///
/// The normal visual replay now uses the source-path single-enemy replay adapter:
/// it computes slot-0/mono-active enemy movement through the source-first path
/// validated by v0.9.6-v0.9.8, while still syncing release timing, player,
/// gates, timers, inactive slots, and out-of-scope multi-enemy frames from the
/// reference trace.
///
/// This is not the final autonomous enemy AI yet. It is the first step where the
/// visible candidate replay no longer advances the active single enemy simply by
/// copying the reference direction.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    private readonly LadyBugSourcePathSingleEnemyReplayAdapter _inner = new();

    public string Name => _inner.Name;

    public string Description => _inner.Description;

    public bool ExpectedToMismatch => _inner.ExpectedToMismatch;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        return _inner.Run(referenceFrames);
    }
}
