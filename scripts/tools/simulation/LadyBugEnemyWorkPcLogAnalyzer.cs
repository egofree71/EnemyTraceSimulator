using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Builds a compact report from MAME error.log lines emitted by
/// tools/mame/lua/ladybug_enemywork_pc_trace.lua.
///
/// The diagnostic is observational: it counts exact-PC events around rejectedMask,
/// fallbackMask, local-door validation, fallback, forced-reversal paths, and now
/// the 0x3C0A tile-address lookup helper.
///
/// Important v0.6.95 note:
/// The debugger action logs H and L separately and also logs a convenience "hl"
/// expression.  Exact-PC 0x3C0A traces showed that the composite "hl" expression
/// is not reliable in this MAME debugger context.  Tile lookup validation therefore
/// derives actualHL only from the separate h/l fields.
/// </summary>
public static class LadyBugEnemyWorkPcLogAnalyzer
{
    private const string Marker = "LBEW|";
    private const string TileLookupReturnSource = "3C2B_TILE_LOOKUP_RETURN";

    public static IReadOnlyList<string> BuildReport(string errorLogText)
    {
        var events = ParseEvents(errorLogText);
        var lines = new List<string>
        {
            "Lady Bug EnemyWork exact-PC diagnostic analysis",
            "===============================================",
            string.Empty,
            $"LBEW hits: {events.Count}",
        };

        if (events.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No LBEW lines found in error.log.");
            lines.Add("Check that the selected Lua script is ladybug_enemywork_pc_trace.lua and that MAME was launched with -debug -log.");
            return lines;
        }

        AppendCountSection(lines, "Hits by source", events.GroupBy(e => e.Source).OrderByDescending(g => g.Count()).ThenBy(g => g.Key));
        AppendCountSection(lines, "Hits by rejectedMask", events.GroupBy(e => e.Rejected).OrderByDescending(g => g.Count()).ThenBy(g => g.Key));
        AppendCountSection(lines, "Hits by fallbackMask", events.GroupBy(e => e.Fallback).OrderByDescending(g => g.Count()).ThenBy(g => g.Key));
        AppendCountSection(lines, "Hits by derived active enemy count", events.GroupBy(e => e.ActiveEnemyCount.ToString(CultureInfo.InvariantCulture)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key));

        AppendTileLookup3C0aSummary(lines, events);

        var cycles = BuildCycles(events);
        AppendCycleSummary(lines, cycles);
        AppendDecisionClassification(lines, cycles);

        lines.Add(string.Empty);
        lines.Add("First events");
        lines.Add("------------");
        foreach (EnemyWorkPcEvent e in events.Take(60))
            lines.Add(FormatEvent(e));

        AppendEventSection(
            lines,
            "First 0x3C0A tile lookup return events",
            events.Where(e => e.Source.Equals(TileLookupReturnSource, StringComparison.OrdinalIgnoreCase)).Take(40));

        AppendEventSection(
            lines,
            "Exact rejectedMask writer events",
            events.Where(IsRejectedWriter).Take(80));

        AppendEventSection(
            lines,
            "Exact fallbackMask step events",
            events.Where(IsFallbackStep).Take(80));

        AppendEventSection(
            lines,
            "Fallback entry events",
            events.Where(e => e.Source.Equals("4241_FALLBACK_ENTRY", StringComparison.OrdinalIgnoreCase) ||
                              e.Source.Equals("4241_FALLBACK", StringComparison.OrdinalIgnoreCase)).Take(80));

        var rejectedChanges = CollectChanges(events, e => e.Rejected);
        lines.Add(string.Empty);
        lines.Add("RejectedMask changes");
        lines.Add("--------------------");
        if (rejectedChanges.Count == 0)
        {
            lines.Add("none");
        }
        else
        {
            foreach ((int index, string previous, string current, EnemyWorkPcEvent e) in rejectedChanges.Take(80))
                lines.Add($"#{index}: {previous}->{current} {FormatEvent(e)}");
        }

        var fallbackChanges = CollectChanges(events, e => e.Fallback);
        lines.Add(string.Empty);
        lines.Add("FallbackMask changes");
        lines.Add("--------------------");
        if (fallbackChanges.Count == 0)
        {
            lines.Add("none");
        }
        else
        {
            foreach ((int index, string previous, string current, EnemyWorkPcEvent e) in fallbackChanges.Take(80))
                lines.Add($"#{index}: {previous}->{current} {FormatEvent(e)}");
        }

        lines.Add(string.Empty);
        lines.Add("Interpretation hints");
        lines.Add("--------------------");
        lines.Add("- 42CC_REJECT_RESET and 42CF_FALLBACK_RESET are the per-enemy scratch reset writes at the start of Enemy_UpdateOne.");
        lines.Add("- 4315_REJECT_OR_CANDIDATE writes rejectedMask |= rejected preferred candidate after logical/local rejection.");
        lines.Add("- 4331_REJECT_OR_TEMPDIR writes rejectedMask |= current temp direction before entering fallback.");
        lines.Add("- 43C4_FALLBACK_STEP_INC / 43C5_FALLBACK_STEP_READ observe the fallback-step counter at 61C2.");
        lines.Add("- 3C2B_TILE_LOOKUP_RETURN validates only the HL address computed by 0x3C0A. 0x3C0A restores AF/BC before returning; A is not the tile value there.");
        lines.Add("- The true tile value is loaded by each caller after 0x3C0A, for example at 4143/4156/4169/417C in the 0x4130 local-door routine.");
        lines.Add("- 4187_LOCAL_DOOR_REJECT is the already-observed door/local-tile rejection point.");
        lines.Add("- 4241_FALLBACK_ENTRY is the already-observed generic fallback entry point.");
        lines.Add("- 4347_FORCED_REVERSAL is the already-observed forced-reversal point outside normal center decision logic.");
        lines.Add("- Active enemy count is derived by this analyzer from e0..e3 raw bytes because the debugger action cannot call Lua helpers at exact breakpoint time.");

        return lines;
    }

