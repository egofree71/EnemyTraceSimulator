using System.Collections.Generic;

/// <summary>
/// Temporary adapter that mirrors the MAME trace into simulation frames.
/// It validates the comparison pipeline and should produce zero mismatches.
/// </summary>
public sealed class IdentityTraceSimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "identity comparison";
    public string Description => "Compare the MAME trace against a simulation generated from the same trace.";
    public bool ExpectedToMismatch => false;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        List<SimulationFrame> frames = TraceSimulationStub.CreateIdentitySimulation(referenceFrames);
        return new SimulationAdapterResult(frames, "identity trace simulation; expected mismatches = 0");
    }
}
