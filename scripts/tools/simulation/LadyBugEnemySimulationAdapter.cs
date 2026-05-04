using System.Collections.Generic;

/// <summary>
/// First skeleton for the real Lady Bug enemy simulation adapter.
///
/// The adapter now creates frames from its own mutable simulation state instead of
/// directly returning an identity trace. The state is intentionally frozen for now:
/// no real enemy movement is applied yet.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug adapter skeleton";

    public string Description =>
        "Build the future Lady Bug simulation initial state from the trace. " +
        "For now, the simulation state is frozen, so it should diverge once the arcade state changes.";

    public bool ExpectedToMismatch => true;

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
            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));

        return new SimulationAdapterResult(
            frames,
            "Lady Bug adapter skeleton; initial state: " + initialState.Summary +
            "; simulation state is frozen; expected to mismatch when MAME moves or changes state");
    }
}