    private static void AppendTileLookup3C0aSummary(List<string> lines, IReadOnlyList<EnemyWorkPcEvent> events)
    {
        List<EnemyWorkPcEvent> tileEvents = events
            .Where(e => e.Source.Equals(TileLookupReturnSource, StringComparison.OrdinalIgnoreCase))
            .ToList();

        lines.Add(string.Empty);
        lines.Add("0x3C0A tile-address lookup summary");
        lines.Add("-----------------------------------");

        if (tileEvents.Count == 0)
        {
            lines.Add("no 3C2B_TILE_LOOKUP_RETURN events were captured");
            lines.Add("expected formula when present: actualHL(H,L) == D0A0 + ((D & F8) * 4) + (E >> 3)");
            return;
        }

        int comparable = 0;
        int matches = 0;
        int mismatches = 0;
        int missingRegisters = 0;
        int compositeHlMatchesActual = 0;
        int compositeHlDiffersFromActual = 0;
        string firstMismatch = string.Empty;
        string firstCompositeDifference = string.Empty;
        var uniqueAddresses = new HashSet<int>();
        var uniqueProbeCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contextCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (EnemyWorkPcEvent e in tileEvents)
        {
            string context = TileLookupContext(e);
            Increment(contextCounts, context);

            if (!TryParseHexByte(e.D, out int d) ||
                !TryParseHexByte(e.E, out int probeE) ||
                !TryParseHexByte(e.H, out int h) ||
                !TryParseHexByte(e.L, out int l))
            {
                missingRegisters++;
                continue;
            }

            comparable++;
            int expected = Compute3C0aExpectedHl(d, probeE);
            int actual = ((h & 0xFF) << 8) | (l & 0xFF);
            uniqueAddresses.Add(actual);
            uniqueProbeCells.Add($"{(d & 0xF8):X2}:{(probeE >> 3):X2}");

            if (TryParseHexWord(e.HlComposite, out int compositeHl))
            {
                if ((compositeHl & 0xFFFF) == actual)
                {
                    compositeHlMatchesActual++;
                }
                else
                {
                    compositeHlDiffersFromActual++;
                    if (string.IsNullOrEmpty(firstCompositeDifference))
                    {
                        firstCompositeDifference =
                            $"actualHL={FormatWord(actual)} compositeHl={FormatWord(compositeHl)} event={FormatEvent(e)}";
                    }
                }
            }

            if (actual == expected)
            {
                matches++;
            }
            else
            {
                mismatches++;
                if (string.IsNullOrEmpty(firstMismatch))
                {
                    firstMismatch =
                        $"expectedHL={FormatWord(expected)} actualHL={FormatWord(actual)} D={e.D} E={e.E} event={FormatEvent(e)}";
                }
            }
        }

        lines.Add($"events={tileEvents.Count}, comparable={comparable}, matches={matches}, mismatches={mismatches}, missingRegisters={missingRegisters}");
        lines.Add($"uniqueActualAddresses={uniqueAddresses.Count}, uniqueProbeCells={uniqueProbeCells.Count}");
        lines.Add("formula: actualHL = D0A0 + ((D & F8) * 4) + (E >> 3)");
        lines.Add("mapping: VRAM columns, bottom-to-top; D chooses the screen column group, E>>3 chooses the tile within that column.");
        lines.Add($"debuggerCompositeHlMatchesActual={compositeHlMatchesActual}, debuggerCompositeHlDiffersFromActual={compositeHlDiffersFromActual}");

        if (!string.IsNullOrEmpty(firstMismatch))
            lines.Add("first address mismatch: " + firstMismatch);

        if (!string.IsNullOrEmpty(firstCompositeDifference))
            lines.Add("first ignored composite-HL difference: " + firstCompositeDifference);

        lines.Add("contexts:");
        foreach (KeyValuePair<string, int> pair in contextCounts.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
            lines.Add($"  {pair.Key}: {pair.Value}");

        lines.Add("sample computed lookups:");
        int printed = 0;
        foreach (EnemyWorkPcEvent e in tileEvents)
        {
            if (printed >= 12)
                break;

            if (!TryParseHexByte(e.D, out int d) ||
                !TryParseHexByte(e.E, out int probeE) ||
                !TryParseHexByte(e.H, out int h) ||
                !TryParseHexByte(e.L, out int l))
            {
                continue;
            }

            int expected = Compute3C0aExpectedHl(d, probeE);
            int actual = ((h & 0xFF) << 8) | (l & 0xFF);
            lines.Add($"  D={e.D} E={e.E} actualHL={FormatWord(actual)} expectedHL={FormatWord(expected)} match={actual == expected} context={TileLookupContext(e)}");
            printed++;
        }
    }

    private static int Compute3C0aExpectedHl(int d, int e)
    {
        return 0xD0A0 + ((d & 0xF8) * 4) + ((e & 0xFF) >> 3);
    }

    private static string TileLookupContext(EnemyWorkPcEvent e)
    {
        // This is intentionally simple and based on the observable exact-PC state.
        // 0x3C0A is a shared helper, so until caller-specific tile-load breakpoints
        // are added, this only labels the most useful context families.
        if (e.Ix.Equals("61BE", StringComparison.OrdinalIgnoreCase))
            return "enemyWork-local-door-or-decision";

        if (e.Ix.Equals("6040", StringComparison.OrdinalIgnoreCase))
            return "enemy-update-probe";

        if (e.Ix.Equals("61A8", StringComparison.OrdinalIgnoreCase))
            return "player-or-global-probe";

        if (e.Ix.Equals("602B", StringComparison.OrdinalIgnoreCase) ||
            e.Ix.Equals("6030", StringComparison.OrdinalIgnoreCase) ||
            e.Ix.Equals("6035", StringComparison.OrdinalIgnoreCase) ||
            e.Ix.Equals("603A", StringComparison.OrdinalIgnoreCase))
        {
            return "enemy-slot-probe";
        }

        return "other-ix-" + e.Ix;
    }

    private static List<EnemyWorkCycle> BuildCycles(IReadOnlyList<EnemyWorkPcEvent> events)
    {
        var cycles = new List<EnemyWorkCycle>();
        EnemyWorkCycle? current = null;

        for (int i = 0; i < events.Count; i++)
        {
            EnemyWorkPcEvent e = events[i];

            if (e.Source.Equals("42CC_REJECT_RESET", StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                    cycles.Add(current);

                current = new EnemyWorkCycle(cycles.Count, i, e);
            }

            if (current != null)
                current.Events.Add(e);
        }

        if (current != null)
            cycles.Add(current);

        return cycles;
    }

    private static void AppendCycleSummary(List<string> lines, IReadOnlyList<EnemyWorkCycle> cycles)
    {
        lines.Add(string.Empty);
        lines.Add("Enemy_UpdateOne cycle summary");
        lines.Add("-----------------------------");

        if (cycles.Count == 0)
        {
            lines.Add("none");
            return;
        }

        int cyclesWithRejectWrites = cycles.Count(c => c.RejectWriters.Count > 0);
        int cyclesWithFallbackEntry = cycles.Count(c => c.FallbackEntries.Count > 0);
        int cyclesWithDoorReject = cycles.Count(c => c.DoorRejects.Count > 0);
        int cyclesWithForcedReversal = cycles.Count(c => c.ForcedReversals.Count > 0);

        lines.Add($"cycles: {cycles.Count}");
        lines.Add($"cycles with rejectedMask write candidates: {cyclesWithRejectWrites}");
        lines.Add($"cycles with local-door reject breakpoint: {cyclesWithDoorReject}");
        lines.Add($"cycles entering fallback: {cyclesWithFallbackEntry}");
        lines.Add($"cycles with forced reversal: {cyclesWithForcedReversal}");

        lines.Add(string.Empty);
        lines.Add("Fallback step-read count by cycle");
        lines.Add("---------------------------------");
        foreach (IGrouping<int, EnemyWorkCycle> group in cycles
                     .GroupBy(c => c.FallbackStepReads.Count)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key))
        {
            lines.Add($"{group.Key}: {group.Count()}");
        }

        lines.Add(string.Empty);
        lines.Add("Interesting cycles");
        lines.Add("------------------");

        int printed = 0;
        for (int i = 0; i < cycles.Count && printed < 80; i++)
        {
            EnemyWorkCycle cycle = cycles[i];

            bool interesting =
                cycle.RejectWriters.Count > 0 ||
                cycle.FallbackEntries.Count > 0 ||
                cycle.DoorRejects.Count > 0 ||
                cycle.ForcedReversals.Count > 0 ||
                cycle.FallbackStepReads.Count != 1;

            if (!interesting)
                continue;

            EnemyWorkCycle? next = i + 1 < cycles.Count ? cycles[i + 1] : null;
            lines.Add(FormatCycle(cycle, next));
            printed++;
        }

        if (printed == 0)
            lines.Add("none");
    }

    private static void AppendDecisionClassification(List<string> lines, IReadOnlyList<EnemyWorkCycle> cycles)
    {
        lines.Add(string.Empty);
        lines.Add("Decision-cycle classification");
        lines.Add("-----------------------------");

        if (cycles.Count == 0)
        {
            lines.Add("none");
            return;
        }

        var classified = cycles
            .Select((cycle, index) => new CycleView(cycle, index + 1 < cycles.Count ? cycles[index + 1] : null))
            .ToList();

        AppendClassificationCount(lines, "plain step / no rejected candidate", classified, c => c.Classification == CycleClassification.PlainStep);
        AppendClassificationCount(lines, "preferred rejected, current direction kept", classified, c => c.Classification == CycleClassification.PreferredRejectedCurrentKept);
        AppendClassificationCount(lines, "preferred/current rejected, fallback entered", classified, c => c.Classification == CycleClassification.FallbackAfterCurrentRejected);
        AppendClassificationCount(lines, "forced reversal outside decision center", classified, c => c.Classification == CycleClassification.ForcedReversal);
        AppendClassificationCount(lines, "other / mixed", classified, c => c.Classification == CycleClassification.Other);

        lines.Add(string.Empty);
        lines.Add("Rejected preferred but current direction kept");
        lines.Add("--------------------------------------------");
        var keepCurrent = classified
            .Where(c => c.Classification == CycleClassification.PreferredRejectedCurrentKept)
            .ToList();
        if (keepCurrent.Count == 0)
        {
            lines.Add("none");
        }
        else
        {
            foreach (IGrouping<string, CycleView> group in keepCurrent
                         .GroupBy(c => $"candidate={c.FirstRejectedCandidate} startDir={c.Cycle.Start.TempDir} nextDir={c.NextTempDir}")
                         .OrderByDescending(g => g.Count())
                         .ThenBy(g => g.Key))
            {
                lines.Add($"{group.Key}: {group.Count()} cycles [{JoinCycleIndexes(group)}]");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Fallback outcomes");
        lines.Add("-----------------");
        var fallback = classified
            .Where(c => c.Classification == CycleClassification.FallbackAfterCurrentRejected)
            .ToList();
        if (fallback.Count == 0)
        {
            lines.Add("none");
        }
        else
        {
            foreach (IGrouping<string, CycleView> group in fallback
                         .GroupBy(c => $"fallbackC1={c.FallbackRejectedMask} startDir={c.Cycle.Start.TempDir} nextDir={c.NextTempDir}")
                         .OrderByDescending(g => g.Count())
                         .ThenBy(g => g.Key))
            {
                lines.Add($"{group.Key}: {group.Count()} cycles [{JoinCycleIndexes(group)}]");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Decision interpretation hints");
        lines.Add("-----------------------------");
        lines.Add("- 4315 without 4331/fallback means: a preferred candidate was rejected, but the current temp direction remained acceptable and was kept.");
        lines.Add("- 4315 followed by 4331 and 4241 means: the preferred candidate was rejected, the current temp direction was also rejected, then the fallback finder selected another direction.");
        lines.Add("- nextDir is inferred from the next cycle's start temp direction. It is a cycle-level summary, not a separate exact-PC commit breakpoint.");
    }

    private static void AppendClassificationCount(
        List<string> lines,
        string label,
        IReadOnlyList<CycleView> cycles,
        Func<CycleView, bool> predicate)
    {
        lines.Add($"{label}: {cycles.Count(predicate)}");
    }

    private static string JoinCycleIndexes(IEnumerable<CycleView> cycles)
    {
        return string.Join(",", cycles.Select(c => c.Cycle.Index.ToString(CultureInfo.InvariantCulture)));
    }

    private static string FormatCycle(EnemyWorkCycle cycle, EnemyWorkCycle? next)
    {
        string rejectWrites = cycle.RejectWriters.Count == 0
            ? "none"
            : string.Join(",", cycle.RejectWriters.Select(e => $"{e.Source}@{e.Pc}:A={e.A}:preC1={e.Rejected}"));

        string doorRejects = cycle.DoorRejects.Count == 0
            ? "none"
            : string.Join(",", cycle.DoorRejects.Select(e => $"{e.Pc}:A={e.A}:HLactual={e.ActualHlText}:DE={e.DeComposite}"));

        string fallbackEntries = cycle.FallbackEntries.Count == 0
            ? "none"
            : string.Join(",", cycle.FallbackEntries.Select(e => $"{e.Pc}:A={e.A}:B={e.B}:preC1={e.Rejected}"));

        string forcedReversals = cycle.ForcedReversals.Count == 0
            ? "none"
            : string.Join(",", cycle.ForcedReversals.Select(e => $"{e.Pc}:A={e.A}:tmp={e.TempDir}:{e.TempX},{e.TempY}"));

        string nextText = next == null
            ? "next=none"
            : $"nextTmp={next.Start.TempDir}:{next.Start.TempX},{next.Start.TempY} nextE0={next.Start.Enemies[0]}";

        return string.Join(
            " ",
            $"cycle={cycle.Index}",
            $"eventIndex={cycle.StartEventIndex}",
            $"startTmp={cycle.Start.TempDir}:{cycle.Start.TempX},{cycle.Start.TempY}",
            $"startC1={cycle.Start.Rejected}",
            $"startC2={cycle.Start.Fallback}",
            $"startPref=[{cycle.Start.Preferred}]",
            $"startE0={cycle.Start.Enemies[0]}",
            $"rejectWrites={rejectWrites}",
            $"doorRejects={doorRejects}",
            $"fallbackEntries={fallbackEntries}",
            $"fallbackStepReads={cycle.FallbackStepReads.Count}",
            $"forcedReversals={forcedReversals}",
            nextText);
    }

    private static bool IsRejectedWriter(EnemyWorkPcEvent e)
    {
        return e.Source.Equals("42CC_REJECT_RESET", StringComparison.OrdinalIgnoreCase) ||
               e.Source.Equals("4315_REJECT_OR_CANDIDATE", StringComparison.OrdinalIgnoreCase) ||
               e.Source.Equals("4331_REJECT_OR_TEMPDIR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFallbackStep(EnemyWorkPcEvent e)
    {
        return e.Source.Equals("42CF_FALLBACK_RESET", StringComparison.OrdinalIgnoreCase) ||
               e.Source.Equals("43C4_FALLBACK_STEP_INC", StringComparison.OrdinalIgnoreCase) ||
               e.Source.Equals("43C5_FALLBACK_STEP_READ", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendEventSection(List<string> lines, string title, IEnumerable<EnemyWorkPcEvent> events)
    {
        lines.Add(string.Empty);
        lines.Add(title);
        lines.Add(new string('-', title.Length));

        int count = 0;
        foreach (EnemyWorkPcEvent e in events)
        {
            lines.Add(FormatEvent(e));
            count++;
        }

        if (count == 0)
            lines.Add("none");
    }

    private static List<(int index, string previous, string current, EnemyWorkPcEvent e)> CollectChanges(
        IReadOnlyList<EnemyWorkPcEvent> events,
        Func<EnemyWorkPcEvent, string> selector)
    {
        var changes = new List<(int index, string previous, string current, EnemyWorkPcEvent e)>();
        if (events.Count == 0)
            return changes;

        string previous = selector(events[0]);
        for (int i = 1; i < events.Count; i++)
        {
            string current = selector(events[i]);
            if (!string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add((i, previous, current, events[i]));
                previous = current;
            }
        }

        return changes;
    }

    private static void AppendCountSection(
        List<string> lines,
        string title,
        IOrderedEnumerable<IGrouping<string, EnemyWorkPcEvent>> groups)
    {
        lines.Add(string.Empty);
        lines.Add(title);
        lines.Add(new string('-', title.Length));

        foreach (IGrouping<string, EnemyWorkPcEvent> group in groups)
            lines.Add($"{group.Key}: {group.Count()}");
    }

    private static string FormatEvent(EnemyWorkPcEvent e)
    {
        return string.Join(
            " ",
            $"source={e.Source}",
            $"pc={e.Pc}",
            $"r={e.R}",
            $"a={e.A}",
            $"b={e.B}",
            $"c={e.C}",
            $"d={e.D}",
            $"e={e.E}",
            $"h={e.H}",
            $"l={e.L}",
            $"actualHL={e.ActualHlText}",
            $"loggedHL={e.HlComposite}",
            $"de={e.DeComposite}",
            $"ix={e.Ix}",
            $"iy={e.Iy}",
            $"tmp={e.TempDir}:{e.TempX},{e.TempY}",
            $"rejected={e.Rejected}",
            $"fallback={e.Fallback}",
            $"preferred=[{e.Preferred}]",
            $"chase=[{e.Chase}]",
            $"rr={e.RoundRobin}",
            $"active={e.ActiveEnemyCount}",
            $"player={e.PlayerDir}:{e.PlayerX},{e.PlayerY}",
            $"e0={e.Enemies[0]}",
            $"e1={e.Enemies[1]}",
            $"e2={e.Enemies[2]}",
            $"e3={e.Enemies[3]}");
    }

    private static List<EnemyWorkPcEvent> ParseEvents(string errorLogText)
    {
        var events = new List<EnemyWorkPcEvent>();

        string[] lines = errorLogText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (string rawLine in lines)
        {
            int markerIndex = rawLine.IndexOf(Marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            string payload = rawLine[(markerIndex + Marker.Length)..].Trim();
            Dictionary<string, string> values = ParsePayload(payload);
            EnemySnapshot[] enemies = new[]
            {
                ReadEnemy(values, 0),
                ReadEnemy(values, 1),
                ReadEnemy(values, 2),
                ReadEnemy(values, 3)
            };

            events.Add(new EnemyWorkPcEvent(
                Get(values, "source"),
                Get(values, "pc"),
                Get(values, "r"),
                Get(values, "a"),
                Get(values, "b"),
                Get(values, "c"),
                Get(values, "d"),
                Get(values, "e"),
                Get(values, "h"),
                Get(values, "l"),
                Get(values, "hl"),
                Get(values, "de"),
                Get(values, "ix"),
                Get(values, "iy"),
                Get(values, "tmpDir"),
                Get(values, "tmpX"),
                Get(values, "tmpY"),
                Get(values, "rejected"),
                Get(values, "fallback"),
                Join4(values, "p0", "p1", "p2", "p3"),
                Join4(values, "chase0", "chase1", "chase2", "chase3"),
                Get(values, "rr"),
                Get(values, "playerDir"),
                Get(values, "playerX"),
                Get(values, "playerY"),
                enemies));
        }

        return events;
    }

    private static Dictionary<string, string> ParsePayload(string payload)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = part[..equals].Trim();
            string value = part[(equals + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }

    private static string Get(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "??";
    }

    private static string Join4(Dictionary<string, string> values, string a, string b, string c, string d)
    {
        return string.Join(",", Get(values, a), Get(values, b), Get(values, c), Get(values, d));
    }

    private static EnemySnapshot ReadEnemy(Dictionary<string, string> values, int slot)
    {
        string prefix = "e" + slot.ToString(CultureInfo.InvariantCulture);
        return new EnemySnapshot(
            Get(values, prefix + "Raw"),
            Get(values, prefix + "X"),
            Get(values, prefix + "Y"));
    }

    private static int CountActiveEnemies(IReadOnlyList<EnemySnapshot> enemies)
    {
        int count = 0;
        foreach (EnemySnapshot enemy in enemies)
        {
            if (TryParseHexByte(enemy.Raw, out int raw) && (raw & 0x02) != 0)
                count++;
        }

        return count;
    }

    private static bool TryParseHexByte(string value, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value) || value == "??")
            return false;

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseHexWord(string value, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value) || value == "??")
            return false;

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
    }

    private static string FormatWord(int value)
    {
        return (value & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        if (!counts.TryGetValue(key, out int count))
            count = 0;

        counts[key] = count + 1;
    }

    private sealed class CycleView
    {
        public CycleView(EnemyWorkCycle cycle, EnemyWorkCycle? next)
        {
            Cycle = cycle;
            Next = next;
        }

        public EnemyWorkCycle Cycle { get; }
        public EnemyWorkCycle? Next { get; }

        public string NextTempDir => Next?.Start.TempDir ?? "??";

        public string FirstRejectedCandidate => Cycle.RejectWriters.Count == 0
            ? "??"
            : Cycle.RejectWriters[0].A;

        public string FallbackRejectedMask => Cycle.FallbackEntries.Count == 0
            ? "??"
            : Cycle.FallbackEntries[0].A;

        public CycleClassification Classification
        {
            get
            {
                if (Cycle.ForcedReversals.Count > 0)
                    return CycleClassification.ForcedReversal;

                if (Cycle.FallbackEntries.Count > 0)
                    return CycleClassification.FallbackAfterCurrentRejected;

                if (Cycle.RejectWriters.Count == 1 && Cycle.FallbackEntries.Count == 0)
                    return CycleClassification.PreferredRejectedCurrentKept;

                if (Cycle.RejectWriters.Count == 0 && Cycle.FallbackEntries.Count == 0 && Cycle.DoorRejects.Count == 0)
                    return CycleClassification.PlainStep;

                return CycleClassification.Other;
            }
        }
    }

    private enum CycleClassification
    {
        PlainStep,
        PreferredRejectedCurrentKept,
        FallbackAfterCurrentRejected,
        ForcedReversal,
        Other
    }

    private sealed class EnemyWorkCycle
    {
        public EnemyWorkCycle(int index, int startEventIndex, EnemyWorkPcEvent start)
        {
            Index = index;
            StartEventIndex = startEventIndex;
            Start = start;
        }

        public int Index { get; }
        public int StartEventIndex { get; }
        public EnemyWorkPcEvent Start { get; }
        public List<EnemyWorkPcEvent> Events { get; } = new();

        public List<EnemyWorkPcEvent> RejectWriters =>
            Events.Where(e =>
                e.Source.Equals("4315_REJECT_OR_CANDIDATE", StringComparison.OrdinalIgnoreCase) ||
                e.Source.Equals("4331_REJECT_OR_TEMPDIR", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public List<EnemyWorkPcEvent> DoorRejects =>
            Events.Where(e => e.Source.Equals("4187_LOCAL_DOOR_REJECT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public List<EnemyWorkPcEvent> FallbackEntries =>
            Events.Where(e =>
                e.Source.Equals("4241_FALLBACK_ENTRY", StringComparison.OrdinalIgnoreCase) ||
                e.Source.Equals("4241_FALLBACK", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public List<EnemyWorkPcEvent> FallbackStepReads =>
            Events.Where(e => e.Source.Equals("43C5_FALLBACK_STEP_READ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public List<EnemyWorkPcEvent> ForcedReversals =>
            Events.Where(e => e.Source.Equals("4347_FORCED_REVERSAL", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed record EnemyWorkPcEvent(
        string Source,
        string Pc,
        string R,
        string A,
        string B,
        string C,
        string D,
        string E,
        string H,
        string L,
        string HlComposite,
        string DeComposite,
        string Ix,
        string Iy,
        string TempDir,
        string TempX,
        string TempY,
        string Rejected,
        string Fallback,
        string Preferred,
        string Chase,
        string RoundRobin,
        string PlayerDir,
        string PlayerX,
        string PlayerY,
        EnemySnapshot[] Enemies)
    {
        public int ActiveEnemyCount => CountActiveEnemies(Enemies);

        public string ActualHlText
        {
            get
            {
                if (!TryParseHexByte(H, out int h) || !TryParseHexByte(L, out int l))
                    return "????";

                return FormatWord((h << 8) | l);
            }
        }
    }

    private sealed record EnemySnapshot(string Raw, string X, string Y)
    {
        public override string ToString() => string.Join(":", Raw, X + "," + Y);
    }
}
