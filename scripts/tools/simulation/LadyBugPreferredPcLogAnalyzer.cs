using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Offline analyzer for the MAME debugger error.log produced by
/// tools/mame/lua/ladybug_preferred_pc_trace.lua.
///
/// This is intentionally separate from the standard JSONL trace pipeline:
///
/// - JSONL remains the frame-by-frame comparison format.
/// - error.log / LBPREF is an exact-PC reverse-engineering diagnostic stream.
///
/// v0.6.71:
/// - Adds a shadow replay check that replays the exact-PC LBPREF stream with
///   LadyBugMonsterPreferenceSystem.
/// - The replay validates the modeled base writes and checks that the simulated
///   preferred[] state matches the p0..p3 snapshots before each logged write.
/// - BFS/chase writes are still applied from the observed 477D hit, because the
///   full BFS pathfinding system is not implemented yet.
/// </summary>
public static class LadyBugPreferredPcLogAnalyzer
{
    private const string RandomSource = "2EC7_RANDOM_WRITE";
    private const string RotateSource = "2E97_ROTATE_WRITE";
    private const string BfsSource = "477D_BFS_WRITE";

    public static IReadOnlyList<string> BuildReport(string errorLogText)
    {
        var lines = new List<string>();

        List<PreferredPcHit> hits = ParseHits(errorLogText);

        lines.Add("preferred[] exact-PC error.log diagnostics");
        lines.Add($"  LBPREF hits: {hits.Count}");

        if (hits.Count == 0)
        {
            lines.Add("  No LBPREF lines found. Make sure MAME was launched with the exact-PC diagnostic script and -log.");
            return lines;
        }

        Dictionary<string, int> sourceCounts = CountBySource(hits);
        int[] slotCounts = InferSlotCounts(hits, out int unknownSlotCount);

        AppendSourceCounts(lines, sourceCounts);
        AppendSlotCounts(lines, slotCounts, unknownSlotCount);
        AppendSlotBfsCorrelation(lines, sourceCounts, slotCounts);

        List<PreferredPcTuple> randomTuples = BuildBaseTuples(hits, RandomSource);
        List<PreferredPcTuple> rotateTuples = BuildBaseTuples(hits, RotateSource);

        lines.Add("preferred[] exact-PC tuple summary");
        lines.Add($"  {RandomSource}: {randomTuples.Count} complete 4-write tuple(s)");
        lines.Add($"  {RotateSource}: {rotateTuples.Count} complete 4-write tuple(s)");

        AppendTopTuples(lines, "top random tuples from 2EC7", randomTuples, 12);
        AppendTopTuples(lines, "top rotate tuples from 2E97", rotateTuples, 8);
        AppendRandomRDiagnostics(lines, randomTuples);
        AppendModelCheck(lines, randomTuples, rotateTuples);
        AppendShadowReplayCheck(lines, hits);
        AppendBfsSummary(lines, hits);

        return lines;
    }

    public static List<PreferredPcHit> ParseHits(string errorLogText)
    {
        var hits = new List<PreferredPcHit>();

        if (string.IsNullOrEmpty(errorLogText))
            return hits;

        string[] rows = errorLogText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (string row in rows)
        {
            if (!row.Contains("LBPREF", StringComparison.Ordinal))
                continue;

            if (TryParseHit(row, out PreferredPcHit hit))
                hits.Add(hit);
        }

        return hits;
    }

