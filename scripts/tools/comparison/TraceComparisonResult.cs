using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Aggregate comparison result for a full trace run.
/// </summary>
public sealed class TraceComparisonResult
{
    public List<ComparisonFrame> Frames { get; } = new();
    public int ComparedFrameCount { get; set; }

    public int MismatchCount => Frames.Sum(frame => frame.Mismatches.Count);

    public bool HasMismatch => FirstMismatch != null;

    public TraceMismatch? FirstMismatch
    {
        get
        {
            foreach (ComparisonFrame frame in Frames)
            {
                if (frame.Mismatches.Count > 0)
                    return frame.Mismatches[0];
            }

            return null;
        }
    }
}
