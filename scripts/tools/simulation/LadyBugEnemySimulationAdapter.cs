using System.Collections.Generic;

/// <summary>
/// First skeleton for the real Lady Bug enemy simulation adapter.
///
/// This class already owns the future adapter entry point and builds a typed initial
/// state from the MAME trace. It still mirrors the reference trace as its output until
/// the actual enemy movement classes are connected.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug adapter skeleton";

    public string Description =>
        "Build the future Lady Bug simulation initial state from the trace. " +
        "For now, output is still mirrored from MAME until the real enemy logic is connected.";

    public bool ExpectedToMismatch => false;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return new SimulationAdapterResult(new List<SimulationFrame>(), "empty trace; no initial state created");

        LadyBugSimulationInitialState initialState =
            LadyBugSimulationInitialState.FromFirstFrame(referenceFrames[0]);

        List<SimulationFrame> frames = TraceSimulationStub.CreateIdentitySimulation(referenceFrames);

        return new SimulationAdapterResult(
            frames,
            "Lady Bug adapter skeleton; initial state: " + initialState.Summary +
            "; output currently mirrors MAME trace");
    }
}