    private static bool TryParseHit(string row, out PreferredPcHit hit)
    {
        hit = new PreferredPcHit();

        int marker = row.IndexOf("LBPREF", StringComparison.Ordinal);
        if (marker < 0)
            return false;

        string payload = row[marker..].Trim();
        string[] parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts[0] != "LBPREF")
            return false;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = part[..eq].Trim();
            string value = part[(eq + 1)..].Trim();
            values[key] = value;
        }

        string source = GetString(values, "source");
        string pc = GetString(values, "pc");

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(pc))
            return false;

        hit = new PreferredPcHit
        {
            Source = source,
            Pc = pc,
            R = GetHex(values, "r"),
            A = GetHex(values, "a"),
            B = GetHex(values, "b"),
            C = GetHex(values, "c"),
            D = GetHex(values, "d"),
            E = GetHex(values, "e"),
            H = GetHex(values, "h"),
            L = GetHex(values, "l"),
            HL = GetHex(values, "hl"),
            IY = GetHex(values, "iy"),
            SP = GetHex(values, "sp"),
            P0 = GetHex(values, "p0"),
            P1 = GetHex(values, "p1"),
            P2 = GetHex(values, "p2"),
            P3 = GetHex(values, "p3"),
            TempDir = GetHex(values, "tmpDir"),
            TempX = GetHex(values, "tmpX"),
            TempY = GetHex(values, "tmpY"),
            RejectedMask = GetHex(values, "rejected"),
            FallbackMask = GetHex(values, "fallback"),
            Chase0 = GetHex(values, "chase0"),
            Chase1 = GetHex(values, "chase1"),
            Chase2 = GetHex(values, "chase2"),
            Chase3 = GetHex(values, "chase3"),
            RoundRobin = GetHex(values, "rr"),
            RawLine = row
        };

        return true;
    }

    private static Dictionary<string, int> CountBySource(List<PreferredPcHit> hits)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (PreferredPcHit hit in hits)
        {
            counts.TryGetValue(hit.Source, out int count);
            counts[hit.Source] = count + 1;
        }

        return counts;
    }

    private static int[] InferSlotCounts(List<PreferredPcHit> hits, out int unknown)
    {
        int[] slotCounts = new int[4];
        unknown = 0;

        int randomIndex = 0;
        int rotateIndex = 0;

        foreach (PreferredPcHit hit in hits)
        {
            int slot = InferSlot(hit, ref randomIndex, ref rotateIndex);

            if (slot >= 0 && slot < 4)
                slotCounts[slot]++;
            else
                unknown++;
        }

        return slotCounts;
    }

    private static void AppendSourceCounts(List<string> lines, Dictionary<string, int> counts)
    {
        lines.Add("preferred[] exact-PC source counts");
        foreach (KeyValuePair<string, int> pair in TopCounts(counts, 16))
            lines.Add($"  {pair.Key}: {pair.Value}");
    }

    private static void AppendSlotCounts(List<string> lines, int[] slotCounts, int unknown)
    {
        lines.Add("preferred[] exact-PC inferred slot counts");
        for (int i = 0; i < 4; i++)
            lines.Add($"  slot {i} / 0x{0x61C4 + i:X4}: {slotCounts[i]}");

        if (unknown > 0)
            lines.Add($"  unknown slot: {unknown}");
    }

    private static void AppendSlotBfsCorrelation(
        List<string> lines,
        Dictionary<string, int> sourceCounts,
        int[] slotCounts)
    {
        sourceCounts.TryGetValue(RandomSource, out int randomHits);
        sourceCounts.TryGetValue(RotateSource, out int rotateHits);
        sourceCounts.TryGetValue(BfsSource, out int bfsHits);

        int baseHits = randomHits + rotateHits;
        bool baseDivisibleByFour = (baseHits % 4) == 0;
        int expectedBasePerSlot = baseHits / 4;

        int slot0ExtraOverSlot1 = slotCounts[0] - slotCounts[1];
        int slot0ExtraOverBase = baseDivisibleByFour
            ? slotCounts[0] - expectedBasePerSlot
            : 0;

        lines.Add("preferred[] exact-PC slot/BFS correlation");
        lines.Add($"  base writes 2EC7+2E97: {baseHits} {(baseDivisibleByFour ? "(divisible by 4)" : "(not divisible by 4)")}");
        if (baseDivisibleByFour)
            lines.Add($"  expected base writes per slot: {expectedBasePerSlot}");

        lines.Add($"  slot0 extra over slot1: {slot0ExtraOverSlot1}");
        if (baseDivisibleByFour)
            lines.Add($"  slot0 extra over base-per-slot: {slot0ExtraOverBase}");

        lines.Add($"  {BfsSource} hits: {bfsHits}");

        bool exactMatch = slot0ExtraOverSlot1 == bfsHits &&
                          (!baseDivisibleByFour || slot0ExtraOverBase == bfsHits);

        lines.Add(exactMatch
            ? "  conclusion: slot0 excess matches BFS/chase overrides exactly."
            : "  conclusion: slot0 excess does not exactly match BFS hits; inspect multi-enemy or partial tuple effects.");

        if (bfsHits > 0)
            lines.Add("  interpretation: in this capture, 477D is overriding preferred[0] / 0x61C4.");
    }

    private static List<PreferredPcTuple> BuildBaseTuples(List<PreferredPcHit> hits, string source)
    {
        var tuples = new List<PreferredPcTuple>();
        var group = new List<PreferredPcHit>(4);

        foreach (PreferredPcHit hit in hits)
        {
            if (hit.Source != source)
                continue;

            group.Add(hit);

            if (group.Count == 4)
            {
                tuples.Add(new PreferredPcTuple(group[0], group[1], group[2], group[3]));
                group.Clear();
            }
        }

        return tuples;
    }

    private static void AppendTopTuples(List<string> lines, string title, List<PreferredPcTuple> tuples, int limit)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (PreferredPcTuple tuple in tuples)
        {
            string key = tuple.FormatDirections();
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        lines.Add(title);

        if (counts.Count == 0)
        {
            lines.Add("  none");
            return;
        }

        foreach (KeyValuePair<string, int> pair in TopCounts(counts, limit))
            lines.Add($"  {pair.Value}x {pair.Key}");
    }

    private static void AppendRandomRDiagnostics(List<string> lines, List<PreferredPcTuple> randomTuples)
    {
        lines.Add("preferred[] exact-PC random R diagnostics");

        if (randomTuples.Count == 0)
        {
            lines.Add("  no complete random tuple found.");
            return;
        }

        var deltaByDirection = new Dictionary<int, Dictionary<int, int>>();

        foreach (PreferredPcTuple tuple in randomTuples)
        {
            PreferredPcHit[] hits = tuple.ToArray();

            for (int i = 0; i < hits.Length; i++)
            {
                PreferredPcHit hit = hits[i];
                int direction = hit.A & 0x0F;
                int usedRLow = LadyBugMonsterPreferenceSystem.ReconstructUsedRLowFromWritePcR(hit.R, direction);

                if (i < hits.Length - 1)
                {
                    PreferredPcHit next = hits[i + 1];
                    int nextDirection = next.A & 0x0F;
                    int nextUsedRLow = LadyBugMonsterPreferenceSystem.ReconstructUsedRLowFromWritePcR(next.R, nextDirection);
                    int delta = (nextUsedRLow - usedRLow) & 0x0F;

                    if (!deltaByDirection.TryGetValue(direction, out Dictionary<int, int>? directionCounts))
                    {
                        directionCounts = new Dictionary<int, int>();
                        deltaByDirection[direction] = directionCounts;
                    }

                    directionCounts.TryGetValue(delta, out int count);
                    directionCounts[delta] = count + 1;
                }
            }
        }

        lines.Add("  reconstructed usedRLow formula from R at 2EC7:");
        lines.Add("    A=01 -> (R - 08) & 0F");
        lines.Add("    A=02 -> (R - 0A) & 0F");
        lines.Add("    A=04 -> (R - 0B) & 0F");
        lines.Add("    A=08 -> (R - 0C) & 0F");

        lines.Add("  internal usedRLow deltas by generated direction:");
        foreach (int direction in new[] { 0x01, 0x02, 0x04, 0x08 })
        {
            if (!deltaByDirection.TryGetValue(direction, out Dictionary<int, int>? counts) || counts.Count == 0)
            {
                lines.Add($"    after {direction:X2}: none");
                continue;
            }

            lines.Add($"    after {direction:X2}: {FormatTopDeltaCounts(counts)}");
        }

        lines.Add("  first random tuples:");
        int shown = Math.Min(8, randomTuples.Count);
        for (int i = 0; i < shown; i++)
        {
            PreferredPcTuple tuple = randomTuples[i];
            lines.Add($"    {tuple.FormatDirections()} | R@2EC7=[{tuple.FormatRValues()}] | usedRLow=[{tuple.FormatUsedRLowValues()}]");
        }
    }

    private static void AppendModelCheck(
        List<string> lines,
        List<PreferredPcTuple> randomTuples,
        List<PreferredPcTuple> rotateTuples)
    {
        lines.Add("preferred[] exact-PC C# model check");

        int randomMatches = 0;
        string firstRandomMismatch = "none";

        foreach (PreferredPcTuple tuple in randomTuples)
        {
            int usedRLowStart = tuple.UsedRLowAtSlot(0);
            int[] predicted = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(usedRLowStart);

            if (tuple.Matches(predicted))
            {
                randomMatches++;
            }
            else if (firstRandomMismatch == "none")
            {
                firstRandomMismatch =
                    $"expected={tuple.FormatDirections()} predicted={LadyBugMonsterPreferenceSystem.FormatTuple(predicted)} " +
                    $"usedRLowStart={usedRLowStart:X1}";
            }
        }

        lines.Add($"  2EC7 random model matches: {randomMatches}/{randomTuples.Count}");
        if (firstRandomMismatch != "none")
            lines.Add($"  first random mismatch: {firstRandomMismatch}");

        int rotateMatchesWithDownStart = 0;
        int[] rotateFromDown = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(LadyBugMonsterPreferenceSystem.DirDown);

        foreach (PreferredPcTuple tuple in rotateTuples)
        {
            if (tuple.Matches(rotateFromDown))
                rotateMatchesWithDownStart++;
        }

        lines.Add($"  2E97 rotate model from PLAYER_DIR_CURRENT=08 matches: {rotateMatchesWithDownStart}/{rotateTuples.Count}");
        lines.Add($"  rotate from 08 predicts: {LadyBugMonsterPreferenceSystem.FormatTuple(rotateFromDown)}");
    }

    private static void AppendShadowReplayCheck(List<string> lines, List<PreferredPcHit> hits)
    {
        lines.Add("preferred[] exact-PC shadow replay check");

        if (hits.Count == 0)
        {
            lines.Add("  no hits.");
            return;
        }

        int[] state =
        {
            hits[0].P0 & 0x0F,
            hits[0].P1 & 0x0F,
            hits[0].P2 & 0x0F,
            hits[0].P3 & 0x0F
        };

        int preStateMatches = 0;
        int preStateChecks = 0;
        int modeledBaseWriteMatches = 0;
        int modeledBaseWriteChecks = 0;
        int bfsOverridesApplied = 0;
        int bfsApplyFailures = 0;
        string firstPreStateMismatch = "none";
        string firstBaseWriteMismatch = "none";

        int i = 0;
        while (i < hits.Count)
        {
            PreferredPcHit hit = hits[i];

            if ((hit.Source == RandomSource || hit.Source == RotateSource) &&
                HasConsecutiveSource(hits, i, 4, hit.Source))
            {
                int[] predicted = hit.Source == RandomSource
                    ? LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(
                        LadyBugMonsterPreferenceSystem.ReconstructUsedRLowFromWritePcR(hit.R, hit.A & 0x0F))
                    : LadyBugMonsterPreferenceSystem.GenerateRotateBranch(LadyBugMonsterPreferenceSystem.DirDown);

                for (int slot = 0; slot < 4; slot++)
                {
                    PreferredPcHit slotHit = hits[i + slot];
                    CheckPreState(slotHit, state, ref preStateMatches, ref preStateChecks, ref firstPreStateMismatch);

                    int predictedValue = predicted[slot] & 0x0F;
                    int observedValue = slotHit.A & 0x0F;
                    modeledBaseWriteChecks++;

                    if (predictedValue == observedValue)
                    {
                        modeledBaseWriteMatches++;
                    }
                    else if (firstBaseWriteMismatch == "none")
                    {
                        firstBaseWriteMismatch =
                            $"source={slotHit.Source} pc={slotHit.Pc} slot={slot} " +
                            $"predicted={Hex2(predictedValue)} observed={Hex2(observedValue)} " +
                            $"before={FormatState(state)}";
                    }

                    state[slot] = predictedValue;
                }

                i += 4;
                continue;
            }

            CheckPreState(hit, state, ref preStateMatches, ref preStateChecks, ref firstPreStateMismatch);

            if (hit.Source == BfsSource)
            {
                bool applied = LadyBugMonsterPreferenceSystem.TryApplyBfsOverride(state, hit.IY, hit.A);

                if (applied)
                    bfsOverridesApplied++;
                else
                    bfsApplyFailures++;
            }
            else
            {
                int slot = InferSingleWriteSlot(hit);
                if (slot >= 0 && slot < 4)
                    state[slot] = hit.A & 0x0F;
            }

            i++;
        }

        lines.Add($"  pre-write state matches p0..p3: {preStateMatches}/{preStateChecks}");
        if (firstPreStateMismatch != "none")
            lines.Add($"  first pre-state mismatch: {firstPreStateMismatch}");

        lines.Add($"  modeled base write values match observed A: {modeledBaseWriteMatches}/{modeledBaseWriteChecks}");
        if (firstBaseWriteMismatch != "none")
            lines.Add($"  first base write mismatch: {firstBaseWriteMismatch}");

        lines.Add($"  BFS overrides applied from observed 477D hits: {bfsOverridesApplied}");
        if (bfsApplyFailures > 0)
            lines.Add($"  BFS override apply failures: {bfsApplyFailures}");

        lines.Add($"  final shadow preferred[] state: {FormatState(state)}");
        lines.Add("  note: BFS direction is still observed from 477D; full BFS pathfinding is not implemented yet.");
    }

    private static void CheckPreState(
        PreferredPcHit hit,
        int[] state,
        ref int matches,
        ref int checks,
        ref string firstMismatch)
    {
        checks++;

        int[] snapshot =
        {
            hit.P0 & 0x0F,
            hit.P1 & 0x0F,
            hit.P2 & 0x0F,
            hit.P3 & 0x0F
        };

        if (StateEquals(state, snapshot))
        {
            matches++;
            return;
        }

        if (firstMismatch == "none")
        {
            firstMismatch =
                $"source={hit.Source} pc={hit.Pc} expectedPre={FormatState(snapshot)} shadowPre={FormatState(state)}";
        }
    }

    private static bool HasConsecutiveSource(List<PreferredPcHit> hits, int start, int count, string source)
    {
        if (start + count > hits.Count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (hits[start + i].Source != source)
                return false;
        }

        return true;
    }

    private static int InferSingleWriteSlot(PreferredPcHit hit)
    {
        if (hit.IY >= 0x61C4 && hit.IY <= 0x61C7)
            return hit.IY - 0x61C4;

        if (hit.L >= 0xC4 && hit.L <= 0xC7)
            return hit.L - 0xC4;

        return -1;
    }

    private static bool StateEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count < 4 || b.Count < 4)
            return false;

        return (a[0] & 0x0F) == (b[0] & 0x0F) &&
               (a[1] & 0x0F) == (b[1] & 0x0F) &&
               (a[2] & 0x0F) == (b[2] & 0x0F) &&
               (a[3] & 0x0F) == (b[3] & 0x0F);
    }

    private static string FormatState(IReadOnlyList<int> state)
    {
        return $"[{Hex2(state[0])},{Hex2(state[1])},{Hex2(state[2])},{Hex2(state[3])}]";
    }

    private static void AppendBfsSummary(List<string> lines, List<PreferredPcHit> hits)
    {
        lines.Add("preferred[] exact-PC BFS/chase override summary");

        int count = 0;
        var timerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var directionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] bfsSlotCounts = new int[4];
        int bfsUnknownSlotCount = 0;

        foreach (PreferredPcHit hit in hits)
        {
            if (hit.Source != BfsSource)
                continue;

            count++;

            int bfsSlot = (hit.IY >= 0x61C4 && hit.IY <= 0x61C7) ? hit.IY - 0x61C4 : -1;
            if (bfsSlot >= 0)
                bfsSlotCounts[bfsSlot]++;
            else
                bfsUnknownSlotCount++;

            string timerKey = $"chase=[{Hex2(hit.Chase0)},{Hex2(hit.Chase1)},{Hex2(hit.Chase2)},{Hex2(hit.Chase3)}] rr={Hex2(hit.RoundRobin)}";
            timerCounts.TryGetValue(timerKey, out int timerCount);
            timerCounts[timerKey] = timerCount + 1;

            string directionKey = Hex2(hit.A);
            directionCounts.TryGetValue(directionKey, out int directionCount);
            directionCounts[directionKey] = directionCount + 1;
        }

        lines.Add($"  {BfsSource}: {count} hit(s)");

        if (count == 0)
            return;

        lines.Add("  BFS target slots inferred from IY:");
        for (int i = 0; i < 4; i++)
            lines.Add($"    slot {i} / 0x{0x61C4 + i:X4}: {bfsSlotCounts[i]}");

        if (bfsUnknownSlotCount > 0)
            lines.Add($"    unknown: {bfsUnknownSlotCount}");

        lines.Add("  BFS write directions:");
        foreach (KeyValuePair<string, int> pair in TopCounts(directionCounts, 8))
            lines.Add($"    {pair.Value}x A={pair.Key}");

        lines.Add("  BFS chase timer states:");
        foreach (KeyValuePair<string, int> pair in TopCounts(timerCounts, 8))
            lines.Add($"    {pair.Value}x {pair.Key}");
    }

    private static int InferSlot(PreferredPcHit hit, ref int randomIndex, ref int rotateIndex)
    {
        if (hit.Source == RandomSource)
        {
            int slot = randomIndex & 0x03;
            randomIndex++;
            return slot;
        }

        if (hit.Source == RotateSource)
        {
            int slot = rotateIndex & 0x03;
            rotateIndex++;
            return slot;
        }

        if (hit.Source == BfsSource)
        {
            if (hit.IY >= 0x61C4 && hit.IY <= 0x61C7)
                return hit.IY - 0x61C4;

            return 0;
        }

        return -1;
    }

    private static string GetString(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static int GetHex(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string? text))
            return 0;

        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    private static string Hex2(int value) => (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);

    private static string Hex1(int value) => (value & 0x0F).ToString("X1", CultureInfo.InvariantCulture);

    private static string FormatTopDeltaCounts(Dictionary<int, int> counts)
    {
        var parts = new List<string>();

        foreach (KeyValuePair<int, int> pair in TopCounts(counts, 4))
            parts.Add($"+{Hex1(pair.Key)}:{pair.Value}");

        return string.Join(" ", parts);
    }

    private static List<KeyValuePair<string, int>> TopCounts(Dictionary<string, int> counts, int limit)
    {
        var pairs = new List<KeyValuePair<string, int>>(counts);
        pairs.Sort((a, b) =>
        {
            int countCompare = b.Value.CompareTo(a.Value);
            if (countCompare != 0)
                return countCompare;

            return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        });

        if (pairs.Count <= limit)
            return pairs;

        return pairs.GetRange(0, limit);
    }

    private static List<KeyValuePair<int, int>> TopCounts(Dictionary<int, int> counts, int limit)
    {
        var pairs = new List<KeyValuePair<int, int>>(counts);
        pairs.Sort((a, b) =>
        {
            int countCompare = b.Value.CompareTo(a.Value);
            if (countCompare != 0)
                return countCompare;

            return a.Key.CompareTo(b.Key);
        });

        if (pairs.Count <= limit)
            return pairs;

        return pairs.GetRange(0, limit);
    }

    public sealed class PreferredPcHit
    {
        public string Source { get; init; } = string.Empty;
        public string Pc { get; init; } = string.Empty;

        public int R { get; init; }
        public int A { get; init; }
        public int B { get; init; }
        public int C { get; init; }
        public int D { get; init; }
        public int E { get; init; }
        public int H { get; init; }
        public int L { get; init; }
        public int HL { get; init; }
        public int IY { get; init; }
        public int SP { get; init; }

        public int P0 { get; init; }
        public int P1 { get; init; }
        public int P2 { get; init; }
        public int P3 { get; init; }

        public int TempDir { get; init; }
        public int TempX { get; init; }
        public int TempY { get; init; }
        public int RejectedMask { get; init; }
        public int FallbackMask { get; init; }

        public int Chase0 { get; init; }
        public int Chase1 { get; init; }
        public int Chase2 { get; init; }
        public int Chase3 { get; init; }
        public int RoundRobin { get; init; }

        public string RawLine { get; init; } = string.Empty;
    }

    private sealed class PreferredPcTuple
    {
        public PreferredPcTuple(PreferredPcHit s0, PreferredPcHit s1, PreferredPcHit s2, PreferredPcHit s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        private PreferredPcHit S0 { get; }
        private PreferredPcHit S1 { get; }
        private PreferredPcHit S2 { get; }
        private PreferredPcHit S3 { get; }

        public PreferredPcHit[] ToArray()
        {
            return new[] { S0, S1, S2, S3 };
        }

        public bool Matches(IReadOnlyList<int> values)
        {
            return (S0.A & 0x0F) == (values[0] & 0x0F) &&
                   (S1.A & 0x0F) == (values[1] & 0x0F) &&
                   (S2.A & 0x0F) == (values[2] & 0x0F) &&
                   (S3.A & 0x0F) == (values[3] & 0x0F);
        }

        public int UsedRLowAtSlot(int slot)
        {
            PreferredPcHit hit = slot switch
            {
                0 => S0,
                1 => S1,
                2 => S2,
                3 => S3,
                _ => S0
            };

            return LadyBugMonsterPreferenceSystem.ReconstructUsedRLowFromWritePcR(hit.R, hit.A & 0x0F);
        }

        public string FormatDirections()
        {
            return $"[{Hex2(S0.A)},{Hex2(S1.A)},{Hex2(S2.A)},{Hex2(S3.A)}]";
        }

        public string FormatRValues()
        {
            return $"{Hex2(S0.R)},{Hex2(S1.R)},{Hex2(S2.R)},{Hex2(S3.R)}";
        }

        public string FormatUsedRLowValues()
        {
            int r0 = UsedRLowAtSlot(0);
            int r1 = UsedRLowAtSlot(1);
            int r2 = UsedRLowAtSlot(2);
            int r3 = UsedRLowAtSlot(3);

            return $"{Hex1(r0)},{Hex1(r1)},{Hex1(r2)},{Hex1(r3)}";
        }
    }
}
