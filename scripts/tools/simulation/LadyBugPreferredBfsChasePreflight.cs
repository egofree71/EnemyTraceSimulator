using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Diagnostic-only BFS/chase preferred[] preflight.
///
/// v0.9.14b/c source-first check:
/// - identify visible 0x477D-style preferred[slot] overrides in the standard JSONL trace;
/// - confirm chase timer activity;
/// - verify the override direction against the source 0x45DC coordinate-to-index
///   formula and the captured 0x6200 logical maze.
///
/// This class does not drive the replay. The replay provider in v0.9.14c has its
/// own guarded BFS application path.
/// </summary>
public static class LadyBugPreferredBfsChasePreflight
{
    private const string Version = "v0.9.14c";

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var stats = new Stats { Frames = referenceFrames?.Count ?? 0 };

        if (referenceFrames == null || referenceFrames.Count == 0)
            return $"Lady Bug BFS/chase preferred[] override preflight {Version}: no frames";

        for (int frameIndex = 0; frameIndex < referenceFrames.Count; frameIndex++)
        {
            EnemyTraceFrame frame = referenceFrames[frameIndex];
            EnemyTraceFrame? previous = frameIndex > 0 ? referenceFrames[frameIndex - 1] : null;
            InspectFrame(frameIndex, previous, frame, stats);
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

        stats.SingleActiveFrames++;

        if (!TryCopyPreferredTuple(frame, out int[] referenceTuple))
        {
            stats.MissingPreferred++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
                stats.FirstProblem = $"firstMissingPreferred frameIndex={frameIndex} tick={frame.frame} activeSlot={activeSlot}";
            return;
        }

        stats.TupleChecks++;

        List<BaseCandidate> matchingBases = FindBaseCandidates(frame, referenceTuple, activeSlot, visibleOverrideOnly: false);
        List<BaseCandidate> visibleBfsBases = FindBaseCandidates(frame, referenceTuple, activeSlot, visibleOverrideOnly: true);

        if (TupleEqualsAny(referenceTuple, matchingBases))
        {
            stats.PureBaseTupleFrames++;
            if (matchingBases.Count > 1)
                stats.AmbiguousBaseCandidates++;

            foreach (BaseCandidate candidate in matchingBases)
                stats.AddPureBaseSource(candidate.Source);
            return;
        }

        if (visibleBfsBases.Count == 0)
        {
            stats.NotBaseNotBfsVisible++;
            if (string.IsNullOrEmpty(stats.FirstProblem))
            {
                stats.FirstProblem =
                    $"firstNotBaseNotBfsVisible frameIndex={frameIndex} tick={frame.frame} slot={activeSlot} " +
                    $"tuple={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)}";
            }
            return;
        }

        stats.VisibleBfsOverrideFrames++;
        if (visibleBfsBases.Count > 1)
            stats.AmbiguousBaseCandidates++;

        int overrideDirection = referenceTuple[activeSlot] & 0x0F;
        int chaseTimer = GetChaseTimer(frame, activeSlot);
        if (chaseTimer != 0)
            stats.ChaseTimerActiveVisibleBfs++;
        else
            stats.ChaseTimerInactiveVisibleBfs++;

        stats.AddBfsOverrideDirection(overrideDirection);
        stats.AddBfsOverrideSlot(activeSlot);
        stats.AddBfsChaseTimer(chaseTimer);
        stats.AddRoundRobin(frame.enemyWork?.chaseRoundRobin ?? -1);

        foreach (BaseCandidate candidate in visibleBfsBases)
            stats.AddBfsOverBaseSource(candidate.Source);

        EnemyTraceActor? currentEnemy = TryGetEnemyBySlot(frame.enemies, activeSlot);
        if (currentEnemy != null)
            ProbeSourceGuidance("current", frameIndex, frame, currentEnemy.x, currentEnemy.y, overrideDirection, stats);

        if (previousFrame != null)
        {
            EnemyTraceActor? previousEnemy = TryGetEnemyBySlot(previousFrame.enemies, activeSlot);
            if (previousEnemy != null)
                ProbeSourceGuidance("previous", frameIndex, previousFrame, previousEnemy.x, previousEnemy.y, overrideDirection, stats);
        }

        if (string.IsNullOrEmpty(stats.FirstVisibleBfsOverride))
        {
            BaseCandidate firstBase = visibleBfsBases[0];
            stats.FirstVisibleBfsOverride =
                $"firstVisibleBfsOverride frameIndex={frameIndex} tick={frame.frame} slot={activeSlot} " +
                $"base={firstBase.Source} baseTuple={LadyBugMonsterPreferenceSystem.FormatTuple(firstBase.Tuple)} " +
                $"override={Hex2(overrideDirection)} tuple={LadyBugMonsterPreferenceSystem.FormatTuple(referenceTuple)} " +
                $"chaseTimer={Hex2(chaseTimer)} rr={Hex2(frame.enemyWork?.chaseRoundRobin ?? -1)}";
        }
    }

