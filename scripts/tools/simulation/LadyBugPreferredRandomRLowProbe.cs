using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Diagnostic-only feasibility probe for the random/R-register preferred[] branch.
///
/// v0.9.12 already uses modeled preferred[] for the deterministic 0x2E97 rotate
/// branch. The remaining hard case is 0x2EC7, which depends on Z80 LD A,R timing.
///
/// This probe intentionally does not change replay behavior. It classifies the
/// observed preferred[] tuples using the same source-family priority as the
/// v0.9.11 preflight, then focuses on tuples attributed to:
///
/// - pure 0x2EC7 random/R-low;
/// - 0x477D BFS override over a 0x2EC7 random/R-low base tuple.
///
/// For those random-family cases it infers the used R-low nibble implied by the
/// observed preferred[] tuple, then checks whether that nibble can be predicted
/// from frame.r or previousFrame.r using a stable low-nibble offset.
///
/// If the best offset covers every unique random-family case, a future package
/// can safely try a modeled random provider. If not, the standard trace probably
/// needs a closer capture point around the actual LD A,R path.
/// </summary>
public static class LadyBugPreferredRandomRLowProbe
{
    private const string Version = "v0.9.13";

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var stats = new Stats { Frames = referenceFrames?.Count ?? 0 };

        if (referenceFrames == null || referenceFrames.Count == 0)
            return $"Lady Bug preferred[] random/R-low feasibility probe {Version}: no frames";

        for (int frameIndex = 0; frameIndex < referenceFrames.Count; frameIndex++)
        {
            EnemyTraceFrame? previous = frameIndex > 0 ? referenceFrames[frameIndex - 1] : null;
            InspectFrame(frameIndex, previous, referenceFrames[frameIndex], stats);
        }

