using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// v0.7.13 bridge diagnostic that feeds Enemy_UpdateOne's selected preferred[slot]
/// from the imported exact-PC preferred[] tape instead of from the standard-trace
/// tuple classifier.
///
/// The standard JSONL trace must remain debugger-free.  The exact-PC tape is read
/// from the separate EnemyWork diagnostic, normally tools/mame/lua/error.log.
/// v0.7.12 proved that the active-frame standard tuple sequence aligns with a
/// contiguous window of the imported 0x2E5C call tape.  This model uses that best
/// tuple window as a replay provider and verifies the selected preferred[slot]
/// consumed by Enemy_UpdateOne for the current one-enemy static-player sequence.
///
/// This is still a shadow check: it does not move enemies and it does not alter the
/// authoritative comparison frames.
/// </summary>
public static class LadyBugEnemyUpdateOneExactPcPreferredInputShadowModel
{
    private const string Version = "v0.7.13";
    private const string Marker = "LBEW|";

    private static readonly string[] PreferredSources =
    {
        "2E5C_PREF_ENTRY",
        "2E8C_PREF_ROTATE_BRANCH",
        "2E97_PREF_ROTATE_WRITE",
        "2E9E_PREF_RANDOM_BRANCH",
        "2EA5_PREF_RANDOM_R_VALUE",
        "2EC7_PREF_RANDOM_WRITE",
        "2ECB_PREF_CALL_BFS_OVERRIDE",
        "46D8_BFS_OVERRIDE_ENTRY",
        "477D_BFS_OVERRIDE_WRITE"
    };

    private sealed class PcEvent
    {
        public string Source = string.Empty;
        public string A = string.Empty;
        public string Iy = string.Empty;
    }

    private sealed class PreferredCall
    {
        public PreferredCall(int index)
        {
            Index = index;
        }

        public int Index { get; }
        public PcEvent? RotateBranch;
        public PcEvent? RandomBranch;
        public readonly List<PcEvent> RandomRValues = new();
        public readonly List<PcEvent> BfsWrites = new();
    }

    private sealed class TapeTuple
    {
        public int CallIndex;
        public int[] Tuple = new[] { 0, 0, 0, 0 };
        public string Source = string.Empty;
    }

    private sealed class StandardTuple
    {
        public int FrameIndex;
        public int Tick;
        public int MameFrame;
        public int[] Tuple = new[] { 0, 0, 0, 0 };
        public string Source = string.Empty;
    }

    private sealed class AlignmentResult
    {
        public int Start = -1;
        public int TupleMatches;
        public int TupleMismatches;
        public int SourceMatches;
        public int SourceMismatches;
        public string FirstMismatch = string.Empty;
    }

    private sealed class Counters
    {
        public int Frames;
        public int Transitions;
        public int StandardActiveProviderFrames;
        public int StandardProviderSkips;
        public int ImportedMarkerCount;
        public string ImportedPath = string.Empty;
        public bool FileFound;
        public int PreferredEvents;
        public int TapeCalls;
        public int ModeledTapeCalls;
        public int UnmodeledTapeCalls;
        public int BestWindowStart = -1;
        public int BestWindowTupleMatches;
        public int BestWindowTupleMismatches;
        public int BestWindowSourceMatches;
        public int BestWindowSourceMismatches;
        public int Checks;
        public int Matches;
        public int Mismatches;
        public int SkippedMissingEnemyWork;
        public int SkippedNoActiveEnemy;
        public int SkippedMissingReferencePreferred;
        public int SkippedNoAlignedTuple;
        public int Slot0;
        public int Slot1;
        public int Slot2;
        public int Slot3;
        public string FirstMatch = string.Empty;
        public string FirstMismatch = string.Empty;
        public string FirstAlignmentMismatch = string.Empty;
        public readonly Dictionary<string, int> ExactProviderSources = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> SelectedPreferredValues = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Skips = new(StringComparer.Ordinal);
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var counters = new Counters
        {
            Frames = referenceFrames.Count
        };

        List<StandardTuple> standard = BuildStandardSequence(referenceFrames, out int skippedStandard);
        counters.StandardActiveProviderFrames = standard.Count;
        counters.StandardProviderSkips = skippedStandard;

