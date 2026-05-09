using System.Collections.Generic;

/// <summary>
/// Default Lady Bug simulation adapter for the v0.9.11 milestone.
///
/// The default visual candidate remains the source-path single-enemy replay
/// adapter. v0.9.11 appends a diagnostic-only preferred[] trace-sync preflight
/// so we can measure how well the observed preferred[] tuple is explained by
/// the reconstructed 0x2E5C / 0x2E97 / 0x2EC7 / 0x477D model before trying to
/// remove preferred[] trace synchronization.
///
/// This wrapper intentionally does not change visible replay logic.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    private readonly LadyBugSourcePathSingleEnemyReplayAdapter _inner = new();

    public string Name => _inner.Name;

    public string Description =>
        _inner.Description + " v0.9.11 also appends a diagnostic-only preferred[] trace-sync preflight.";

    public bool ExpectedToMismatch => _inner.ExpectedToMismatch;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        SimulationAdapterResult result = _inner.Run(referenceFrames);
        string preferredSummary = LadyBugPreferredTraceClassifier.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            result.Frames,
            result.Summary + "; " + preferredSummary);
    }
}
