/// <summary>
/// Result of comparing the visual gameplay state displayed by the simulator
/// against the same tick in the MAME reference trace.
/// </summary>
public sealed class VisualReplayMismatch
{
    public static readonly VisualReplayMismatch None = new()
    {
        HasMismatch = false,
        Category = "none",
        Details = "visual states match"
    };

    public bool HasMismatch { get; init; }
    public int FrameIndex { get; init; } = -1;
    public int Tick { get; init; } = -1;
    public string Category { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;

    public override string ToString()
    {
        if (!HasMismatch)
            return "visual states match";

        return $"Visual mismatch at tick {Tick} / frameIndex {FrameIndex}: {Category}: {Details}";
    }
}
