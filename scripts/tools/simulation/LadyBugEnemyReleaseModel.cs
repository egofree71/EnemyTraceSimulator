using System.Collections.Generic;
using System.Text;

/// <summary>
/// Source-first diagnostic model for Lady Bug enemy release / den-exit setup.
///
/// This class deliberately does not change simulation state and does not classify
/// away any rejectedMask mismatch.  Its purpose is to expose what the release
/// source code predicts, so the next implementation step can simulate the release
/// path instead of adding filters.
///
/// Source blocks:
/// - 0x05AE..0x05D0 scans enemy slots at 0x602B + slot * 5 and calls 0x3061
///   when (slotRaw & 0x03) == 0.
/// - 0x3061..0x3086 initializes the selected enemy slot:
///     raw = 0x82
///     x   = 0x58
///     y   = 0x86
///     sprite/attr come from 0x3087.
/// - 0x4406..0x4477 is related to den/center occupancy and can also call 0x3061,
///   but it is not fully simulated here yet.
/// </summary>
public static class LadyBugEnemyReleaseModel
{
    public const int EnemySlotBaseAddress = 0x602B;
    public const int EnemySlotStride = 0x05;
    public const int EnemySlotCount = 4;

    public const int SourceReleaseRaw = 0x82;
    public const int SourceReleaseX = 0x58;
    public const int SourceReleaseY = 0x86;
    public const int SourceReleaseDir = 0x08;

    /// <summary>
    /// Candidate end-of-frame Y after one 0x4224 movement step from the 0x3061
    /// source position.  The trace currently sees 0x58,0x87 at the first active
    /// frame, while 0x3061 writes 0x58,0x86 before movement/update commit.
    /// </summary>
    public const int ObservedAfterFirstStepY = 0x87;

    public sealed class ReleaseTransitionObservation
    {
        public int Tick { get; set; }
        public int MameFrame { get; set; }
        public int Slot { get; set; }
        public int PreviousActiveCount { get; set; }
        public int CurrentActiveCount { get; set; }
        public int Raw { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int EnemyWorkTempDir { get; set; }
        public int EnemyWorkTempX { get; set; }
        public int EnemyWorkTempY { get; set; }
        public int EnemyWorkRejectedMask { get; set; }
        public bool Matches3061Raw { get; set; }
        public bool Matches3061X { get; set; }
        public bool Matches3061YBeforeMovement { get; set; }
        public bool MatchesObservedAfterFirstStepY { get; set; }
        public bool MatchesEnemyWorkAfterFirstStep { get; set; }

        public bool LooksLikeSourceRelease =>
            Matches3061Raw &&
            Matches3061X &&
            (Matches3061YBeforeMovement || MatchesObservedAfterFirstStepY);

        public string Summary
        {
            get
            {
                return "tick=" + Tick +
                       " mameFrame=" + MameFrame +
                       " slot=" + Slot +
                       " active=" + PreviousActiveCount + "->" + CurrentActiveCount +
                       " slotRawXY=" + FormatByte(Raw) + ":" + FormatByte(X) + "," + FormatByte(Y) +
                       " expected3061=" + FormatByte(SourceReleaseRaw) + ":" + FormatByte(SourceReleaseX) + "," + FormatByte(SourceReleaseY) +
                       " work=" + FormatByte(EnemyWorkTempDir) + ":" + FormatByte(EnemyWorkTempX) + "," + FormatByte(EnemyWorkTempY) +
                       " rejected=" + FormatByte(EnemyWorkRejectedMask) +
                       " matchesRaw=" + Matches3061Raw +
                       " matchesX=" + Matches3061X +
                       " matchesY3061=" + Matches3061YBeforeMovement +
                       " matchesYAfterStep=" + MatchesObservedAfterFirstStepY +
                       " matchesWorkAfterStep=" + MatchesEnemyWorkAfterFirstStep;
            }
        }
    }

    public sealed class ReleaseDiagnostics
    {
        public int Transitions { get; set; }
        public int ActivationTransitions { get; set; }
        public int SourceReleaseLikeTransitions { get; set; }
        public int SourceReleaseAfterStepTransitions { get; set; }
        public string FirstActivation = string.Empty;
        public string FirstSourceReleaseLike = string.Empty;
        public string FirstNonSourceActivation = string.Empty;

        public string BuildSummaryText()
        {
            var builder = new StringBuilder();
            builder.Append("Lady Bug enemy release diagnostics v0.6.90: ");
            builder.Append("transitions=").Append(Transitions);
            builder.Append(", activationTransitions=").Append(ActivationTransitions);
            builder.Append(", sourceReleaseLikeTransitions=").Append(SourceReleaseLikeTransitions);
            builder.Append(", sourceReleaseAfterStepTransitions=").Append(SourceReleaseAfterStepTransitions);

            if (!string.IsNullOrEmpty(FirstActivation))
                builder.Append(", firstActivation: ").Append(FirstActivation);

            if (!string.IsNullOrEmpty(FirstSourceReleaseLike))
                builder.Append(", firstSourceReleaseLike: ").Append(FirstSourceReleaseLike);

            if (!string.IsNullOrEmpty(FirstNonSourceActivation))
                builder.Append(", firstNonSourceActivation: ").Append(FirstNonSourceActivation);

            builder.Append(". NOTE: diagnostic only. This does not alter rejectedMask modeling, movement, or comparison frames.");
            return builder.ToString();
        }
    }