    private static void ProbeSourceGuidance(string label, int frameIndex, EnemyTraceFrame frame, int x, int y, int overrideDirection, Stats stats)
    {
        if (label == "current")
            stats.SourceGuidanceCurrentValid++;
        else if (label == "previous")
            stats.SourceGuidancePreviousValid++;

        if (!LadyBugBfsChaseSourceModel.TryGetGuidance(frame, x, y, out LadyBugBfsChaseSourceModel.GuidanceResult guidance))
            return;

        bool match = (guidance.Direction & 0x0F) == (overrideDirection & 0x0F);

        if (label == "current")
        {
            if (match)
                stats.SourceGuidanceCurrentMatches++;
        }
        else if (label == "previous")
        {
            if (match)
                stats.SourceGuidancePreviousMatches++;
        }

        if (match && string.IsNullOrEmpty(stats.FirstSourceGuidanceMatch))
        {
            stats.FirstSourceGuidanceMatch =
                $"firstSourceGuidanceMatch {label} frameIndex={frameIndex} tick={frame.frame} {guidance.ToCompactString()}";
        }
    }

    private static List<BaseCandidate> FindBaseCandidates(EnemyTraceFrame frame, int[] referenceTuple, int activeSlot, bool visibleOverrideOnly)
    {
        var result = new List<BaseCandidate>();

        foreach (BaseCandidate candidate in EnumerateBaseCandidates(frame))
        {
            bool nonActiveMatch = true;
            for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
            {
                if (i == activeSlot)
                    continue;

                if ((referenceTuple[i] & 0x0F) != (candidate.Tuple[i] & 0x0F))
                {
                    nonActiveMatch = false;
                    break;
                }
            }

            if (!nonActiveMatch)
                continue;

            bool activeSame = (referenceTuple[activeSlot] & 0x0F) == (candidate.Tuple[activeSlot] & 0x0F);

            if (visibleOverrideOnly)
            {
                if (!activeSame)
                    result.Add(candidate);
            }
            else if (activeSame)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static IEnumerable<BaseCandidate> EnumerateBaseCandidates(EnemyTraceFrame frame)
    {
        if (TryGetPlayerDirection(frame, out int playerDirection))
        {
            yield return new BaseCandidate(
                LadyBugMonsterPreferenceSystem.GenerateRotateBranch(playerDirection),
                "2E97_ROTATE_FROM_" + Hex2(playerDirection));
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            yield return new BaseCandidate(
                LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow),
                "2EC7_RANDOM_RLOW_" + rLow.ToString("X1", CultureInfo.InvariantCulture));
        }
    }

    private static bool TryCopyPreferredTuple(EnemyTraceFrame frame, out int[] tuple)
    {
        tuple = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];
        if (frame.enemyWork?.preferred == null || frame.enemyWork.preferred.Count < tuple.Length)
            return false;

        for (int i = 0; i < tuple.Length; i++)
            tuple[i] = frame.enemyWork.preferred[i] & 0x0F;
        return true;
    }

