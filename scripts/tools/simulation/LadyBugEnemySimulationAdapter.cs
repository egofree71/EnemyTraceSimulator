using System.Collections.Generic;

/// <summary>
/// Default Lady Bug simulation adapter for the v0.9.13 diagnostic package.
///
/// The default visual candidate remains the v0.9.12 source-path single-enemy
/// replay adapter:
///
/// - one active enemy movement is computed through the reconstructed source path;
/// - deterministic 0x2E97 rotate preferred[] tuples are modeled and used;
/// - random/R-register and BFS preferred[] cases are still trace-synced.
///
/// v0.9.13 adds a diagnostic-only random/R-low feasibility probe. It does not
/// change replay behavior. Its job is to measure whether the random 0x2EC7
/// preferred[] cases can be predicted from the R values already present in the
/// standard JSONL trace, before we attempt to remove that trace-sync dependency.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    private readonly LadyBugSourcePathSingleEnemyReplayAdapter _inner = new();

    public string Name => _inner.Name;

    public string Description =>
        _inner.Description + " v0.9.13 appends preferred[] preflight and random/R-low predictability diagnostics.";

    public bool ExpectedToMismatch => _inner.ExpectedToMismatch;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        SimulationAdapterResult result = _inner.Run(referenceFrames);
        string preferredSummary = LadyBugPreferredTraceClassifier.BuildSummary(referenceFrames);
        string randomSummary = LadyBugPreferredRandomRLowProbe.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            result.Frames,
            result.Summary + "; " + preferredSummary + "; " + randomSummary);
    }
}
