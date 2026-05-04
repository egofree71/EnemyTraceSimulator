/// <summary>
/// Optional filters applied after comparing a MAME reference sequence with a simulation sequence.
/// These are useful while the simulation adapter is built incrementally.
/// </summary>
public sealed class TraceComparisonOptions
{
    public bool IgnoreEnemyWork { get; init; }
    public bool IgnoreEnemyWorkPreferred { get; init; }

    public bool HasActiveFilters => IgnoreEnemyWork || IgnoreEnemyWorkPreferred;

    public string Summary
    {
        get
        {
            if (!HasActiveFilters)
                return string.Empty;

            if (IgnoreEnemyWork)
                return "all EnemyWork mismatches ignored";

            if (IgnoreEnemyWorkPreferred)
                return "EnemyWork preferred[] mismatches ignored";

            return string.Empty;
        }
    }

    public bool ShouldIgnore(TraceMismatch mismatch)
    {
        if (IgnoreEnemyWork && mismatch.Kind == TraceMismatchKind.EnemyWork)
            return true;

        if (IgnoreEnemyWorkPreferred &&
            mismatch.Kind == TraceMismatchKind.EnemyWork &&
            mismatch.Field.StartsWith("preferred"))
        {
            return true;
        }

        return false;
    }
}
