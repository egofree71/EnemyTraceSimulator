using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Diagnostic comparison adapter for the source-first enemy decision model.
///
/// Important: this adapter intentionally returns identity simulation frames so
/// the normal visual/comparison pipeline should still produce zero mismatches.
/// Its value is in the Summary string: it runs transition-oriented diagnostics
/// over the loaded MAME trace and reports which 0x61C1 / rejectedMask outcomes
/// can already be explained by the source-first 0x42E6 / 0x4315 / 0x4331 model.
///
/// Current scope:
/// - uses the previous frame's EnemyWork temp position as the pre-move decision state;
/// - uses the current frame's preferred[] as the candidate visible at frame end;
/// - validates the 0x4315 case where preferred is rejected but current direction is kept;
/// - reports source-first enemy release / den-exit observations separately, without
///   changing the rejectedMask transition counts;
/// - keeps 0x4130 local-door validation out of this standard-JSONL diagnostic until
///   0x3C0A tile lookup is reconstructed from VRAM.
/// </summary>
public sealed class LadyBugEnemyDecisionTraceDiagnosticAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug decision diagnostics";

    public string Description =>
        "Runs source-first enemy decision diagnostics in parallel and returns identity frames. " +
        "Use this to inspect rejectedMask transition modeling without changing the current comparison result.";

    public bool ExpectedToMismatch => false;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        List<SimulationFrame> frames = TraceSimulationStub.CreateIdentitySimulation(referenceFrames);
        string summary = LadyBugEnemyDecisionTraceDiagnostics.BuildSummary(referenceFrames);
        return new SimulationAdapterResult(frames, summary);
    }
}

public static class LadyBugEnemyDecisionTraceDiagnostics
{
    private sealed class Counters
    {
        public int Frames;
        public int Transitions;
        public int TransitionsWithEnemyWork;
        public int AttributedTransitions;
        public int MissingPreferredSkips;
        public int CenterTransitions;
        public int OutsideCenterTransitions;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int PreferredAndPreviousRejectedFallback;
        public int ReverseIgnored;
        public int NoPreferredCandidate;
        public int ReferenceRejectedMaskNonZero;
        public int RejectedMaskMatchesModeled;
        public int RejectedMaskDiffersFromModeled;
        public string FirstRejectedMaskDifference = string.Empty;
        public readonly Dictionary<string, int> Sources = new();
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return "Lady Bug decision diagnostics v0.6.90: empty trace";

        var counters = new Counters { Frames = referenceFrames.Count };

        for (int i = 1; i < referenceFrames.Count; i++)
            AnalyzeTransition(referenceFrames[i - 1], referenceFrames[i], counters);

        LadyBugEnemyReleaseModel.ReleaseDiagnostics releaseDiagnostics =
            LadyBugEnemyReleaseModel.BuildDiagnostics(referenceFrames);

        return BuildSummaryText(counters, releaseDiagnostics);
    }

    private static void AnalyzeTransition(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        Counters counters)
    {
        counters.Transitions++;

        if (previousFrame.enemyWork == null || currentFrame.enemyWork == null)
            return;

        counters.TransitionsWithEnemyWork++;

        if (currentFrame.enemyWork.rejectedMask != 0)
            counters.ReferenceRejectedMaskNonZero++;

        if (!TrySelectEnemyWorkSlot(currentFrame, out EnemyTraceActor? enemy) || enemy == null)
            return;

        counters.AttributedTransitions++;

        int slot = Math.Clamp(enemy.slot, 0, 3);
        if (currentFrame.enemyWork.preferred.Count <= slot)
        {
            counters.MissingPreferredSkips++;
            return;
        }

        int previousTempDir = previousFrame.enemyWork.tempDir & 0x0F;
        int previousTempX = previousFrame.enemyWork.tempX & 0xFF;
        int previousTempY = previousFrame.enemyWork.tempY & 0xFF;
        int currentTempDir = currentFrame.enemyWork.tempDir & 0x0F;
        int preferred = currentFrame.enemyWork.preferred[slot] & 0x0F;

        int modeledRejected;
        string source;

        if (!LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(previousTempX, previousTempY))
        {
            counters.OutsideCenterTransitions++;
            modeledRejected = 0;
            source = "PLAIN_STEP_OUTSIDE_CENTER";
        }
        else
        {
            counters.CenterTransitions++;
            modeledRejected = ModelRejectedMaskAtDecisionCenter(
                previousTempDir,
                currentTempDir,
                preferred,
                out source);
        }

        CountSource(counters, source);
        CountDecisionClass(counters, source);

        int referenceRejected = currentFrame.enemyWork.rejectedMask & 0x0F;
        if (referenceRejected == modeledRejected)
        {
            counters.RejectedMaskMatchesModeled++;
        }
        else
        {
            counters.RejectedMaskDiffersFromModeled++;
            if (string.IsNullOrEmpty(counters.FirstRejectedMaskDifference))
            {
                counters.FirstRejectedMaskDifference = BuildRejectedMaskDifferenceContext(
                    previousFrame,
                    currentFrame,
                    enemy,
                    preferred,
                    previousTempDir,
                    currentTempDir,
                    source,
                    referenceRejected,
                    modeledRejected);
            }
        }
    }

