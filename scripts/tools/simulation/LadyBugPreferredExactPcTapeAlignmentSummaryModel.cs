using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// v0.7.12 diagnostic alignment between the stable standard JSONL trace and the
/// separately imported exact-PC preferred[] tape.
///
/// This is intentionally weaker than true frame-accurate alignment: raw MAME
/// error.log LBEW lines do not carry the standard JSONL tick number.  The model
/// therefore reconstructs complete 0x2E5C preferred[] calls from the exact-PC tape,
/// reconstructs the active-frame preferred[] provider sequence from the normal
/// JSONL trace, and searches for the best contiguous tuple window in the tape.
///
/// A perfect tuple window means the imported tape contains a sequence compatible
/// with the 496 active standard frames.  It does not yet prove the exact tick for
/// each call.  That will require adding an explicit stable time marker to the
/// exact-PC diagnostic or a post-processing alignment key.
/// </summary>
public static class LadyBugPreferredExactPcTapeAlignmentSummaryModel
{
    private const string Version = "v0.7.12";
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
        public string P0 = string.Empty;
        public string P1 = string.Empty;
        public string P2 = string.Empty;
        public string P3 = string.Empty;
    }

    private sealed class PreferredCall
    {
        public PreferredCall(int index)
        {
            Index = index;
        }

        public int Index { get; }
        public readonly List<PcEvent> Events = new();
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

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> standardTraceFrames)
    {
        List<StandardTuple> standard = BuildStandardSequence(standardTraceFrames, out int skippedStandard);

        if (!TryFindExactPcRawLog(out string path, out int markerCount))
        {
            return "Lady Bug preferred[] exact-PC tape alignment " + Version +
                   ": fileFound=false, standardActiveProviderFrames=" + standard.Count +
                   ", standardProviderSkips=" + skippedStandard +
                   ". NOTE: generate the separate exact-PC diagnostic first. This alignment model reads res://tools/mame/lua/error.log or a usable *_enemywork_pc_hits.log companion file.";
        }

        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return "Lady Bug preferred[] exact-PC tape alignment " + Version +
                   ": fileFound=true, importedPath=" + ToDisplayPath(path) +
                   ", readError=" + ex.Message +
                   ". NOTE: comparison remains valid; regenerate the exact-PC diagnostic if this file is locked or corrupt.";
        }

        List<PcEvent> preferredEvents = ParsePreferredEvents(text);
        List<PreferredCall> calls = BuildCalls(preferredEvents);
        List<TapeTuple> tape = BuildTapeTuples(calls, out int unmodeledCalls, out string firstUnmodeled);

        AlignmentResult best = FindBestWindow(standard, tape);

        var builder = new StringBuilder();
        builder.Append("Lady Bug preferred[] exact-PC tape alignment ").Append(Version).Append(": ");
        builder.Append("fileFound=true");
        builder.Append(", importedPath=").Append(ToDisplayPath(path));
        builder.Append(", importedMarkerCount=").Append(markerCount);
        builder.Append(", preferredEvents=").Append(preferredEvents.Count);
        builder.Append(", tapeCalls=").Append(calls.Count);
        builder.Append(", modeledTapeCalls=").Append(tape.Count);
        builder.Append(", unmodeledTapeCalls=").Append(unmodeledCalls);
        builder.Append(", standardFrames=").Append(standardTraceFrames.Count);
        builder.Append(", standardActiveProviderFrames=").Append(standard.Count);
        builder.Append(", standardProviderSkips=").Append(skippedStandard);
        builder.Append(", bestWindowStart=").Append(best.Start);
        builder.Append(", bestWindowTupleMatches=").Append(best.TupleMatches);
        builder.Append(", bestWindowTupleMismatches=").Append(best.TupleMismatches);
        builder.Append(", bestWindowSourceMatches=").Append(best.SourceMatches);
        builder.Append(", bestWindowSourceMismatches=").Append(best.SourceMismatches);

        if (tape.Count >= standard.Count && standard.Count > 0)
        {
            double ratio = best.TupleMatches / Math.Max(1.0, standard.Count);
            builder.Append(", tupleMatchRatio=").Append(ratio.ToString("0.000", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(firstUnmodeled))
            builder.Append(", firstUnmodeled: ").Append(firstUnmodeled);

        if (!string.IsNullOrEmpty(best.FirstMismatch))
            builder.Append(", firstWindowMismatch: ").Append(best.FirstMismatch);
        else if (best.TupleMismatches == 0 && best.Start >= 0)
            builder.Append(", firstWindowMismatch: none");

        builder.Append(". NOTE: diagnostic-only. This is a contiguous tuple-window alignment between the standard active-frame preferred[] provider sequence and the imported exact-PC 2E5C call tape. It proves sequence compatibility only if tuple mismatches are zero; it still does not provide exact per-frame PC timing. Do not use it yet as the authoritative preferred[] provider.");
        return builder.ToString();
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
                Iy = Get(fields, "iy"),
                P0 = Get(fields, "p0"),
                P1 = Get(fields, "p1"),
                P2 = Get(fields, "p2"),
                P3 = Get(fields, "p3")
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
            current.Events.Add(ev);

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
