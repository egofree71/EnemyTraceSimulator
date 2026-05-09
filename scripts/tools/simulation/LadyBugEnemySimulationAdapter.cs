using System.Collections.Generic;

/// <summary>
/// Default Lady Bug simulation adapter.
///
/// This class intentionally stays as the stable UI-facing adapter name used by
/// EnemyTraceSimulatorWindow and Compare.  The actual implementation is delegated
/// to <see cref="LadyBugSourcePathSingleEnemyReplayAdapter"/>.
///
/// Current default, v0.9.10:
/// - compute the active mono-enemy transition through the validated source path;
/// - keep release timing, player, gates, timers, preferred[], VRAM context,
///   inactive slots, and multi-enemy frames synchronized from the reference trace;
/// - stop pretending that the old reference-direction step is the main candidate.
///
/// This is still not the final autonomous Lady Bug enemy AI. It is the validated
/// single-enemy candidate replay used before removing more trace-synced inputs.
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
