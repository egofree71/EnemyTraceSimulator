using System.Collections.Generic;

/// <summary>
/// Concise reference-direction validation adapter for the v0.9.x comparison work.
///
/// This adapter intentionally keeps the visible side-by-side replay stable:
/// active enemies still move using the MAME reference direction. The important
/// v0.9.7 diagnostic is not a new runtime model; it is a transition-based
/// source-path inspector that explains which directions the source path actually
/// tested for each frame transition.
///
/// The old all-four-directions collision report is intentionally not appended in
/// this adapter because it can produce false positives: it asks directions that
/// the arcade source path did not necessarily test at that update.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "v0.9.7 concise replay: keep the reference-direction visual comparison stable, " +
        "then run the compact transition-based source-path decision inspector with mirrored vertical static-maze direction mapping. " +
        "The all-four-directions collision probe is disabled in the normal report.";

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

        string sourcePathInspection =
            LadyBugSourcePathDecisionInspector.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-direction replay v0.9.7; " +
            "initial state: " + initialState.Summary + "; " +
            "normal Compare no longer appends legacy exact-PC/shadow summaries; " +
            "error.log is not used by this adapter; " +
            "all-four-directions collision comparison is disabled in this adapter because it can produce false positives; " +
            sourcePathInspection + "; " +
            "real autonomous enemy AI is not enabled yet");
    }
}
