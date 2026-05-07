using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// v0.7.07b diagnostic bridge for the Lady Bug preferred[] generator.
///
/// This class is intentionally non-authoritative. It does not move enemies and it
/// does not replace Enemy_UpdateOne yet. Its job is to make the dependency on
/// preferred[] explicit while we transition away from reading the already-computed
/// 0x61C4..0x61C7 tuple from MAME.
///
/// Source-first status:
/// - The pure tuple generator is LadyBugMonsterPreferenceSystem.
/// - v0.7.06 exact-PC diagnostics validated 0x2E97, 0x2EC7, and 0x477D call by call.
/// - The standard JSONL trace still contains only the end-of-frame preferred[]
///   tuple plus safe polling change events, not the exact 0x2EA5 LD A,R tape.
///
/// Therefore this model currently checks whether the active-enemy standard trace
/// tuple can be represented by the validated source-first generator and reports the remaining
/// bridge explicitly. The next step is to feed an exact-PC preferred replay tape
/// into Enemy_UpdateOne instead of classifying the end tuple.
/// </summary>
public static class LadyBugEnemyPreferredGeneratorReplayShadowModel
{
    private const string Version = "v0.7.07b";

    private sealed class Counters
    {
        public int Frames;
        public int FramesWithEnemyWork;
        public int TupleChecks;
        public int TupleMatches;
        public int TupleMismatches;
        public int MissingPreferred;
        public int SkippedNoActiveEnemy;
        public int PreferredChangeEvents;
        public int FramesWithPreferredChangeEvents;
        public int RotateMatches;
        public int RandomMatches;
        public int Slot0BfsOverrideMatches;
        public int Unclassified;
        public string FirstMatch = string.Empty;
        public string FirstMismatch = string.Empty;
        public readonly Dictionary<string, int> Sources = new(StringComparer.Ordinal);
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
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

        if (frame.preferredChangeEvents != null && frame.preferredChangeEvents.Count > 0)
        {
            counters.FramesWithPreferredChangeEvents++;
            counters.PreferredChangeEvents += frame.preferredChangeEvents.Count;
        }

        // Scope guard: the first frames after state load can contain stale
        // preferred[] scratch values before any enemy slot is active.  Those bytes
        // are useful as RAM state, but they are not a generated Enemy_UpdateOne
        // preferred tuple for the single-enemy static-player milestone.
        if (!HasAnyActiveEnemy(frame))
        {
            counters.SkippedNoActiveEnemy++;
            return;
        }

        if (frame.enemyWork.preferred == null || frame.enemyWork.preferred.Count < LadyBugMonsterPreferenceSystem.PreferredSlotCount)
        {
            counters.MissingPreferred++;
            return;
        }

        counters.TupleChecks++;
        int[] referenceTuple = ReadPreferredTuple(frame.enemyWork.preferred);

        if (TryClassifyTuple(referenceTuple, out int[] modeledTuple, out string source))
        {
            counters.TupleMatches++;
            Count(counters.Sources, source);

            if (source.StartsWith("2E97_ROTATE", StringComparison.Ordinal))
                counters.RotateMatches++;
            else if (source.StartsWith("2EC7_RANDOM", StringComparison.Ordinal))
                counters.RandomMatches++;
            else if (source.StartsWith("477D_SLOT0_BFS", StringComparison.Ordinal))
                counters.Slot0BfsOverrideMatches++;

            if (string.IsNullOrEmpty(counters.FirstMatch))
                counters.FirstMatch = BuildContext(frame, source, referenceTuple, modeledTuple);
            return;
        }

        counters.TupleMismatches++;
        counters.Unclassified++;
        Count(counters.Sources, "unclassified");

        if (string.IsNullOrEmpty(counters.FirstMismatch))
            counters.FirstMismatch = BuildContext(frame, "unclassified", referenceTuple, new[] { 0, 0, 0, 0 });
    }


