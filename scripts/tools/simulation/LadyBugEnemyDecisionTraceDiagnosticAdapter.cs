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
/// - uses the previous frame's EnemyWork temp position as the pre-move decision state
///   for normal continuous enemy updates;
/// - treats first activation / den-exit transitions separately, because exact-PC
///   logging showed that previousFrame.enemyWork is stale and not the start state
///   of the newly active slot;
/// - uses the 0x3061 source release shape plus the exact-PC cycle-0 result as the
///   first-activation start state: startTmp=08:58,86, preferred=02, 4315 writes
///   rejectedMask |= 02, current direction 08 is kept, then 43D4 commits 08:58,87;
/// - validates the 0x4315 case where preferred is rejected but current direction is kept;
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
        public int ReleaseActivationTransitions;
        public int ReleaseActivationModeledFromExactPc;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int PreferredAndPreviousRejectedFallback;
        public int ReverseIgnored;
        public int NoPreferredCandidate;
        public int ReferenceRejectedMaskNonZero;
        public int RejectedMaskMatchesModeled;
        public int RejectedMaskDiffersFromModeled;
        public string FirstRejectedMaskDifference = string.Empty;
        public string FirstReleaseActivationModeled = string.Empty;
        public readonly Dictionary<string, int> Sources = new();
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return "Lady Bug decision diagnostics v0.6.92: empty trace";

        var counters = new Counters { Frames = referenceFrames.Count };

        for (int i = 1; i < referenceFrames.Count; i++)
            AnalyzeTransition(referenceFrames[i - 1], referenceFrames[i], counters);

        return BuildSummaryText(counters);
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

        if (TryModelFirstActivationFromReleaseCycle(
                previousFrame,
                currentFrame,
                enemy,
                currentTempDir,
                preferred,
                counters,
                out modeledRejected,
                out source))
        {
            // Exact-PC v0.6.91 proved that the first active frame is not a normal
            // previousFrame.enemyWork -> currentFrame.enemyWork transition.  Its
            // real cycle start is the 0x3061 den-release shape at 08:58,86, which
            // is a decision-center position.
            counters.CenterTransitions++;
        }
        else if (!LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(previousTempX, previousTempY))
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
    /// Models the first active enemy transition using the source release shape and
    /// exact-PC release/decision log, rather than previousFrame.enemyWork.
    ///
    /// Exact-PC v0.6.91 showed the first cycle as:
    ///   startTmp=08:58,86, preferred=[02,02,02,02]
    ///   4315_REJECT_OR_CANDIDATE writes A=02 to rejectedMask
    ///   no 4331 and no 4241 fallback entry
    ///   current direction 08 is kept
    ///   43D4 commits 08:58,87
    ///
    /// Therefore the standard JSONL transition must not treat the stale previous
    /// EnemyWork value as the pre-move state for this newly active slot.
    /// </summary>
    private static bool TryModelFirstActivationFromReleaseCycle(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        EnemyTraceActor enemy,
        int currentTempDir,
        int preferred,
        Counters counters,
        out int modeledRejected,
        out string source)
    {
        modeledRejected = 0;
        source = string.Empty;

        if (!LadyBugEnemyReleaseModel.TryObserveActivationTransition(
                previousFrame,
                currentFrame,
                out LadyBugEnemyReleaseModel.ReleaseTransitionObservation? observation) ||
            observation == null)
        {
            return false;
        }

        if (observation.Slot != enemy.slot || !observation.MatchesEnemyWorkAfterFirstStep)
            return false;

        counters.ReleaseActivationTransitions++;

        if (!LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(
                LadyBugEnemyReleaseModel.SourceReleaseX,
                LadyBugEnemyReleaseModel.SourceReleaseY))
        {
            source = "3061_RELEASE_START_NOT_CENTER";
            return false;
        }

        counters.ReleaseActivationModeledFromExactPc++;

        if (string.IsNullOrEmpty(counters.FirstReleaseActivationModeled))
        {
            counters.FirstReleaseActivationModeled =
                observation.Summary +
                " exactPcCycle=startTmp=08:58,86 preferred=" + FormatByte(preferred) +
                " currentKept=" + FormatByte(currentTempDir);
        }

        if (!IsDirectionBit(preferred))
        {
            source = "3061_RELEASE_NO_PREFERRED";
            return true;
        }

        if (preferred == currentTempDir)
        {
            source = "3061_RELEASE_PREFERRED_ACCEPTED";
            return true;
        }

        // Source-first interpretation from exact-PC cycle 0:
        // the preferred candidate is rejected at 0x4315, but current dir 08 is
        // then accepted/kept.  This is not a special ignore filter; it is the same
        // 4315-only pattern as the later non-release current-kept cycle.
        modeledRejected = preferred & 0x0F;
        source = "3061_4315_RELEASE_PREFERRED_REJECTED_CURRENT_KEPT";
        return true;
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
            case "3061_RELEASE_PREFERRED_ACCEPTED":
                counters.PreferredAccepted++;
                break;
            case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
            case "3061_4315_RELEASE_PREFERRED_REJECTED_CURRENT_KEPT":
                counters.PreferredRejectedCurrentKept++;
                break;
            case "4315_4331_PREFERRED_AND_PREVIOUS_REJECTED":
                counters.PreferredAndPreviousRejectedFallback++;
                break;
            case "DECISION_CENTER_REVERSE_IGNORED":
                counters.ReverseIgnored++;
                break;
            case "DECISION_CENTER_NO_PREFERRED":
            case "3061_RELEASE_NO_PREFERRED":
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

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();

        builder.Append("Lady Bug decision diagnostics v0.6.92 transition model: ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", enemyWorkTransitions=").Append(counters.TransitionsWithEnemyWork);
        builder.Append(", attributedTransitions=").Append(counters.AttributedTransitions);
        builder.Append(", centerTransitions=").Append(counters.CenterTransitions);
        builder.Append(", outsideCenterTransitions=").Append(counters.OutsideCenterTransitions);
        builder.Append(", releaseActivationTransitions=").Append(counters.ReleaseActivationTransitions);
        builder.Append(", releaseActivationModeledFromExactPc=").Append(counters.ReleaseActivationModeledFromExactPc);
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

        if (!string.IsNullOrEmpty(counters.FirstReleaseActivationModeled))
            builder.Append(", firstReleaseActivationModeled: ").Append(counters.FirstReleaseActivationModeled);

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

        builder.Append(". NOTE: this is diagnostic-only. It does not change movement, comparison frames, or the existing reference-sync bridges. The key source-first checks are 4315_PREFERRED_REJECTED_CURRENT_KEPT and 3061_4315_RELEASE_PREFERRED_REJECTED_CURRENT_KEPT: preferred is ORed into 0x61C1 even when the current direction is kept.");
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
