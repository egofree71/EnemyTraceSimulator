using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Source-first shadow diagnostic for the 0x42E6 / 0x4315 path where a preferred
/// direction is rejected but the current direction remains valid and is kept.
///
/// This is intentionally diagnostic-only. It does not replace the authoritative
/// reference-synced EnemyWork state yet.  Its purpose is to run the existing
/// LadyBugEnemyDecisionModel.TryPreferredDirection() transcription against the
/// two exact-PC validated current-kept shapes:
///
/// - release first cycle: startTmp=08:58,86, preferred=02, current 08 kept;
/// - normal decision cycle: startTmp=01:58,76, preferred=08, current 01 kept.
///
/// The local-door reader is permissive here because these two exact-PC cycles did
/// not enter 4187_LOCAL_DOOR_REJECT.  Door/local-tile authoritative validation is
/// deliberately left for the later 0x3C0A / 0x4130 reconstruction step.
/// </summary>
public static class LadyBugEnemySourceFirst4315ShadowModel
{
    private const string Version = "v0.6.93";

    private sealed class Counters
    {
        public int Frames;
        public int Transitions;
        public int ReleaseActivationTransitions;
        public int ReleaseChecks;
        public int NormalCenterCandidates;
        public int NormalCurrentKeptChecks;
        public int Checks;
        public int Matches;
        public int Mismatches;
        public int SkippedMissingEnemyWork;
        public int SkippedMissingPreferred;
        public int SkippedNonCenter;
        public int SkippedNotCurrentKept;
        public string FirstMatch = string.Empty;
        public string FirstMismatch = string.Empty;
        public readonly Dictionary<string, int> Sources = new();
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
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

        if (currentFrame.enemyWork == null)
        {
            counters.SkippedMissingEnemyWork++;
            return;
        }

        if (LadyBugEnemyReleaseModel.TryObserveActivationTransition(
                previousFrame,
                currentFrame,
                out LadyBugEnemyReleaseModel.ReleaseTransitionObservation? releaseObservation) &&
            releaseObservation != null &&
            releaseObservation.MatchesEnemyWorkAfterFirstStep)
        {
            counters.ReleaseActivationTransitions++;
            AnalyzeReleaseCurrentKeptCycle(currentFrame, releaseObservation, counters);
            return;
        }

        AnalyzeNormalCurrentKeptCycle(currentFrame, counters);
    }

    private static void AnalyzeReleaseCurrentKeptCycle(
        EnemyTraceFrame currentFrame,
        LadyBugEnemyReleaseModel.ReleaseTransitionObservation observation,
        Counters counters)
    {
        counters.ReleaseChecks++;

        int preferred = SelectPreferredForSlot(currentFrame, observation.Slot, out bool hasPreferred);
        if (!hasPreferred)
        {
            counters.SkippedMissingPreferred++;
            return;
        }

        // Exact-PC v0.6.91 showed that the visible first active frame corresponds
        // to a cycle that started at the 0x3061 position, then executed the usual
        // 0x42E6/0x4315 current-kept path before the 0x43BA movement step.
        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = LadyBugEnemyReleaseModel.SourceReleaseDir,
            TempX = LadyBugEnemyReleaseModel.SourceReleaseX,
            TempY = LadyBugEnemyReleaseModel.SourceReleaseY,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        RunAndCompare(
            currentFrame,
            scratch,
            preferred,
            "3061_4315_RELEASE_SOURCE_MODEL_CURRENT_KEPT",
            counters);
    }

    private static void AnalyzeNormalCurrentKeptCycle(
        EnemyTraceFrame currentFrame,
        Counters counters)
    {
        if (!TrySelectEnemyWorkSlot(currentFrame, out EnemyTraceActor? enemy) || enemy == null)
            return;

        if (currentFrame.enemyWork == null)
            return;

        int slot = Math.Clamp(enemy.slot, 0, 3);
        int preferred = SelectPreferredForSlot(currentFrame, slot, out bool hasPreferred);
        if (!hasPreferred)
        {
            counters.SkippedMissingPreferred++;
            return;
        }

        int currentDir = currentFrame.enemyWork.tempDir & 0x0F;
        int finalX = currentFrame.enemyWork.tempX & 0xFF;
        int finalY = currentFrame.enemyWork.tempY & 0xFF;
        UndoOnePixel(currentDir, finalX, finalY, out int startX, out int startY);

        if (!LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(startX, startY))
        {
            counters.SkippedNonCenter++;
            return;
        }

        counters.NormalCenterCandidates++;

        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = currentDir,
            TempX = startX,
            TempY = startY,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        LadyBugEnemyDecisionModel.DirectionAttemptResult decision = TryRunPreferred(scratch, preferred);
        if (!string.Equals(decision.Source, "4315_PREFERRED_REJECTED_CURRENT_KEPT", StringComparison.Ordinal))
        {
            counters.SkippedNotCurrentKept++;
            CountSource(counters, decision.Source);
            return;
        }

        counters.NormalCurrentKeptChecks++;

        // Run again from a fresh scratch because TryRunPreferred mutates scratch.
        scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = currentDir,
            TempX = startX,
            TempY = startY,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        RunAndCompare(
            currentFrame,
            scratch,
            preferred,
            "4315_SOURCE_MODEL_CURRENT_KEPT",
            counters);
    }