    private static bool HasAnyActiveEnemy(EnemyTraceFrame frame)
    {
        if (frame.enemies == null)
            return false;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.active)
                return true;
        }

        return false;
    }

    private static int[] ReadPreferredTuple(IReadOnlyList<int> preferred)
    {
        return new[]
        {
            preferred[0] & 0x0F,
            preferred[1] & 0x0F,
            preferred[2] & 0x0F,
            preferred[3] & 0x0F
        };
    }

    private static bool TryClassifyTuple(int[] referenceTuple, out int[] modeledTuple, out string source)
    {
        modeledTuple = new[] { 0, 0, 0, 0 };
        source = "none";

        foreach (int seed in PreferredRotateSeeds())
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
            {
                modeledTuple = candidate;
                source = "2E97_ROTATE_FROM_" + FormatByte(seed);
                return true;
            }
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
            {
                modeledTuple = candidate;
                source = "2EC7_RANDOM_RLOW_" + rLow.ToString("X1", CultureInfo.InvariantCulture);
                return true;
            }
        }

        // v0.7.06 exact-PC showed that the current static-player window only uses
        // IY=61C4 for BFS/chase overwrites. The standard JSONL trace lacks the exact
        // 477D PC event, so this remains a bridge classifier, not an authoritative
        // replay tape.
        foreach (int seed in PreferredRotateSeeds())
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (TryApplyObservedSlot0Override(referenceTuple, candidate, out modeledTuple))
            {
                source = "477D_SLOT0_BFS_OVER_2E97_ROTATE_FROM_" + FormatByte(seed);
                return true;
            }
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (TryApplyObservedSlot0Override(referenceTuple, candidate, out modeledTuple))
            {
                source = "477D_SLOT0_BFS_OVER_2EC7_RANDOM_RLOW_" + rLow.ToString("X1", CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    private static bool TryApplyObservedSlot0Override(int[] referenceTuple, int[] baseTuple, out int[] modeledTuple)
    {
        modeledTuple = new[] { 0, 0, 0, 0 };

        if ((baseTuple[1] & 0x0F) != (referenceTuple[1] & 0x0F) ||
            (baseTuple[2] & 0x0F) != (referenceTuple[2] & 0x0F) ||
            (baseTuple[3] & 0x0F) != (referenceTuple[3] & 0x0F))
        {
            return false;
        }

        modeledTuple = new[]
        {
            baseTuple[0] & 0x0F,
            baseTuple[1] & 0x0F,
            baseTuple[2] & 0x0F,
            baseTuple[3] & 0x0F
        };

        return LadyBugMonsterPreferenceSystem.TryApplyBfsOverride(
            modeledTuple,
            LadyBugMonsterPreferenceSystem.PreferredBaseAddress,
            referenceTuple[0] & 0x0F);
    }

    private static IEnumerable<int> PreferredRotateSeeds()
    {
        yield return LadyBugMonsterPreferenceSystem.DirLeft;
        yield return LadyBugMonsterPreferenceSystem.DirUp;
        yield return LadyBugMonsterPreferenceSystem.DirRight;
        yield return LadyBugMonsterPreferenceSystem.DirDown;
    }

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug preferred[] generator replay shadow ").Append(Version).Append(": ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", framesWithEnemyWork=").Append(counters.FramesWithEnemyWork);
        builder.Append(", tupleChecks=").Append(counters.TupleChecks);
        builder.Append(", tupleMatches=").Append(counters.TupleMatches);
        builder.Append(", tupleMismatches=").Append(counters.TupleMismatches);
        builder.Append(", missingPreferred=").Append(counters.MissingPreferred);
        builder.Append(", skippedNoActiveEnemy=").Append(counters.SkippedNoActiveEnemy);
        builder.Append(", preferredChangeEvents=").Append(counters.PreferredChangeEvents);
        builder.Append(", framesWithPreferredChangeEvents=").Append(counters.FramesWithPreferredChangeEvents);
        builder.Append(", rotateMatches=").Append(counters.RotateMatches);
        builder.Append(", randomMatches=").Append(counters.RandomMatches);
        builder.Append(", slot0BfsOverrideMatches=").Append(counters.Slot0BfsOverrideMatches);
        builder.Append(", unclassified=").Append(counters.Unclassified);

        if (!string.IsNullOrEmpty(counters.FirstMatch))
            builder.Append(", firstMatch: ").Append(counters.FirstMatch);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);

        builder.Append(", sources: ").Append(DescribeDictionary(counters.Sources));
        builder.Append(". NOTE: shadow-only bridge. v0.7.06 exact-PC proved the source-first generator, including LD A,R values at 2EA5 and BFS writes at 477D. v0.7.07b scopes the standard-trace tuple classifier to frames with at least one active enemy, so stale pre-release preferred[] bytes from tick 0..4 are not counted as generator mismatches. This still classifies the end-of-frame preferred[] tuple; it does not yet feed an exact-PC random tape into Enemy_UpdateOne. Do not call this 100% autonomous yet.");
        return builder.ToString();
    }

    private static string BuildContext(EnemyTraceFrame frame, string source, IReadOnlyList<int> referenceTuple, IReadOnlyList<int> modeledTuple)
    {
        return "tick=" + frame.frame +
               " mameFrame=" + frame.mameFrame +
               " pc=" + (frame.pc ?? "") +
               " r=" + (frame.r ?? "") +
               " source=" + source +
               " reference=" + LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple) +
               " modeled=" + LadyBugMonsterPreferenceSystem.FormatTuple(modeledTuple);
    }

    private static void Count(Dictionary<string, int> counts, string key)
    {
        if (!counts.TryGetValue(key, out int count))
            count = 0;

        counts[key] = count + 1;
    }

    private static string DescribeDictionary(Dictionary<string, int> values)
    {
        if (values.Count == 0)
            return "none";

        var parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in values)
            parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));

        return string.Join("; ", parts);
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }
}
