using System.Collections.Generic;

/// <summary>
/// Reference-direction validation adapter for the future real Lady Bug enemy simulation.
///
/// The adapter creates frames from its own mutable simulation state. It advances
/// through the reference trace with reference-driven enemy direction and partial
/// EnemyWork reconstruction. preferred[], chase timers, and chase round-robin are
/// temporarily synced from MAME while their real arcade generators are pending.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "Build the future Lady Bug simulation state from the trace. " +
        "AdvanceOneTick syncs reference controls, moves active enemies by one pixel using the MAME direction, and updates first EnemyWork fields, with preferred[] temporarily synced from the reference trace.";

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
                simulationState.AdvanceOneTick(referenceFrames[i]);

            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));
        }

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-synced EnemyWork checkpoint; initial state: " + initialState.Summary +
            "; AdvanceOneTick syncs player, ports, gates, timers, and enemy control state; " +
            "active enemies move one pixel using the MAME direction; " +
            "EnemyWork tempDir/tempX/tempY, transient rejectedMask, preferred fallback pair and preferred[] is temporarily synced from the reference trace; " +
            "real enemy decision logic is not implemented yet");
    }
}
