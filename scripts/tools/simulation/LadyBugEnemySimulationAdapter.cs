using System.Collections.Generic;

/// <summary>
/// Default Lady Bug simulation adapter for the v0.9.15 random-mode package.
///
/// The visual candidate now remains the source-path single-enemy replay, with a
/// slightly larger modeled preferred[] subset:
///
/// - one active enemy movement is computed through the reconstructed source path;
/// - deterministic 0x2E97 rotate preferred[] tuples are modeled and used;
/// - visible 0x477D BFS/chase overrides are modeled from the source 0x45DC
///   coordinate-to-logical-maze guidance path and used when safe;
/// - random/R-register base tuples are trace-synced by default, or generated with a seeded C# Random when behavior mode is enabled.
///
/// The appended preflights remain diagnostic-only. They keep reporting coverage
/// and reverse-engineering confidence, but the actual replay result comes from
/// LadyBugSourcePathSingleEnemyReplayAdapter.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    private readonly LadyBugSourcePathSingleEnemyReplayAdapter _inner = new();

    public string Name => _inner.Name;

    public string Description =>
        _inner.Description + " v0.9.15 appends preferred[], random/R-low and BFS/chase diagnostics, plus optional C# random behavior mode.";

    public bool ExpectedToMismatch => _inner.ExpectedToMismatch;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        SimulationAdapterResult result = _inner.Run(referenceFrames);
        string preferredSummary = LadyBugPreferredTraceClassifier.BuildSummary(referenceFrames);
        string randomSummary = LadyBugPreferredRandomRLowProbe.BuildSummary(referenceFrames);
        string bfsSummary = LadyBugPreferredBfsChasePreflight.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            result.Frames,
            result.Summary + "; " + preferredSummary + "; " + randomSummary + "; " + bfsSummary);
    }
}
