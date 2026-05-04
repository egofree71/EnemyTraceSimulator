/// <summary>
/// Optional filters applied after comparing a MAME reference sequence with a simulation sequence.
/// These are useful while the simulation adapter is built incrementally.
/// </summary>
public sealed class TraceComparisonOptions
{
    public bool IgnoreEnemyWork { get; init; }

    public bool HasActiveFilters => IgnoreEnemyWork;

    public string Summary
    {
        get
        {
            if (!HasActiveFilters)
                return string.Empty;

            return IgnoreEnemyWork
                ? "EnemyWork mismatches ignored"
                : string.Empty;
        }
    }

    public bool ShouldIgnore(TraceMismatch mismatch)
    {
        return IgnoreEnemyWork && mismatch.Kind == TraceMismatchKind.EnemyWork;
    }
}