    /// <summary>
    /// Source-first transition model for the 0x42E6 decision path as visible in
    /// standard JSONL frame boundaries.
    ///
    /// The important correction compared with the older shadow heuristic is the
    /// 0x4315 path:
    ///
    ///     preferred rejected -> 0x61C1 |= preferred
    ///     current direction still valid -> keep current and return
    ///
    /// So when preferred differs from the final/current temp direction, and the
    /// enemy kept its previous direction, the preferred bit is still modeled as
    /// rejected unless the observed shape is the explicit reverse-ignored case.
    /// </summary>
    private static int ModelRejectedMaskAtDecisionCenter(
        int previousTempDir,
        int currentTempDir,
        int preferred,
        out string source)
    {
        if (!IsDirectionBit(preferred))
        {
            source = "DECISION_CENTER_NO_PREFERRED";
            return 0;
        }

        if (preferred == currentTempDir)
        {
            source = "42E6_PREFERRED_ACCEPTED";
            return 0;
        }

        if (previousTempDir == currentTempDir &&
            AreOppositeDirections(preferred, currentTempDir))
        {
            source = "DECISION_CENTER_REVERSE_IGNORED";
            return 0;
        }

        int rejectedMask = preferred & 0x0F;

        if (IsDirectionBit(previousTempDir) && previousTempDir != currentTempDir)
        {
            source = "4315_4331_PREFERRED_AND_PREVIOUS_REJECTED";
            return (rejectedMask | previousTempDir) & 0x0F;
        }

        source = "4315_PREFERRED_REJECTED_CURRENT_KEPT";
        return rejectedMask;
    }

    private static bool TrySelectEnemyWorkSlot(EnemyTraceFrame frame, out EnemyTraceActor? selected)
    {
        selected = null;

        if (frame.enemies == null || frame.enemyWork == null)
            return false;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (!enemy.active || !enemy.HasKnownPosition)
                continue;

            if (enemy.x == frame.enemyWork.tempX && enemy.y == frame.enemyWork.tempY)
            {
                selected = enemy;
                return true;
            }
        }

        EnemyTraceActor? onlyActive = null;
        int activeCount = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (!enemy.active || !enemy.HasKnownPosition)
                continue;