        return stats.BuildSummary();
    }

    private static void InspectFrame(int frameIndex, EnemyTraceFrame? previousFrame, EnemyTraceFrame frame, Stats stats)
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

        if (!TryCopyReferencePreferredTuple(frame, out int[] referenceTuple))
        {
            stats.MissingPreferred++;
            return;
        }

        stats.TupleChecks++;

        Classification classification = ClassifyWithPreflightPriority(referenceTuple, activeSlot);
        switch (classification.Family)
        {
            case PreferredFamily.Rotate:
                stats.RotateClassified++;
                return;
            case PreferredFamily.BfsOverRotate:
                stats.BfsOverRotateClassified++;
                return;
            case PreferredFamily.Random:
                stats.PureRandomChecks++;
                break;
            case PreferredFamily.BfsOverRandom:
                stats.BfsOverRandomChecks++;
                break;
            default:
                stats.OtherOrUnclassified++;
                if (string.IsNullOrEmpty(stats.FirstProblem))
                {
                    stats.FirstProblem =
                        $"firstRandomProbeUnclassified frameIndex={frameIndex} tick={frame.frame} " +
                        $"slot={activeSlot} tuple={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)}";
                }
                return;
        }

        stats.RandomFamilyChecks++;
        stats.AddRandomSource(classification.Source);

        if (classification.CandidateRLow.Count != 1)
        {
            stats.AmbiguousRandomRLow++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
            {
                stats.FirstProblem =
                    $"firstAmbiguousRandomRLow frameIndex={frameIndex} tick={frame.frame} " +
                    $"slot={activeSlot} source={classification.Source} candidates={FormatRLowList(classification.CandidateRLow)} " +
                    $"tuple={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)}";
            }
            return;
        }

        int usedRLow = classification.CandidateRLow[0] & 0x0F;
        stats.UniqueRandomRLow++;
        stats.AddUsedRLow(usedRLow);

        if (!TryGetRLow(frame, out int currentRLow))
        {
            stats.MissingCurrentFrameR++;
        }
        else
        {
            stats.CurrentRLowCases++;
            stats.CurrentOffsetCounts[(usedRLow - currentRLow) & 0x0F]++;
            if (currentRLow == usedRLow)
                stats.CurrentDirectMatches++;
        }

        if (previousFrame == null || !TryGetRLow(previousFrame, out int previousRLow))
        {
            stats.MissingPreviousFrameR++;
        }
        else
        {
            stats.PreviousRLowCases++;
            stats.PreviousOffsetCounts[(usedRLow - previousRLow) & 0x0F]++;
            if (previousRLow == usedRLow)
                stats.PreviousDirectMatches++;
        }
    }

    private static Classification ClassifyWithPreflightPriority(int[] referenceTuple, int activeSlot)
    {
        foreach (int seed in DirectionSeeds())
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
            {
                return Classification.NonRandom(
                    PreferredFamily.Rotate,
                    "2E97_ROTATE_FROM_" + Hex2(seed));
            }
        }

        var pureRandomCandidates = new List<int>();
        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] candidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(candidate, referenceTuple))
                pureRandomCandidates.Add(rLow & 0x0F);
        }

        if (pureRandomCandidates.Count > 0)
        {
            return Classification.Random(
                PreferredFamily.Random,
                "2EC7_RANDOM_RLOW_" + FormatPrimaryOrMany(pureRandomCandidates),
                pureRandomCandidates);
        }

        foreach (int seed in DirectionSeeds())
        {
            int[] baseTuple = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            if (TryApplyObservedSlotOverride(referenceTuple, baseTuple, activeSlot))
            {
                return Classification.NonRandom(
                    PreferredFamily.BfsOverRotate,
                    "477D_SLOT" + activeSlot + "_BFS_OVER_2E97_ROTATE_FROM_" + Hex2(seed));
            }
        }

        var bfsRandomCandidates = new List<int>();
        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] baseTuple = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);
            if (TryApplyObservedSlotOverride(referenceTuple, baseTuple, activeSlot))
                bfsRandomCandidates.Add(rLow & 0x0F);
        }

        if (bfsRandomCandidates.Count > 0)
        {
            return Classification.Random(
                PreferredFamily.BfsOverRandom,
                "477D_SLOT" + activeSlot + "_BFS_OVER_2EC7_RANDOM_RLOW_" + FormatPrimaryOrMany(bfsRandomCandidates),
                bfsRandomCandidates);
        }

        return Classification.NonRandom(PreferredFamily.Other, "unclassified");
    }

    private static bool TryApplyObservedSlotOverride(int[] referenceTuple, int[] baseTuple, int activeSlot)
    {
        if (activeSlot < 0 || activeSlot >= LadyBugMonsterPreferenceSystem.PreferredSlotCount)
            return false;

        for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
        {
            int expected = i == activeSlot
                ? referenceTuple[i] & 0x0F
                : baseTuple[i] & 0x0F;

            if (expected != (referenceTuple[i] & 0x0F))
                return false;
        }

        return true;
    }

    private static bool TryCopyReferencePreferredTuple(EnemyTraceFrame frame, out int[] tuple)
    {
        tuple = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];

        if (frame.enemyWork == null ||
            frame.enemyWork.preferred == null ||
            frame.enemyWork.preferred.Count < LadyBugMonsterPreferenceSystem.PreferredSlotCount)
        {
            return false;
        }

        for (int i = 0; i < tuple.Length; i++)
            tuple[i] = frame.enemyWork.preferred[i] & 0x0F;

        return true;
    }

    private static bool TryGetRLow(EnemyTraceFrame frame, out int rLow)
    {
        rLow = 0;

        if (string.IsNullOrWhiteSpace(frame.r))
            return false;

        string text = frame.r.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);

        if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(0, text.Length - 1);

        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;

        rLow = value & 0x0F;
        return true;
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

    private static IEnumerable<int> DirectionSeeds()
    {
        yield return LadyBugMonsterPreferenceSystem.DirLeft;
        yield return LadyBugMonsterPreferenceSystem.DirUp;
        yield return LadyBugMonsterPreferenceSystem.DirRight;
        yield return LadyBugMonsterPreferenceSystem.DirDown;
    }

    private static string FormatPrimaryOrMany(IReadOnlyList<int> candidates)
    {
        if (candidates.Count == 0)
            return "NONE";

        if (candidates.Count == 1)
            return candidates[0].ToString("X1", CultureInfo.InvariantCulture);

        return "MANY_" + FormatRLowList(candidates);
    }

    private static string FormatRLowList(IReadOnlyList<int> candidates)
    {
        var parts = new List<string>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
            parts.Add((candidates[i] & 0x0F).ToString("X1", CultureInfo.InvariantCulture));

        return "[" + string.Join(",", parts) + "]";
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private enum PreferredFamily
    {
        Other,
        Rotate,
        Random,
        BfsOverRotate,
        BfsOverRandom
    }

    private readonly struct Classification
    {
        private Classification(PreferredFamily family, string source, List<int> candidateRLow)
        {
            Family = family;
            Source = source;
            CandidateRLow = candidateRLow;
        }

        public PreferredFamily Family { get; }
        public string Source { get; }
        public List<int> CandidateRLow { get; }

        public static Classification NonRandom(PreferredFamily family, string source)
        {
            return new Classification(family, source, new List<int>());
        }

        public static Classification Random(PreferredFamily family, string source, List<int> candidateRLow)
        {
            return new Classification(family, source, new List<int>(candidateRLow));
        }
    }

    private sealed class Stats
    {
        public int Frames;
        public int SkippedNoActiveEnemy;
        public int SkippedMultiEnemy;
        public int MissingPreferred;
        public int TupleChecks;
        public int RotateClassified;
        public int BfsOverRotateClassified;
        public int OtherOrUnclassified;
        public int PureRandomChecks;
        public int BfsOverRandomChecks;
        public int RandomFamilyChecks;
        public int UniqueRandomRLow;
        public int AmbiguousRandomRLow;
        public int MissingCurrentFrameR;
        public int MissingPreviousFrameR;
        public int CurrentRLowCases;
        public int PreviousRLowCases;
        public int CurrentDirectMatches;
        public int PreviousDirectMatches;
        public string FirstProblem = string.Empty;
        public readonly int[] CurrentOffsetCounts = new int[16];
        public readonly int[] PreviousOffsetCounts = new int[16];
        private readonly Dictionary<string, int> _randomSourceCounts = new();
        private readonly Dictionary<string, int> _usedRLowCounts = new();

        public void AddRandomSource(string source)
        {
            if (!_randomSourceCounts.TryGetValue(source, out int count))
                count = 0;

            _randomSourceCounts[source] = count + 1;
        }

        public void AddUsedRLow(int usedRLow)
        {
            string key = "usedRLow_" + (usedRLow & 0x0F).ToString("X1", CultureInfo.InvariantCulture);
            if (!_usedRLowCounts.TryGetValue(key, out int count))
                count = 0;

            _usedRLowCounts[key] = count + 1;
        }

        public string BuildSummary()
        {
            BestOffset currentBest = FindBestOffset(CurrentOffsetCounts);
            BestOffset previousBest = FindBestOffset(PreviousOffsetCounts);

            bool currentPredictable = UniqueRandomRLow > 0 &&
                                      AmbiguousRandomRLow == 0 &&
                                      CurrentRLowCases == UniqueRandomRLow &&
                                      currentBest.Matches == UniqueRandomRLow;

            bool previousPredictable = UniqueRandomRLow > 0 &&
                                       AmbiguousRandomRLow == 0 &&
                                       PreviousRLowCases == UniqueRandomRLow &&
                                       previousBest.Matches == UniqueRandomRLow;

            bool probeClean = MissingPreferred == 0 && OtherOrUnclassified == 0;

            var builder = new StringBuilder();
            builder.Append($"Lady Bug preferred[] random/R-low feasibility probe {Version}: ");
            builder.Append($"frames={Frames}, ");
            builder.Append($"tupleChecks={TupleChecks}, ");
            builder.Append($"rotateClassified={RotateClassified}, ");
            builder.Append($"bfsOverRotateClassified={BfsOverRotateClassified}, ");
            builder.Append($"pureRandomChecks={PureRandomChecks}, ");
            builder.Append($"bfsOverRandomChecks={BfsOverRandomChecks}, ");
            builder.Append($"randomFamilyChecks={RandomFamilyChecks}, ");
            builder.Append($"uniqueRandomRLow={UniqueRandomRLow}, ");
            builder.Append($"ambiguousRandomRLow={AmbiguousRandomRLow}, ");
            builder.Append($"currentFrameRLowDirectMatches={CurrentDirectMatches}, ");
            builder.Append($"currentFrameRLowBestOffset=+{currentBest.Offset:X1}/{currentBest.Matches}, ");
            builder.Append($"previousFrameRLowDirectMatches={PreviousDirectMatches}, ");
            builder.Append($"previousFrameRLowBestOffset=+{previousBest.Offset:X1}/{previousBest.Matches}, ");
            builder.Append($"currentFrameRPredictable={(currentPredictable ? "true" : "false")}, ");
            builder.Append($"previousFrameRPredictable={(previousPredictable ? "true" : "false")}, ");
            builder.Append($"missingPreferred={MissingPreferred}, ");
            builder.Append($"missingCurrentFrameR={MissingCurrentFrameR}, ");
            builder.Append($"missingPreviousFrameR={MissingPreviousFrameR}, ");
            builder.Append($"otherOrUnclassified={OtherOrUnclassified}, ");
            builder.Append($"clean={(probeClean ? "true" : "false")}, ");
            builder.Append("randomAutonomy=false, probeOnly=true");

            if (string.IsNullOrWhiteSpace(FirstProblem))
                builder.Append("; firstRandomProblem: none");
            else
                builder.Append("; ").Append(FirstProblem);

            if (_usedRLowCounts.Count > 0)
                builder.Append("; inferredUsedRLow: ").Append(FormatCounts(_usedRLowCounts));

            if (_randomSourceCounts.Count > 0)
                builder.Append("; randomSources: ").Append(FormatCounts(_randomSourceCounts));

            builder.Append("; NOTE: probe infers used R-low from observed random-family preferred[] tuples and compares it with frame.r / previousFrame.r low nibbles. It does not change replay behavior.");
            return builder.ToString();
        }

        private static BestOffset FindBestOffset(IReadOnlyList<int> counts)
        {
            int bestOffset = 0;
            int bestMatches = 0;

            for (int offset = 0; offset < counts.Count; offset++)
            {
                if (counts[offset] > bestMatches)
                {
                    bestMatches = counts[offset];
                    bestOffset = offset;
                }
            }

            return new BestOffset(bestOffset, bestMatches);
        }

        private static string FormatCounts(Dictionary<string, int> counts)
        {
            var parts = new List<string>(counts.Count);
            foreach (KeyValuePair<string, int> item in counts)
                parts.Add(item.Key + "=" + item.Value.ToString(CultureInfo.InvariantCulture));

            return string.Join("; ", parts);
        }
    }

    private readonly struct BestOffset
    {
        public BestOffset(int offset, int matches)
        {
            Offset = offset & 0x0F;
            Matches = matches;
        }

        public int Offset { get; }
        public int Matches { get; }
    }
}