    private static void RunAndCompare(
        EnemyTraceFrame currentFrame,
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        int preferred,
        string sourcePrefix,
        Counters counters)
    {
        if (currentFrame.enemyWork == null)
            return;

        counters.Checks++;

        LadyBugEnemyDecisionModel.DirectionAttemptResult decision = TryRunPreferred(scratch, preferred);
        LadyBugEnemyDecisionModel.ApplyEnemyTempMovementStep(scratch);

        string source = sourcePrefix + ":" + decision.Source;
        CountSource(counters, source);

        int referenceRejected = currentFrame.enemyWork.rejectedMask & 0x0F;
        int modeledRejected = scratch.RejectedDirMask & 0x0F;
        int referenceDir = currentFrame.enemyWork.tempDir & 0x0F;
        int referenceX = currentFrame.enemyWork.tempX & 0xFF;
        int referenceY = currentFrame.enemyWork.tempY & 0xFF;

        bool matched =
            string.Equals(decision.Source, "4315_PREFERRED_REJECTED_CURRENT_KEPT", StringComparison.Ordinal) &&
            modeledRejected == referenceRejected &&
            (scratch.TempDir & 0x0F) == referenceDir &&
            (scratch.TempX & 0xFF) == referenceX &&
            (scratch.TempY & 0xFF) == referenceY;

        string context =
            "tick=" + currentFrame.frame +
            " mameFrame=" + currentFrame.mameFrame +
            " start=" + FormatByte(decision.SelectedDirection) + ":" +
                FormatByte(UndoX(referenceDir, referenceX)) + "," +
                FormatByte(UndoY(referenceDir, referenceY)) +
            " preferred=" + FormatByte(preferred) +
            " decision=" + decision.Source +
            " reference=" + FormatByte(referenceRejected) + ":" +
                FormatByte(referenceDir) + ":" + FormatByte(referenceX) + "," + FormatByte(referenceY) +
            " modeled=" + FormatByte(modeledRejected) + ":" +
                FormatByte(scratch.TempDir) + ":" + FormatByte(scratch.TempX) + "," + FormatByte(scratch.TempY);

        if (matched)
        {
            counters.Matches++;
            if (string.IsNullOrEmpty(counters.FirstMatch))
                counters.FirstMatch = context;
            return;
        }

        counters.Mismatches++;
        if (string.IsNullOrEmpty(counters.FirstMismatch))
            counters.FirstMismatch = context;
    }

    private static LadyBugEnemyDecisionModel.DirectionAttemptResult TryRunPreferred(
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        int preferred)
    {
        return LadyBugEnemyDecisionModel.TryPreferredDirection(
            scratch,
            preferred,
            LadyBugStaticMazeRomTable.Table0DA2,
            ReadPermissiveTileForCurrentKeptDiagnostic);
    }

    private static int ReadPermissiveTileForCurrentKeptDiagnostic(int x, int y)
    {
        _ = x;
        _ = y;
        return 0x00;
    }

    private static int SelectPreferredForSlot(EnemyTraceFrame frame, int slot, out bool hasPreferred)
    {
        hasPreferred = false;

        if (frame.enemyWork == null || frame.enemyWork.preferred.Count <= slot)
            return 0;

        hasPreferred = true;
        return frame.enemyWork.preferred[slot] & 0x0F;
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

    private static void UndoOnePixel(int direction, int finalX, int finalY, out int startX, out int startY)
    {
        startX = finalX & 0xFF;
        startY = finalY & 0xFF;

        switch (direction & 0x0F)
        {
            case LadyBugEnemyDecisionModel.DirLeft:
                startX = (startX + 1) & 0xFF;
                break;
            case LadyBugEnemyDecisionModel.DirUp:
                startY = (startY + 1) & 0xFF;
                break;
            case LadyBugEnemyDecisionModel.DirRight:
                startX = (startX - 1) & 0xFF;
                break;
            default:
                startY = (startY - 1) & 0xFF;
                break;
        }
    }

    private static int UndoX(int direction, int finalX)
    {
        UndoOnePixel(direction, finalX, 0, out int x, out _);
        return x;
    }

    private static int UndoY(int direction, int finalY)
    {
        UndoOnePixel(direction, 0, finalY, out _, out int y);
        return y;
    }

    private static void CountSource(Counters counters, string source)
    {
        if (!counters.Sources.TryGetValue(source, out int count))
            count = 0;

        counters.Sources[source] = count + 1;
    }

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug source-first 4315 shadow ").Append(Version).Append(": ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", releaseActivationTransitions=").Append(counters.ReleaseActivationTransitions);
        builder.Append(", releaseChecks=").Append(counters.ReleaseChecks);
        builder.Append(", normalCenterCandidates=").Append(counters.NormalCenterCandidates);
        builder.Append(", normalCurrentKeptChecks=").Append(counters.NormalCurrentKeptChecks);
        builder.Append(", checks=").Append(counters.Checks);
        builder.Append(", matches=").Append(counters.Matches);
        builder.Append(", mismatches=").Append(counters.Mismatches);
        builder.Append(", skippedMissingEnemyWork=").Append(counters.SkippedMissingEnemyWork);
        builder.Append(", skippedMissingPreferred=").Append(counters.SkippedMissingPreferred);
        builder.Append(", skippedNonCenter=").Append(counters.SkippedNonCenter);
        builder.Append(", skippedNotCurrentKept=").Append(counters.SkippedNotCurrentKept);

        if (!string.IsNullOrEmpty(counters.FirstMatch))
            builder.Append(", firstMatch: ").Append(counters.FirstMatch);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);

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

        builder.Append(". NOTE: diagnostic-only; authoritative rejectedMask remains reference-synced until the full 0x4130 / 0x3C0A local-door path is source-faithful.");
        return builder.ToString();
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }
}
