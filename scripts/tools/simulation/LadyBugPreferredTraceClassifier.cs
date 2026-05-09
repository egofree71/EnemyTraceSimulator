using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Diagnostic-only preferred[] classifier for v0.9.11.
///
/// The current source-path single-enemy replay still uses preferred[] from the
/// trace. This class does not change that. It only asks whether the observed
/// preferred[] tuple can be explained by the source-level preferred generator
/// model that already exists in LadyBugMonsterPreferenceSystem:
///
/// - 0x2E97 rotate branch;
/// - 0x2EC7 random/R-register branch;
/// - 0x477D chase/BFS override of one preferred[] slot.
///
/// Important: this is a preflight, not autonomy. The classifier may use the
/// observed preferred[] tuple to identify the compatible source family. It is
/// meant to tell us whether the current traces are covered before we attempt a
/// later adapter that stops reading preferred[] directly from the trace.
/// </summary>
public static class LadyBugPreferredTraceClassifier
{
    private const string Version = "v0.9.11";

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var stats = new Stats { Frames = referenceFrames?.Count ?? 0 };

        if (referenceFrames == null || referenceFrames.Count == 0)
            return $"Lady Bug preferred[] trace-sync preflight {Version}: no frames";

        for (int frameIndex = 0; frameIndex < referenceFrames.Count; frameIndex++)
            InspectFrame(frameIndex, referenceFrames[frameIndex], stats);