            activeCount++;
            onlyActive = enemy;
        }

        if (activeCount == 1)
        {
            selected = onlyActive;
            return true;
        }

        return false;
    }

    private static void CountDecisionClass(Counters counters, string source)
    {
        switch (source)
        {
            case "42E6_PREFERRED_ACCEPTED":
                counters.PreferredAccepted++;
                break;
            case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
                counters.PreferredRejectedCurrentKept++;
                break;
            case "4315_4331_PREFERRED_AND_PREVIOUS_REJECTED":
                counters.PreferredAndPreviousRejectedFallback++;
                break;
            case "DECISION_CENTER_REVERSE_IGNORED":
                counters.ReverseIgnored++;
                break;
            case "DECISION_CENTER_NO_PREFERRED":
                counters.NoPreferredCandidate++;
                break;
        }
    }

    private static void CountSource(Counters counters, string source)
    {
        if (!counters.Sources.TryGetValue(source, out int count))
            count = 0;

        counters.Sources[source] = count + 1;
    }

    private static string BuildRejectedMaskDifferenceContext(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        EnemyTraceActor enemy,
        int preferred,
        int previousTempDir,
        int currentTempDir,
        string source,
        int referenceRejected,
        int modeledRejected)
    {
        return "tick=" + currentFrame.frame +
               " mameFrame=" + currentFrame.mameFrame +
               " slot=" + enemy.slot +
               " prevTmp=" + FormatByte(previousTempDir) + ":" +
               FormatByte(previousFrame.enemyWork?.tempX ?? 0) + "," +
               FormatByte(previousFrame.enemyWork?.tempY ?? 0) +
               " currTmp=" + FormatByte(currentTempDir) + ":" +
               FormatByte(currentFrame.enemyWork?.tempX ?? 0) + "," +
               FormatByte(currentFrame.enemyWork?.tempY ?? 0) +
               " preferred=" + FormatByte(preferred) +
               " source=" + source +
               " referenceRejected=" + FormatByte(referenceRejected) +
               " modeledRejected=" + FormatByte(modeledRejected);
    }

    private static string BuildSummaryText(
        Counters counters,
        LadyBugEnemyReleaseModel.ReleaseDiagnostics releaseDiagnostics)
    {
        var builder = new StringBuilder();

        builder.Append("Lady Bug decision diagnostics v0.6.90 transition model: ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", enemyWorkTransitions=").Append(counters.TransitionsWithEnemyWork);
        builder.Append(", attributedTransitions=").Append(counters.AttributedTransitions);
        builder.Append(", centerTransitions=").Append(counters.CenterTransitions);
        builder.Append(", outsideCenterTransitions=").Append(counters.OutsideCenterTransitions);
        builder.Append(", missingPreferredSkips=").Append(counters.MissingPreferredSkips);
        builder.Append(", preferredAccepted=").Append(counters.PreferredAccepted);
        builder.Append(", preferredRejectedCurrentKept=").Append(counters.PreferredRejectedCurrentKept);
        builder.Append(", preferredAndPreviousRejectedFallback=").Append(counters.PreferredAndPreviousRejectedFallback);
        builder.Append(", reverseIgnored=").Append(counters.ReverseIgnored);
        builder.Append(", noPreferredCandidate=").Append(counters.NoPreferredCandidate);
        builder.Append(", referenceRejectedMaskNonZero=").Append(counters.ReferenceRejectedMaskNonZero);
        builder.Append(", rejectedMaskMatchesModeled=").Append(counters.RejectedMaskMatchesModeled);
        builder.Append(", rejectedMaskDiffersFromModeled=").Append(counters.RejectedMaskDiffersFromModeled);

        if (!string.IsNullOrEmpty(counters.FirstRejectedMaskDifference))
            builder.Append(", firstRejectedMaskDifference: ").Append(counters.FirstRejectedMaskDifference);

        builder.Append(", sources: ");
        if (counters.Sources.Count == 0)
        {
            builder.Append("none");
        }
        else
        {
            bool first = true;
            foreach (KeyValuePair<string, int> pair in counters.Sources)
            {
                if (!first)
                    builder.Append("; ");

                first = false;
                builder.Append(pair.Key).Append("=").Append(pair.Value);
            }
        }

        builder.Append(". NOTE: this is diagnostic-only. It does not change movement, comparison frames, or the existing reference-sync bridges. The key source-first check is 4315_PREFERRED_REJECTED_CURRENT_KEPT: preferred is ORed into 0x61C1 even when the current direction is kept. ");
        builder.Append(releaseDiagnostics.BuildSummaryText());
        return builder.ToString();
    }

    private static bool IsDirectionBit(int value)
    {
        int v = value & 0x0F;
        return v == 0x01 || v == 0x02 || v == 0x04 || v == 0x08;
    }

    private static bool AreOppositeDirections(int a, int b)
    {
        return ((a & 0x0F) == LadyBugEnemyDecisionModel.DirLeft && (b & 0x0F) == LadyBugEnemyDecisionModel.DirRight) ||
               ((a & 0x0F) == LadyBugEnemyDecisionModel.DirRight && (b & 0x0F) == LadyBugEnemyDecisionModel.DirLeft) ||
               ((a & 0x0F) == LadyBugEnemyDecisionModel.DirUp && (b & 0x0F) == LadyBugEnemyDecisionModel.DirDown) ||
               ((a & 0x0F) == LadyBugEnemyDecisionModel.DirDown && (b & 0x0F) == LadyBugEnemyDecisionModel.DirUp);
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }
}