    public static int SlotAddress(int slot)
    {
        return EnemySlotBaseAddress + (slot * EnemySlotStride);
    }

    public static void ApplySource3061Initialization(
        int slot,
        LadyBugEnemyDecisionModel.EnemySlotState enemy)
    {
        // 0x3061 computes IX = 0x602B + C * 5, where C is the selected slot.
        // The slot value is not needed by this C# state assignment, but keeping it
        // as a parameter makes the call site mirror the source block.
        _ = SlotAddress(slot);
        enemy.Raw = SourceReleaseRaw;
        enemy.X = SourceReleaseX;
        enemy.Y = SourceReleaseY;
    }

    public static ReleaseDiagnostics BuildDiagnostics(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var diagnostics = new ReleaseDiagnostics();

        if (referenceFrames.Count == 0)
            return diagnostics;

        for (int i = 1; i < referenceFrames.Count; i++)
        {
            diagnostics.Transitions++;

            if (!TryObserveActivationTransition(
                    referenceFrames[i - 1],
                    referenceFrames[i],
                    out ReleaseTransitionObservation? observation) || observation == null)
            {
                continue;
            }

            diagnostics.ActivationTransitions++;

            if (string.IsNullOrEmpty(diagnostics.FirstActivation))
                diagnostics.FirstActivation = observation.Summary;

            if (observation.LooksLikeSourceRelease)
            {
                diagnostics.SourceReleaseLikeTransitions++;

                if (observation.MatchesObservedAfterFirstStepY)
                    diagnostics.SourceReleaseAfterStepTransitions++;

                if (string.IsNullOrEmpty(diagnostics.FirstSourceReleaseLike))
                    diagnostics.FirstSourceReleaseLike = observation.Summary;
            }
            else if (string.IsNullOrEmpty(diagnostics.FirstNonSourceActivation))
            {
                diagnostics.FirstNonSourceActivation = observation.Summary;
            }
        }

        return diagnostics;
    }

    public static bool TryObserveActivationTransition(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        out ReleaseTransitionObservation? observation)
    {
        observation = null;

        if (currentFrame.enemies == null)
            return false;

        int previousActiveCount = CountActiveEnemies(previousFrame);
        int currentActiveCount = CountActiveEnemies(currentFrame);

        if (currentActiveCount <= previousActiveCount)
            return false;

        foreach (EnemyTraceActor currentEnemy in currentFrame.enemies)
        {
            if (!currentEnemy.active || !currentEnemy.HasKnownPosition)
                continue;

            EnemyTraceActor? previousEnemy = FindEnemyBySlot(previousFrame, currentEnemy.slot);
            bool wasPreviouslyActive = previousEnemy != null && previousEnemy.active;
            if (wasPreviouslyActive)
                continue;

            int currentDir = GetDirectionFromRawOrString(currentEnemy.raw, currentEnemy.dir);
            int workDir = currentFrame.enemyWork?.tempDir ?? -1;
            int workX = currentFrame.enemyWork?.tempX ?? -1;
            int workY = currentFrame.enemyWork?.tempY ?? -1;

            observation = new ReleaseTransitionObservation
            {
                Tick = currentFrame.frame,
                MameFrame = currentFrame.mameFrame,
                Slot = currentEnemy.slot,
                PreviousActiveCount = previousActiveCount,
                CurrentActiveCount = currentActiveCount,
                Raw = currentEnemy.raw & 0xFF,
                X = currentEnemy.x & 0xFF,
                Y = currentEnemy.y & 0xFF,
                EnemyWorkTempDir = workDir & 0xFF,
                EnemyWorkTempX = workX & 0xFF,
                EnemyWorkTempY = workY & 0xFF,
                EnemyWorkRejectedMask = currentFrame.enemyWork?.rejectedMask ?? -1,
                Matches3061Raw = (currentEnemy.raw & 0xFF) == SourceReleaseRaw,
                Matches3061X = (currentEnemy.x & 0xFF) == SourceReleaseX,
                Matches3061YBeforeMovement = (currentEnemy.y & 0xFF) == SourceReleaseY,
                MatchesObservedAfterFirstStepY = (currentEnemy.y & 0xFF) == ObservedAfterFirstStepY,
                MatchesEnemyWorkAfterFirstStep =
                    (workDir & 0x0F) == SourceReleaseDir &&
                    (workX & 0xFF) == SourceReleaseX &&
                    (workY & 0xFF) == ObservedAfterFirstStepY &&
                    currentDir == SourceReleaseDir
            };

            return true;
        }

        return false;
    }

    private static int CountActiveEnemies(EnemyTraceFrame frame)
    {
        if (frame.enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.active)
                count++;
        }

        return count;
    }

    private static EnemyTraceActor? FindEnemyBySlot(EnemyTraceFrame frame, int slot)
    {
        if (frame.enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.slot == slot)
                return enemy;
        }

        return null;
    }

    private static int GetDirectionFromRawOrString(int raw, string? dirText)
    {
        int rawDirection = (raw >> 4) & 0x0F;
        if (rawDirection == 0x01 || rawDirection == 0x02 || rawDirection == 0x04 || rawDirection == 0x08)
            return rawDirection;

        if (!string.IsNullOrWhiteSpace(dirText) &&
            int.TryParse(dirText, System.Globalization.NumberStyles.HexNumber, null, out int parsed))
        {
            return parsed & 0x0F;
        }

        return 0;
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }
}
