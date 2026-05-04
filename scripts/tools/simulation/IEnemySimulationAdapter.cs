using System.Collections.Generic;

/// <summary>
/// Adapter contract for producing simulation frames from a loaded MAME reference trace.
/// The first real Lady Bug enemy simulation adapter will plug into this interface in v0.6.
/// </summary>
public interface IEnemySimulationAdapter
{
    string Name { get; }
    string Description { get; }
    bool ExpectedToMismatch { get; }

    SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames);
}
