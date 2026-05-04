using System.Collections.Generic;

/// <summary>
/// Reference-direction step skeleton for the real Lady Bug enemy simulation adapter.
///
/// The adapter now creates frames from its own mutable simulation state instead of
/// directly returning an identity trace. It advances through the reference trace
/// with a first minimal tick hook. Player, ports, gates, timers, and enemy control state are synced from MAME; active enemies are advanced by one pixel using the reference direction.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "Build the future Lady Bug simulation state from the trace. " +
        "AdvanceOneTick syncs reference controls and moves active enemies by one pixel using the MAME direction.";

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
        {
            if (i > 0)
                simulationState.AdvanceOneTick(referenceFrames[i]);

            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));
        }

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-direction step; initial state: " + initialState.Summary +
            "; AdvanceOneTick syncs player, ports, gates, timers, and enemy control state; " +
            "active enemies move one pixel using the MAME direction; " +
            "enemyWork and real enemy decision logic are not implemented yet");
    }
}
