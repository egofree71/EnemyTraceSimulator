using System.Collections.Generic;

/// <summary>
/// Retired v0.9.1-v0.9.3 diagnostic model kept as a compile-safe compatibility shim.
///
/// The old gate-aware collision comparison probed all four directions at every
/// enemy decision center. That was useful while validating coordinate and gate
/// mapping, but it can produce false positives because the arcade source path
/// does not ask all four directions independently on every update.
///
/// The normal v0.9.6 Compare flow now uses LadyBugSourcePathDecisionInspector,
/// which follows the real transition path: previous slot state -> 0x427E ->
/// preferred/current/fallback -> one-pixel commit.
/// </summary>
public static class LadyBugGateAwareMazeCollisionComparisonModel
{
    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        int count = referenceFrames?.Count ?? 0;
        return "Lady Bug gate-aware collision comparison: retired in v0.9.6; " +
               $"frames={count}; " +
               "all-four-directions probe mode is disabled because it can produce false positives. " +
               "Use LadyBugSourcePathDecisionInspector instead.";
    }
}