    private static bool TupleEqualsAny(int[] tuple, List<BaseCandidate> candidates)
    {
        foreach (BaseCandidate candidate in candidates)
        {
            if (LadyBugMonsterPreferenceSystem.TupleEquals(tuple, candidate.Tuple))
                return true;
        }

        return false;
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

    private static EnemyTraceActor? TryGetEnemyBySlot(IReadOnlyList<EnemyTraceActor>? enemies, int slot)
    {
        if (enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in enemies)
        {
            if (enemy.slot == slot && enemy.active && enemy.HasKnownPosition)
                return enemy;
        }

        return null;
    }

    private static int GetChaseTimer(EnemyTraceFrame frame, int slot)
    {
        if (frame.enemyWork?.chaseTimers == null || slot < 0 || slot >= frame.enemyWork.chaseTimers.Count)
            return 0;
        return frame.enemyWork.chaseTimers[slot] & 0xFF;
    }

    private static bool TryGetPlayerDirection(EnemyTraceFrame frame, out int direction)
    {
        direction = 0;
        if (frame.player == null)
            return false;

        if (TryParseDirectionText(frame.player.dir, out direction))
            return true;

        direction = frame.player.raw < 0 ? 0 : (frame.player.raw >> 4) & 0x0F;
        return LadyBugBfsChaseSourceModel.IsOneHotDirection(direction);
    }

    private static bool TryParseDirectionText(string? text, out int direction)
    {
        direction = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(2);

        if (!int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;

        direction = value & 0x0F;
        return LadyBugBfsChaseSourceModel.IsOneHotDirection(direction);
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private readonly struct BaseCandidate
    {
        public BaseCandidate(int[] tuple, string source)
        {
            Tuple = tuple;
            Source = source;
        }

        public int[] Tuple { get; }
        public string Source { get; }
    }

    private sealed class Stats
    {
        public int Frames;
        public int SingleActiveFrames;
        public int SkippedNoActiveEnemy;
        public int SkippedMultiEnemy;
        public int TupleChecks;
        public int PureBaseTupleFrames;
        public int VisibleBfsOverrideFrames;
        public int NotBaseNotBfsVisible;
        public int ChaseTimerActiveVisibleBfs;
        public int ChaseTimerInactiveVisibleBfs;
        public int AmbiguousBaseCandidates;
        public int MissingPreferred;
        public int SourceGuidanceCurrentValid;
        public int SourceGuidanceCurrentMatches;
        public int SourceGuidancePreviousValid;
        public int SourceGuidancePreviousMatches;
        public string FirstProblem = string.Empty;
        public string FirstVisibleBfsOverride = string.Empty;
        public string FirstSourceGuidanceMatch = string.Empty;

        private readonly Dictionary<string, int> _overrideDirections = new();
        private readonly Dictionary<string, int> _overrideSlots = new();
        private readonly Dictionary<string, int> _chaseTimers = new();
        private readonly Dictionary<string, int> _roundRobin = new();
        private readonly Dictionary<string, int> _bfsOverBaseSources = new();
        private readonly Dictionary<string, int> _pureBaseSources = new();

        public void AddBfsOverrideDirection(int direction) => Add(_overrideDirections, Hex2(direction));
        public void AddBfsOverrideSlot(int slot) => Add(_overrideSlots, "slot" + slot.ToString(CultureInfo.InvariantCulture));
        public void AddBfsChaseTimer(int timer) => Add(_chaseTimers, Hex2(timer));
        public void AddRoundRobin(int rr) => Add(_roundRobin, Hex2(rr));
        public void AddBfsOverBaseSource(string source) => Add(_bfsOverBaseSources, source);
        public void AddPureBaseSource(string source) => Add(_pureBaseSources, source);

        public string BuildSummary()
        {
            int bestMatches = Math.Max(SourceGuidanceCurrentMatches, SourceGuidancePreviousMatches);
            bool sourceGuidanceClean = VisibleBfsOverrideFrames == 0 || bestMatches == VisibleBfsOverrideFrames;
            bool clean = TupleChecks > 0 && MissingPreferred == 0 && NotBaseNotBfsVisible == 0 && ChaseTimerInactiveVisibleBfs == 0 && sourceGuidanceClean;

            var b = new StringBuilder();
            b.Append($"Lady Bug BFS/chase preferred[] override preflight {Version}: ");
            b.Append($"frames={Frames}, ");
            b.Append($"singleActiveFrames={SingleActiveFrames}, ");
            b.Append($"skippedNoActiveEnemy={SkippedNoActiveEnemy}, ");
            b.Append($"skippedMultiEnemy={SkippedMultiEnemy}, ");
            b.Append($"tupleChecks={TupleChecks}, ");
            b.Append($"pureBaseTupleFrames={PureBaseTupleFrames}, ");
            b.Append($"visibleBfsOverrideFrames={VisibleBfsOverrideFrames}, ");
            b.Append($"notBaseNotBfsVisible={NotBaseNotBfsVisible}, ");
            b.Append($"chaseTimerActiveVisibleBfs={ChaseTimerActiveVisibleBfs}, ");
            b.Append($"chaseTimerInactiveVisibleBfs={ChaseTimerInactiveVisibleBfs}, ");
            b.Append($"ambiguousBaseCandidates={AmbiguousBaseCandidates}, ");
            b.Append($"missingPreferred={MissingPreferred}, ");
            b.Append($"sourceGuidanceBestMatches={bestMatches}/{VisibleBfsOverrideFrames}, ");
            b.Append($"sourceGuidanceClean={(sourceGuidanceClean ? "true" : "false")}, ");
            b.Append($"clean={(clean ? "true" : "false")}, ");
            b.Append("bfsAutonomy=partial-visible-overrides-modeled-in-replay, preflightOnly=false");

            if (clean)
                b.Append("; firstBfsProblem: none");
            else if (!string.IsNullOrWhiteSpace(FirstProblem))
                b.Append("; ").Append(FirstProblem);

            AppendOptional(b, FirstVisibleBfsOverride);
            AppendCounts(b, "bfsOverrideDirections", _overrideDirections);
            AppendCounts(b, "bfsOverrideSlots", _overrideSlots);
            AppendCounts(b, "bfsChaseTimers", _chaseTimers);
            AppendCounts(b, "bfsRoundRobinValues", _roundRobin);
            AppendCounts(b, "bfsOverBaseSources", _bfsOverBaseSources);
            AppendCounts(b, "pureBaseSources", _pureBaseSources);
            b.Append($"; sourceGuidance45DC: current={SourceGuidanceCurrentMatches}/{SourceGuidanceCurrentValid}; previous={SourceGuidancePreviousMatches}/{SourceGuidancePreviousValid}");
            AppendOptional(b, FirstSourceGuidanceMatch);
            b.Append("; NOTE: sourceGuidance45DC uses the actual 0x45DC coordinate-to-index formula and the captured 0x6200 map. v0.9.14c replay uses matching visible BFS/chase overrides when safe.");
            return b.ToString();
        }

        private static void Add(Dictionary<string, int> counts, string key)
        {
            if (!counts.TryGetValue(key, out int count))
                count = 0;
            counts[key] = count + 1;
        }

        private static void AppendCounts(StringBuilder b, string label, Dictionary<string, int> counts)
        {
            if (counts.Count == 0)
                return;

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts)
                parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
            b.Append("; ").Append(label).Append(": ").Append(string.Join("; ", parts));
        }

        private static void AppendOptional(StringBuilder b, string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                b.Append("; ").Append(text);
        }
    }
}