        return stats.BuildSummary();
    }

    private static void InspectFrame(int frameIndex, EnemyTraceFrame frame, Stats stats)
    {
        int activeCount = CountActiveKnown(frame.enemies, out int activeSlot);
        if (activeCount != 1)
        {
            if (activeCount == 0)
                stats.SkippedNoActiveEnemy++;
            else
                stats.SkippedMultiEnemy++;

            return;
        }

        stats.SingleActiveFrames++;

        if (frame.enemyWork == null ||
            frame.enemyWork.preferred == null ||
            frame.enemyWork.preferred.Count < LadyBugMonsterPreferenceSystem.PreferredSlotCount)
        {
            stats.MissingPreferred++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
                stats.FirstProblem = $"firstMissingPreferred frameIndex={frameIndex} tick={frame.frame} activeSlot={activeSlot}";
            return;
        }

        int[] referenceTuple = CopyPreferredTuple(frame.enemyWork.preferred);
        stats.TupleChecks++;

        if (!TryClassify(referenceTuple, activeSlot, out string source, out int[] modeledTuple))
        {
            stats.Unclassified++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
            {
                stats.FirstProblem =
                    $"firstUnclassifiedPreferred frameIndex={frameIndex} tick={frame.frame} " +
                    $"activeSlot={activeSlot} reference={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)}";
            }
            return;
        }

        if (LadyBugMonsterPreferenceSystem.TupleEquals(modeledTuple, referenceTuple))
        {
            stats.ClassifiedMatches++;
            stats.AddSource(source);
            stats.AddSelectedPreferred(activeSlot, referenceTuple[activeSlot]);
        }
        else
        {
            stats.ClassifiedMismatches++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
            {
                stats.FirstProblem =
                    $"firstPreferredClassifierMismatch frameIndex={frameIndex} tick={frame.frame} " +
                    $"activeSlot={activeSlot} source={source} " +
                    $"reference={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)} " +
                    $"modeled={LadyBugMonsterPreferenceSystem.FormatTuple(modeledTuple)}";
            }
        }
    }

    private static bool TryClassify(
        int[] referenceTuple,
        int activeSlot,
        out string source,
        out int[] modeledTuple)
    {
        foreach (int seed in DirectionSeeds())
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
            {
                source = "2E97_ROTATE_FROM_" + Hex2(seed);
                modeledTuple = candidate;
                return true;
            }
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
            {
                source = "2EC7_RANDOM_RLOW_" + rLow.ToString("X1", CultureInfo.InvariantCulture);
                modeledTuple = candidate;
                return true;
            }
        }

        foreach (int seed in DirectionSeeds())
        {
            int[] baseTuple = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (TryApplyObservedSlotOverride(referenceTuple, baseTuple, activeSlot, out modeledTuple))
            {
                source = "477D_SLOT" + activeSlot + "_BFS_OVER_2E97_ROTATE_FROM_" + Hex2(seed);
                return true;
            }
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] baseTuple = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (TryApplyObservedSlotOverride(referenceTuple, baseTuple, activeSlot, out modeledTuple))
            {
                source = "477D_SLOT" + activeSlot + "_BFS_OVER_2EC7_RANDOM_RLOW_" +
                         rLow.ToString("X1", CultureInfo.InvariantCulture);
                return true;
            }
        }

        source = "unclassified";
        modeledTuple = new[] { 0, 0, 0, 0 };
        return false;
    }

    private static bool TryApplyObservedSlotOverride(
        int[] referenceTuple,
        int[] baseTuple,
        int activeSlot,
        out int[] modeledTuple)
    {
        modeledTuple = new[] { 0, 0, 0, 0 };

        if (activeSlot < 0 || activeSlot >= LadyBugMonsterPreferenceSystem.PreferredSlotCount)
            return false;

        for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
        {
            int expected = i == activeSlot
                ? referenceTuple[i] & 0x0F
                : baseTuple[i] & 0x0F;

            if (expected != (referenceTuple[i] & 0x0F))
                return false;

            modeledTuple[i] = expected;
        }

        return true;
    }

    private static int[] CopyPreferredTuple(IReadOnlyList<int> values)
    {
        var result = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];
        for (int i = 0; i < result.Length; i++)
            result[i] = values[i] & 0x0F;
        return result;
    }

    private static IEnumerable<int> DirectionSeeds()
    {
        yield return LadyBugMonsterPreferenceSystem.DirLeft;
        yield return LadyBugMonsterPreferenceSystem.DirUp;
        yield return LadyBugMonsterPreferenceSystem.DirRight;
        yield return LadyBugMonsterPreferenceSystem.DirDown;
    }

    private static int CountActiveKnown(IReadOnlyList<EnemyTraceActor>? enemies, out int activeSlot)
    {
        activeSlot = -1;

        if (enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in enemies)
        {
            if (!enemy.active || !enemy.HasKnownPosition)
                continue;

            count++;
            activeSlot = enemy.slot;
        }

        return count;
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private sealed class Stats
    {
        public int Frames;
        public int SingleActiveFrames;
        public int SkippedNoActiveEnemy;
        public int SkippedMultiEnemy;
        public int MissingPreferred;
        public int TupleChecks;
        public int ClassifiedMatches;
        public int ClassifiedMismatches;
        public int Unclassified;
        public string FirstProblem = string.Empty;

        private readonly Dictionary<string, int> _sourceCounts = new();
        private readonly Dictionary<string, int> _selectedPreferredCounts = new();

        public void AddSource(string source)
        {
            if (!_sourceCounts.TryGetValue(source, out int count))
                count = 0;

            _sourceCounts[source] = count + 1;
        }

        public void AddSelectedPreferred(int slot, int preferred)
        {
            string key = "slot" + slot + ":" + Hex2(preferred);
            if (!_selectedPreferredCounts.TryGetValue(key, out int count))
                count = 0;

            _selectedPreferredCounts[key] = count + 1;
        }

        public string BuildSummary()
        {
            bool clean =
                TupleChecks > 0 &&
                MissingPreferred == 0 &&
                ClassifiedMismatches == 0 &&
                Unclassified == 0;

            var builder = new StringBuilder();
            builder.Append($"Lady Bug preferred[] trace-sync preflight {Version}: ");
            builder.Append($"frames={Frames}, ");
            builder.Append($"singleActiveFrames={SingleActiveFrames}, ");
            builder.Append($"skippedNoActiveEnemy={SkippedNoActiveEnemy}, ");
            builder.Append($"skippedMultiEnemy={SkippedMultiEnemy}, ");
            builder.Append($"tupleChecks={TupleChecks}, ");
            builder.Append($"classifiedMatches={ClassifiedMatches}, ");
            builder.Append($"classifiedMismatches={ClassifiedMismatches}, ");
            builder.Append($"unclassified={Unclassified}, ");
            builder.Append($"missingPreferred={MissingPreferred}, ");
            builder.Append($"clean={(clean ? "true" : "false")}, ");
            builder.Append("preferredMode=trace-synced, ");
            builder.Append("preferredAutonomy=false, ");
            builder.Append("preflightOnly=true");

            if (clean)
                builder.Append("; firstPreferredProblem: none");
            else if (!string.IsNullOrWhiteSpace(FirstProblem))
                builder.Append("; ").Append(FirstProblem);

            if (_selectedPreferredCounts.Count > 0)
                builder.Append("; selectedPreferredValues: ").Append(FormatCounts(_selectedPreferredCounts));

            if (_sourceCounts.Count > 0)
                builder.Append("; preferredSources: ").Append(FormatCounts(_sourceCounts));

            builder.Append("; NOTE: classifier uses the observed preferred[] tuple to classify source-family coverage. It does not remove trace-sync yet.");

            return builder.ToString();
        }

        private static string FormatCounts(Dictionary<string, int> counts)
        {
            var parts = new List<string>(counts.Count);
            foreach (KeyValuePair<string, int> item in counts)
                parts.Add(item.Key + "=" + item.Value.ToString(CultureInfo.InvariantCulture));

            return string.Join("; ", parts);
        }
    }
}
