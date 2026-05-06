using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Diagnostic comparison adapter for the source-first enemy decision model.
///
/// Important: this adapter intentionally returns identity simulation frames so
/// the normal visual/comparison pipeline should still produce zero mismatches.
/// Its value is in the Summary string: it runs LadyBugEnemyDecisionModel over
/// the loaded MAME trace and reports which decision frames can already be
/// explained by the source-first transcription.
///
/// Current scope:
/// - uses 0x427E pixel-center predicate;
/// - uses 0x3911 static logical-maze validation with the hard-coded 0x0DA2 table;
/// - uses TryPreferredDirection / FindFallbackDirection with a permissive tile reader.
///
/// Not yet authoritative:
/// - 0x3C0A tile lookup is not reconstructed here;
/// - 0x4130 local door validation is therefore not truly validated yet;
/// - exact-PC timing is not available from standard JSONL frames.
/// </summary>
public sealed class LadyBugEnemyDecisionTraceDiagnosticAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug decision diagnostics";

    public string Description =>
        "Runs source-first enemy decision diagnostics in parallel and returns identity frames. " +
        "Use this to inspect the 0x3911/0x42E6 decision model summary without changing the current comparison result.";

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
        public int FramesWithEnemyWork;
        public int FramesWithActiveEnemyWorkSlot;
        public int FramesSkippedMissingPreferred;
        public int CenterFrames;
        public int OutsideCenterFrames;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int FallbackEntered;
        public int FallbackNotFound;
        public int ReferenceRejectedMaskNonZero;
        public int ReferenceRejectedMaskMatchesModeled;
        public int ReferenceRejectedMaskDiffersFromModeled;
        public int ReferenceTempDirMatchesModeled;
        public int ReferenceTempDirDiffersFromModeled;
        public string FirstRejectedMaskDifference = string.Empty;
        public string FirstTempDirDifference = string.Empty;
        public readonly Dictionary<string, int> Sources = new();
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return "Lady Bug decision diagnostics: empty trace";

        var counters = new Counters { Frames = referenceFrames.Count };

        foreach (EnemyTraceFrame frame in referenceFrames)
            AnalyzeFrame(frame, counters);

        return BuildSummaryText(counters);
    }

    private static void AnalyzeFrame(EnemyTraceFrame frame, Counters counters)
    {
        if (frame.enemyWork == null)
            return;

        counters.FramesWithEnemyWork++;

        if (frame.enemyWork.rejectedMask != 0)
            counters.ReferenceRejectedMaskNonZero++;

        if (!TrySelectEnemyWorkSlot(frame, out EnemyTraceActor? enemy) || enemy == null)
            return;

        counters.FramesWithActiveEnemyWorkSlot++;

        int slot = Math.Clamp(enemy.slot, 0, 3);
        if (frame.enemyWork.preferred.Count <= slot)
        {
            counters.FramesSkippedMissingPreferred++;
            return;
        }

        int tempDir = frame.enemyWork.tempDir & 0x0F;
        int tempX = frame.enemyWork.tempX & 0xFF;
        int tempY = frame.enemyWork.tempY & 0xFF;
        int preferred = frame.enemyWork.preferred[slot] & 0x0F;

        if (!LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter(tempX, tempY))
        {
            counters.OutsideCenterFrames++;
            return;
        }

        counters.CenterFrames++;

        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = tempDir,
            TempX = tempX,
            TempY = tempY,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        LadyBugEnemyDecisionModel.DirectionAttemptResult result;
        try
        {
            result = LadyBugEnemyDecisionModel.TryPreferredDirection(
                scratch,
                preferred,
                LadyBugStaticMazeRomTable.Table0DA2,
                ReadTilePermissiveForCurrentDiagnostic);
        }
        catch (Exception ex)
        {
            AddSource(counters, "EXCEPTION_" + ex.GetType().Name);
            return;
        }

        AddSource(counters, result.Source);

        switch (result.Source)
        {
            case "42E6_PREFERRED_ACCEPTED":
                counters.PreferredAccepted++;
                break;
            case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
                counters.PreferredRejectedCurrentKept++;
                break;
            case "4331_FALLBACK_SELECTED":
                counters.FallbackEntered++;
                break;
            default:
                if (result.FallbackEntered && !result.Accepted)
                    counters.FallbackNotFound++;
                break;
        }

        int referenceRejected = frame.enemyWork.rejectedMask & 0x0F;
        int modeledRejected = result.RejectedMask & 0x0F;
        if (referenceRejected == modeledRejected)
        {
            counters.ReferenceRejectedMaskMatchesModeled++;
        }
        else
        {
            counters.ReferenceRejectedMaskDiffersFromModeled++;
            if (string.IsNullOrEmpty(counters.FirstRejectedMaskDifference))
            {
                counters.FirstRejectedMaskDifference =
                    BuildDifferenceContext(frame, enemy, preferred, result.Source, referenceRejected, modeledRejected);
            }
        }

        int referenceTempDir = frame.enemyWork.tempDir & 0x0F;
        int modeledTempDir = result.SelectedDirection & 0x0F;
        if (referenceTempDir == modeledTempDir)
        {
            counters.ReferenceTempDirMatchesModeled++;
        }
        else
        {
            counters.ReferenceTempDirDiffersFromModeled++;
            if (string.IsNullOrEmpty(counters.FirstTempDirDifference))
            {
                counters.FirstTempDirDifference =
                    BuildTempDirDifferenceContext(frame, enemy, preferred, result.Source, referenceTempDir, modeledTempDir);
            }
        }
    }

    /// <summary>
    /// The current diagnostic deliberately makes local-door validation permissive.
    /// That means this adapter validates the 0x3911 / 0x42E6 skeleton only.
    /// A later patch should replace this with a faithful 0x3C0A tile lookup over
    /// the captured VRAM, then validate 0x4130 for real.
    /// </summary>
    private static int ReadTilePermissiveForCurrentDiagnostic(int probeX, int probeY)
    {
        return 0x00;
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

    private static void AddSource(Counters counters, string source)
    {
        if (!counters.Sources.TryGetValue(source, out int count))
            count = 0;

        counters.Sources[source] = count + 1;
    }

    private static string BuildDifferenceContext(
        EnemyTraceFrame frame,
        EnemyTraceActor enemy,
        int preferred,
        string source,
        int referenceRejected,
        int modeledRejected)
    {
        return "tick=" + frame.frame +
               " mameFrame=" + frame.mameFrame +
               " slot=" + enemy.slot +
               " tmp=" + FormatByte(frame.enemyWork?.tempDir ?? 0) + ":" +
               FormatByte(frame.enemyWork?.tempX ?? 0) + "," +
               FormatByte(frame.enemyWork?.tempY ?? 0) +
               " preferred=" + FormatByte(preferred) +
               " source=" + source +
               " referenceRejected=" + FormatByte(referenceRejected) +
               " modeledRejected=" + FormatByte(modeledRejected);
    }

    private static string BuildTempDirDifferenceContext(
        EnemyTraceFrame frame,
        EnemyTraceActor enemy,
        int preferred,
        string source,
        int referenceTempDir,
        int modeledTempDir)
    {
        return "tick=" + frame.frame +
               " mameFrame=" + frame.mameFrame +
               " slot=" + enemy.slot +
               " tmpXY=" + FormatByte(frame.enemyWork?.tempX ?? 0) + "," +
               FormatByte(frame.enemyWork?.tempY ?? 0) +
               " preferred=" + FormatByte(preferred) +
               " source=" + source +
               " referenceTempDir=" + FormatByte(referenceTempDir) +
               " modeledSelectedDir=" + FormatByte(modeledTempDir);
    }

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();

        builder.Append("Lady Bug decision diagnostics v0.6.87c: ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", enemyWorkFrames=").Append(counters.FramesWithEnemyWork);
        builder.Append(", attributedFrames=").Append(counters.FramesWithActiveEnemyWorkSlot);
        builder.Append(", centerFrames=").Append(counters.CenterFrames);
        builder.Append(", outsideCenterFrames=").Append(counters.OutsideCenterFrames);
        builder.Append(", missingPreferredSkips=").Append(counters.FramesSkippedMissingPreferred);
        builder.Append(", preferredAccepted=").Append(counters.PreferredAccepted);
        builder.Append(", preferredRejectedCurrentKept=").Append(counters.PreferredRejectedCurrentKept);
        builder.Append(", fallbackEntered=").Append(counters.FallbackEntered);
        builder.Append(", fallbackNotFound=").Append(counters.FallbackNotFound);
        builder.Append(", referenceRejectedMaskNonZero=").Append(counters.ReferenceRejectedMaskNonZero);
        builder.Append(", rejectedMaskMatchesModeled=").Append(counters.ReferenceRejectedMaskMatchesModeled);
        builder.Append(", rejectedMaskDiffersFromModeled=").Append(counters.ReferenceRejectedMaskDiffersFromModeled);
        builder.Append(", tempDirMatchesModeled=").Append(counters.ReferenceTempDirMatchesModeled);
        builder.Append(", tempDirDiffersFromModeled=").Append(counters.ReferenceTempDirDiffersFromModeled);

        if (!string.IsNullOrEmpty(counters.FirstRejectedMaskDifference))
            builder.Append(", firstRejectedMaskDifference: ").Append(counters.FirstRejectedMaskDifference);

        if (!string.IsNullOrEmpty(counters.FirstTempDirDifference))
            builder.Append(", firstTempDirDifference: ").Append(counters.FirstTempDirDifference);

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

        builder.Append(". NOTE: this is a diagnostic-only adapter. It returns identity frames, so comparison mismatches should remain zero. Local-door validation is currently permissive until 0x3C0A tile lookup is reconstructed.");
        return builder.ToString();
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }
}