        if (!TryFindExactPcRawLog(out string path, out int markerCount))
            return BuildMissingFileSummary(counters);

        counters.FileFound = true;
        counters.ImportedPath = path;
        counters.ImportedMarkerCount = markerCount;

        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return "Lady Bug Enemy_UpdateOne exact-PC preferred-input shadow " + Version +
                   ": fileFound=true, importedPath=" + ToDisplayPath(path) +
                   ", readError=" + ex.Message +
                   ". NOTE: comparison remains valid; regenerate the exact-PC diagnostic if this file is locked or corrupt.";
        }

        List<PcEvent> preferredEvents = ParsePreferredEvents(text);
        counters.PreferredEvents = preferredEvents.Count;

        List<PreferredCall> calls = BuildCalls(preferredEvents);
        counters.TapeCalls = calls.Count;

        List<TapeTuple> tape = BuildTapeTuples(calls, out int unmodeledCalls, out _);
        counters.ModeledTapeCalls = tape.Count;
        counters.UnmodeledTapeCalls = unmodeledCalls;

        AlignmentResult alignment = FindBestWindow(standard, tape);
        counters.BestWindowStart = alignment.Start;
        counters.BestWindowTupleMatches = alignment.TupleMatches;
        counters.BestWindowTupleMismatches = alignment.TupleMismatches;
        counters.BestWindowSourceMatches = alignment.SourceMatches;
        counters.BestWindowSourceMismatches = alignment.SourceMismatches;
        counters.FirstAlignmentMismatch = alignment.FirstMismatch;

        if (alignment.Start < 0 || alignment.TupleMismatches != 0)
            return BuildSummaryText(counters, exactProviderUsable: false);

