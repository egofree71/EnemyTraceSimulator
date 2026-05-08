using System.Collections.Generic;

/// <summary>
/// Concise reference-direction validation adapter for the v0.9.x comparison work.
///
/// This adapter intentionally keeps the old visible comparison path alive:
/// active enemies still move using the MAME reference direction, so the existing
/// side-by-side replay remains stable.
///
/// v0.9.0c changes the Compare report strategy:
/// - no long v0.6/v0.7/v0.8 shadow summaries are appended by default;
/// - no exact-PC preferred[] tape is loaded by this adapter;
/// - the report focuses on the current milestone: static maze direction
///   availability at enemy decision centers.
///
/// The old diagnostic classes remain in the project for manual investigation,
/// but this adapter no longer calls them during the normal Compare action.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "v0.9.0c concise replay: keep the reference-direction visual comparison stable, " +
        "then report the static maze collision comparison at enemy decision centers, excluding gate-local probes.";

    public bool ExpectedToMismatch => false;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return new SimulationAdapterResult(new List<SimulationFrame>(), "empty trace; no initial state created");

        LadyBugSimulationInitialState initialState =
            LadyBugSimulationInitialState.FromFirstFrame(referenceFrames[0]);

        LadyBugSimulationState simulationState =
            LadyBugSimulationState.FromInitialState(initialState);

        var frames = new List<SimulationFrame>(referenceFrames.Count);
        for (int i = 0; i < referenceFrames.Count; i++)
        {
            if (i > 0)
                simulationState.AdvanceOneTick(referenceFrames[i], i);

            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));
        }

        string staticMazeCollisionComparison =
            LadyBugStaticMazeCollisionComparisonModel.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-direction replay v0.9.0c; " +
            "initial state: " + initialState.Summary + "; " +
            "normal Compare no longer appends legacy exact-PC/shadow summaries; " +
            "error.log is not used by this adapter; " +
            staticMazeCollisionComparison + "; " +
            "real autonomous enemy AI is not enabled yet");
    }
}
