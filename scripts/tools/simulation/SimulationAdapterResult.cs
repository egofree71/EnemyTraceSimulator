using System.Collections.Generic;

/// <summary>
/// Result produced by a simulation adapter before comparison.
/// </summary>
public sealed class SimulationAdapterResult
{
    public List<SimulationFrame> Frames { get; } = new();
    public string Summary { get; init; } = string.Empty;

    public SimulationAdapterResult()
    {
    }

    public SimulationAdapterResult(IEnumerable<SimulationFrame> frames, string summary)
    {
        Frames.AddRange(frames);
        Summary = summary;
    }
}