        AnalyzeTransitions(referenceFrames, tape, alignment.Start, counters);
        return BuildSummaryText(counters, exactProviderUsable: true);
    }

    private static void AnalyzeTransitions(
        IReadOnlyList<EnemyTraceFrame> referenceFrames,
        IReadOnlyList<TapeTuple> tape,
        int tapeStart,
        Counters counters)
    {
        int activeProviderIndex = 0;

        for (int i = 1; i < referenceFrames.Count; i++)
        {
            counters.Transitions++;
            EnemyTraceFrame currentFrame = referenceFrames[i];

            if (currentFrame.enemyWork == null)
            {
                counters.SkippedMissingEnemyWork++;
                continue;
            }

            if (!TrySelectEnemyWorkSlot(currentFrame, out EnemyTraceActor? currentEnemy) || currentEnemy == null)
            {
                counters.SkippedNoActiveEnemy++;
                Count(counters.Skips, "no-active-enemy");
                continue;
            }

            int providerIndex = activeProviderIndex++;
            int tapeIndex = tapeStart + providerIndex;

            int slot = ClampSlot(currentEnemy.slot);
            CountSlot(counters, slot);

            if (currentFrame.enemyWork.preferred == null || currentFrame.enemyWork.preferred.Count <= slot)
            {
                counters.SkippedMissingReferencePreferred++;
                Count(counters.Skips, "missing-reference-preferred");
                continue;
            }

            if (tapeIndex < 0 || tapeIndex >= tape.Count)
            {
                counters.SkippedNoAlignedTuple++;
                Count(counters.Skips, "no-aligned-tape-tuple");
                continue;
            }

            TapeTuple exactTuple = tape[tapeIndex];
            counters.Checks++;
            Count(counters.ExactProviderSources, exactTuple.Source);

            int modeledPreferred = exactTuple.Tuple[slot] & 0x0F;
            int referencePreferred = currentFrame.enemyWork.preferred[slot] & 0x0F;
            Count(counters.SelectedPreferredValues, "slot" + slot.ToString(CultureInfo.InvariantCulture) + ":" + FormatByte(modeledPreferred));

            string context =
                "tick=" + currentFrame.frame +
                " mameFrame=" + currentFrame.mameFrame +
                " slot=" + slot.ToString(CultureInfo.InvariantCulture) +
                " tapeCall=" + exactTuple.CallIndex.ToString(CultureInfo.InvariantCulture) +
                " provider=" + exactTuple.Source +
                " preferredRef=" + FormatByte(referencePreferred) +
                " preferredModel=" + FormatByte(modeledPreferred) +
                " tuple=" + LadyBugMonsterPreferenceSystem.FormatTuple(exactTuple.Tuple) +
                " work=" + FormatByte(currentFrame.enemyWork.tempDir) + ":" +
                    FormatByte(currentFrame.enemyWork.tempX) + "," +
                    FormatByte(currentFrame.enemyWork.tempY) +
                " enemy=" + FormatByte(currentEnemy.raw) + ":" +
                    FormatByte(currentEnemy.x) + "," +
                    FormatByte(currentEnemy.y);

            if (modeledPreferred == referencePreferred)
            {
                counters.Matches++;
                if (string.IsNullOrEmpty(counters.FirstMatch))
                    counters.FirstMatch = context;
                continue;
            }

            counters.Mismatches++;
            if (string.IsNullOrEmpty(counters.FirstMismatch))
                counters.FirstMismatch = context;
        }
    }

    private static List<StandardTuple> BuildStandardSequence(IReadOnlyList<EnemyTraceFrame> frames, out int skipped)
    {
        var sequence = new List<StandardTuple>();
        skipped = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            EnemyTraceFrame frame = frames[i];
            if (!LadyBugEnemyPreferredGeneratorReplayShadowModel.TryBuildPreferredTupleForActiveFrame(
                    frame,
                    out int[] tuple,
                    out string source,
                    out _))
            {
                skipped++;
                continue;
            }

            sequence.Add(new StandardTuple
            {
                FrameIndex = i,
                Tick = frame.frame,
                MameFrame = frame.mameFrame,
                Tuple = NormalizeTuple(tuple),
                Source = source
            });
        }

        return sequence;
    }

    private static AlignmentResult FindBestWindow(IReadOnlyList<StandardTuple> standard, IReadOnlyList<TapeTuple> tape)
    {
        var best = new AlignmentResult();

        if (standard.Count == 0 || tape.Count == 0 || tape.Count < standard.Count)
        {
            best.FirstMismatch = "not enough modeled tape calls for standard sequence";
            return best;
        }

        for (int start = 0; start <= tape.Count - standard.Count; start++)
        {
            int tupleMatches = 0;
            int sourceMatches = 0;
            string firstMismatch = string.Empty;

            for (int i = 0; i < standard.Count; i++)
            {
                StandardTuple s = standard[i];
                TapeTuple t = tape[start + i];

                if (LadyBugMonsterPreferenceSystem.TupleEquals(s.Tuple, t.Tuple))
                {
                    tupleMatches++;
                }
                else if (string.IsNullOrEmpty(firstMismatch))
                {
                    firstMismatch =
                        "stdIndex=" + i.ToString(CultureInfo.InvariantCulture) +
                        " tick=" + s.Tick.ToString(CultureInfo.InvariantCulture) +
                        " mameFrame=" + s.MameFrame.ToString(CultureInfo.InvariantCulture) +
                        " tapeCall=" + t.CallIndex.ToString(CultureInfo.InvariantCulture) +
                        " std=" + s.Source + LadyBugMonsterPreferenceSystem.FormatTuple(s.Tuple) +
                        " tape=" + t.Source + LadyBugMonsterPreferenceSystem.FormatTuple(t.Tuple);
                }

                if (string.Equals(s.Source, t.Source, StringComparison.Ordinal))
                    sourceMatches++;
            }

            int tupleMismatches = standard.Count - tupleMatches;
            int sourceMismatches = standard.Count - sourceMatches;

            if (best.Start < 0 ||
                tupleMatches > best.TupleMatches ||
                (tupleMatches == best.TupleMatches && sourceMatches > best.SourceMatches))
            {
                best.Start = start;
                best.TupleMatches = tupleMatches;
                best.TupleMismatches = tupleMismatches;
                best.SourceMatches = sourceMatches;
                best.SourceMismatches = sourceMismatches;
                best.FirstMismatch = firstMismatch;
            }
        }

        return best;
    }

    private static List<TapeTuple> BuildTapeTuples(IReadOnlyList<PreferredCall> calls, out int unmodeled, out string firstUnmodeled)
    {
        var result = new List<TapeTuple>();
        unmodeled = 0;
        firstUnmodeled = string.Empty;

        foreach (PreferredCall call in calls)
        {
            if (!TryBuildTapeTuple(call, out TapeTuple? tuple, out string error) || tuple == null)
            {
                unmodeled++;
                if (string.IsNullOrEmpty(firstUnmodeled))
                    firstUnmodeled = "call=" + call.Index + " " + error;
                continue;
            }

            result.Add(tuple);
        }

        return result;
    }

    private static bool TryBuildTapeTuple(PreferredCall call, out TapeTuple? result, out string error)
    {
        result = null;
        error = string.Empty;

        int[] tuple;
        string source;

        if (call.RotateBranch != null)
        {
            if (!TryParseHexByte(call.RotateBranch.A, out int seed))
            {
                error = "bad rotate seed";
                return false;
            }

            tuple = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            source = "2E97_ROTATE_FROM_" + FormatByte(seed);
        }
        else if (call.RandomBranch != null)
        {
            if (call.RandomRValues.Count < LadyBugMonsterPreferenceSystem.PreferredSlotCount)
            {
                error = "random branch has only " + call.RandomRValues.Count + " LD A,R values";
                return false;
            }

            tuple = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];
            for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
            {
                if (!TryParseHexByte(call.RandomRValues[i].A, out int rValue))
                {
                    error = "bad random R value at slot " + i;
                    return false;
                }

                tuple[i] = LadyBugMonsterPreferenceSystem.DirectionFromRandomNibble(rValue);
            }

            int firstRLow = TryParseHexByte(call.RandomRValues[0].A, out int firstR)
                ? firstR & 0x0F
                : 0;
            source = "2EC7_RANDOM_RLOW_" + firstRLow.ToString("X1", CultureInfo.InvariantCulture);
        }
        else
        {
            error = "missing branch marker";
            return false;
        }

        string baseSource = source;
        foreach (PcEvent bfsWrite in call.BfsWrites)
        {
            if (!TryParseHexWord(bfsWrite.Iy, out int iyAddress) ||
                !TryParseHexByte(bfsWrite.A, out int direction) ||
                !LadyBugMonsterPreferenceSystem.TryApplyBfsOverride(tuple, iyAddress, direction))
            {
                error = "invalid BFS override";
                return false;
            }

            int slot = iyAddress - LadyBugMonsterPreferenceSystem.PreferredBaseAddress;
            source = slot == 0
                ? "477D_SLOT0_BFS_OVER_" + baseSource
                : "477D_SLOT" + slot.ToString(CultureInfo.InvariantCulture) + "_BFS_OVER_" + baseSource;
        }

        result = new TapeTuple
        {
            CallIndex = call.Index,
            Tuple = NormalizeTuple(tuple),
            Source = source
        };
        return true;
    }

    private static List<PcEvent> ParsePreferredEvents(string text)
    {
        var events = new List<PcEvent>();
        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (string line in lines)
        {
            int markerIndex = line.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            string payload = line[(markerIndex + Marker.Length)..];
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in payload.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;

                fields[part[..eq]] = part[(eq + 1)..];
            }

            string source = Get(fields, "source");
            if (string.IsNullOrWhiteSpace(source) || !IsPreferredSource(source))
                continue;

            events.Add(new PcEvent
            {
                Source = source,
                A = Get(fields, "a"),
                Iy = Get(fields, "iy")
            });
        }

        return events;
    }

    private static List<PreferredCall> BuildCalls(List<PcEvent> preferredEvents)
    {
        var calls = new List<PreferredCall>();
        PreferredCall? current = null;

        foreach (PcEvent ev in preferredEvents)
        {
            if (ev.Source.Equals("2E5C_PREF_ENTRY", StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                    calls.Add(current);

                current = new PreferredCall(calls.Count);
            }

            current ??= new PreferredCall(calls.Count);

            if (ev.Source.Equals("2E8C_PREF_ROTATE_BRANCH", StringComparison.OrdinalIgnoreCase))
                current.RotateBranch = ev;
            else if (ev.Source.Equals("2E9E_PREF_RANDOM_BRANCH", StringComparison.OrdinalIgnoreCase))
                current.RandomBranch = ev;
            else if (ev.Source.Equals("2EA5_PREF_RANDOM_R_VALUE", StringComparison.OrdinalIgnoreCase))
                current.RandomRValues.Add(ev);
            else if (ev.Source.Equals("477D_BFS_OVERRIDE_WRITE", StringComparison.OrdinalIgnoreCase))
                current.BfsWrites.Add(ev);
        }

        if (current != null)
            calls.Add(current);

        return calls;
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

    private static int ClampSlot(int slot)
    {
        if (slot < 0)
            return 0;
        if (slot > 3)
            return 3;
        return slot;
    }

    private static void CountSlot(Counters counters, int slot)
    {
        switch (slot)
        {
            case 0:
                counters.Slot0++;
                break;
            case 1:
                counters.Slot1++;
                break;
            case 2:
                counters.Slot2++;
                break;
            case 3:
                counters.Slot3++;
                break;
        }
    }

    private static bool TryFindExactPcRawLog(out string path, out int markerCount)
    {
        path = string.Empty;
        markerCount = 0;

        var candidates = new List<string>();
        AddExplicitCandidate(candidates, "res://tools/mame/lua/error.log");
        AddCandidates(candidates, "res://traces/mame", "*_enemywork_pc_hits.log");
        AddCandidates(candidates, "res://tools/mame/lua", "*_enemywork_pc_hits.log");

        if (candidates.Count == 0)
            return false;

        var scored = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                Path = p,
                Markers = CountLbewMarkers(p),
                LastWrite = SafeLastWriteUtc(p)
            })
            .OrderByDescending(x => x.Markers > 0)
            .ThenByDescending(x => x.LastWrite)
            .ToList();

        var best = scored[0];
        path = best.Path;
        markerCount = best.Markers;
        return true;
    }

    private static void AddExplicitCandidate(List<string> candidates, string path)
    {
        string globalPath = ToGlobalPath(path);
        if (System.IO.File.Exists(globalPath))
            candidates.Add(globalPath);
    }

    private static void AddCandidates(List<string> candidates, string resDirectory, string pattern)
    {
        string directory = ToGlobalPath(resDirectory);
        if (!Directory.Exists(directory))
            return;

        try
        {
            candidates.AddRange(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));
        }
        catch
        {
            // Best effort only.
        }
    }

    private static int CountLbewMarkers(string path)
    {
        try
        {
            int count = 0;
            foreach (string line in System.IO.File.ReadLines(path))
            {
                if (line.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try
        {
            return System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string BuildMissingFileSummary(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug Enemy_UpdateOne exact-PC preferred-input shadow ").Append(Version).Append(": ");
        builder.Append("fileFound=false");
        builder.Append(", frames=").Append(counters.Frames);
        builder.Append(", standardActiveProviderFrames=").Append(counters.StandardActiveProviderFrames);
        builder.Append(", standardProviderSkips=").Append(counters.StandardProviderSkips);
        builder.Append(". NOTE: generate the exact-PC diagnostic separately with Lua script res://tools/mame/lua/ladybug_enemywork_pc_trace.lua and prefix ladybug_sequence_v8_enemywork_pcdiag, then reload the normal JSONL trace and run Compare again.");
        return builder.ToString();
    }

    private static string BuildSummaryText(Counters counters, bool exactProviderUsable)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug Enemy_UpdateOne exact-PC preferred-input shadow ").Append(Version).Append(": ");
        builder.Append("fileFound=").Append(counters.FileFound ? "true" : "false");
        builder.Append(", importedPath=").Append(ToDisplayPath(counters.ImportedPath));
        builder.Append(", importedMarkerCount=").Append(counters.ImportedMarkerCount);
        builder.Append(", preferredEvents=").Append(counters.PreferredEvents);
        builder.Append(", tapeCalls=").Append(counters.TapeCalls);
        builder.Append(", modeledTapeCalls=").Append(counters.ModeledTapeCalls);
        builder.Append(", unmodeledTapeCalls=").Append(counters.UnmodeledTapeCalls);
        builder.Append(", frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", standardActiveProviderFrames=").Append(counters.StandardActiveProviderFrames);
        builder.Append(", standardProviderSkips=").Append(counters.StandardProviderSkips);
        builder.Append(", bestWindowStart=").Append(counters.BestWindowStart);
        builder.Append(", bestWindowTupleMatches=").Append(counters.BestWindowTupleMatches);
        builder.Append(", bestWindowTupleMismatches=").Append(counters.BestWindowTupleMismatches);
        builder.Append(", bestWindowSourceMatches=").Append(counters.BestWindowSourceMatches);
        builder.Append(", bestWindowSourceMismatches=").Append(counters.BestWindowSourceMismatches);
        builder.Append(", exactProviderUsable=").Append(exactProviderUsable ? "true" : "false");
        builder.Append(", checks=").Append(counters.Checks);
        builder.Append(", matches=").Append(counters.Matches);
        builder.Append(", mismatches=").Append(counters.Mismatches);
        builder.Append(", skippedMissingEnemyWork=").Append(counters.SkippedMissingEnemyWork);
        builder.Append(", skippedNoActiveEnemy=").Append(counters.SkippedNoActiveEnemy);
        builder.Append(", skippedMissingReferencePreferred=").Append(counters.SkippedMissingReferencePreferred);
        builder.Append(", skippedNoAlignedTuple=").Append(counters.SkippedNoAlignedTuple);
        builder.Append(", slots=[0:").Append(counters.Slot0)
            .Append(",1:").Append(counters.Slot1)
            .Append(",2:").Append(counters.Slot2)
            .Append(",3:").Append(counters.Slot3).Append("]");

        if (!string.IsNullOrEmpty(counters.FirstMatch))
            builder.Append(", firstMatch: ").Append(counters.FirstMatch);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);
        else if (counters.Checks > 0)
            builder.Append(", firstMismatch: none");

        if (!string.IsNullOrEmpty(counters.FirstAlignmentMismatch))
            builder.Append(", firstAlignmentMismatch: ").Append(counters.FirstAlignmentMismatch);

        builder.Append(", exactProviderSources: ").Append(DescribeDictionary(counters.ExactProviderSources));
        builder.Append(", selectedPreferredValues: ").Append(DescribeDictionary(counters.SelectedPreferredValues));
        builder.Append(", skips: ").Append(DescribeDictionary(counters.Skips));
        builder.Append(". NOTE: shadow-only. v0.7.13 feeds the selected preferred[slot] from the imported exact-PC tape window aligned in v0.7.12. The standard JSONL trace remains debugger-free. This does not yet replace the visible simulation or prove exact per-frame PC timing; it validates the preferred input bridge for the current one-enemy static-player sequence.");
        return builder.ToString();
    }

    private static bool IsPreferredSource(string source)
    {
        foreach (string preferredSource in PreferredSources)
        {
            if (source.Equals(preferredSource, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int[] NormalizeTuple(IReadOnlyList<int> tuple)
    {
        var result = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];
        for (int i = 0; i < result.Length && i < tuple.Count; i++)
            result[i] = tuple[i] & 0x0F;
        return result;
    }

    private static string Get(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out string? value) ? value.Trim() : string.Empty;
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
        foreach (KeyValuePair<string, int> pair in values.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));

        return string.Join("; ", parts);
    }

    private static bool TryParseHexByte(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHexWord(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string ToGlobalPath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return Godot.ProjectSettings.GlobalizePath(path);
        }

        return path;
    }

    private static string ToDisplayPath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return string.Empty;

        try
        {
            string projectRoot = Godot.ProjectSettings.GlobalizePath("res://").Replace('\\', '/');
            string normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');

            if (!projectRoot.EndsWith('/'))
                projectRoot += "/";

            if (normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return "res://" + normalized[projectRoot.Length..];
        }
        catch
        {
            // Best effort display only.
        }

        return absolutePath;
    }
}
