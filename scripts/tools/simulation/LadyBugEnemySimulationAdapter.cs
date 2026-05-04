using System.Collections.Generic;

/// <summary>
/// First skeleton for the real Lady Bug enemy simulation adapter.
///
/// The adapter now creates frames from its own mutable simulation state instead of
/// directly returning an identity trace. It advances through the reference trace
/// with a first minimal tick hook. Player, ports, gates, and timers are synced from MAME; real enemy movement is not applied yet.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug adapter skeleton";

    public string Description =>
        "Build the future Lady Bug simulation state from the trace. " +
        "AdvanceOneTick currently syncs external player, ports, gates, and timers.";

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
            "Lady Bug adapter skeleton; initial state: " + initialState.Summary +
            "; AdvanceOneTick currently syncs external player, ports, gates, and timers; " +
            "enemy and enemyWork logic are not implemented yet");
    }
}
