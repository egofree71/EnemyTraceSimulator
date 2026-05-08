using System.Collections.Generic;

/// <summary>
/// Concise reference-direction validation adapter for the v0.9.x comparison work.
///
/// This adapter intentionally keeps the visible side-by-side replay stable:
/// active enemies still move using the MAME reference direction. The diagnostic
/// appended to the Compare report is a transition-based source-path inspector.
///
/// v0.9.8 keeps the inspector single-enemy-safe: transitions with more than one
/// active enemy are explicitly skipped and counted, rather than being interpreted
/// as if the single-enemy attribution rules still applied.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "v0.9.8 concise replay: keep the reference-direction visual comparison stable, " +
        "then run the transition-based source-path decision inspector in single-enemy-safe mode. " +
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
            "Lady Bug reference-direction replay v0.9.8; " +
            "initial state: " + initialState.Summary + "; " +
            "normal Compare no longer appends legacy exact-PC/shadow summaries; " +
            "error.log is not used by this adapter; " +
            "all-four-directions collision comparison is disabled in this adapter because it can produce false positives; " +
            sourcePathInspection + "; " +
            "real autonomous enemy AI is not enabled yet");
    }
}
