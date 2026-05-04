using System.Collections.Generic;

/// <summary>
/// Temporary adapter that deliberately changes one value to validate mismatch reporting.
/// </summary>
public sealed class InjectedMismatchSimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "injected mismatch test";
    public string Description => "Deliberately alter the first active enemy X coordinate to test mismatch reporting.";
    public bool ExpectedToMismatch => true;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        List<SimulationFrame> frames = TraceSimulationStub.CreateFirstActiveEnemyXMismatchSimulation(referenceFrames);
        return new SimulationAdapterResult(frames, "first active enemy X is intentionally offset by +1");
    }
}
