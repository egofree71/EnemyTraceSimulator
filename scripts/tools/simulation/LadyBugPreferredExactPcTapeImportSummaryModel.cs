using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// v0.7.11 importer for the preferred[] exact-PC tape captured by
/// tools/mame/lua/ladybug_enemywork_pc_trace.lua.
///
/// This deliberately does not require enabling the MAME debugger for the standard
/// JSONL trace. The stable workflow is now:
///
///   1. Generate the normal JSONL trace with ladybug_sequence_trace.lua, debugger off.
///   2. Generate the exact-PC diagnostic with ladybug_enemywork_pc_trace.lua.
///   3. Load the normal JSONL trace and run Compare.
///
/// This model searches for the newest usable exact-PC raw companion file.
/// The preferred source is tools/mame/lua/error.log because MAME -log captures
/// debugger logerror output there even when the Lua-side *_hits.log drain is only
/// a short header. It also falls back to *_enemywork_pc_hits.log if usable.
/// It imports LBEW preferred-generator events, validates the same source-first model
/// as v0.7.06, and reports whether a usable external random/BFS tape is available.
/// </summary>
public static class LadyBugPreferredExactPcTapeImportSummaryModel
{
    private const string Version = "v0.7.11b";
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

    private sealed class Counters
    {
        public string ImportedPath = string.Empty;
        public bool FileFound;
        public int StandardFrames;
        public int StandardActiveFrames;
        public int StandardProviderChecks;
        public int StandardProviderSkips;
        public int StandardProviderUnclassified;
        public int ImportedLines;
        public int ImportedMarkerCount;
        public int ImportedEvents;
        public int PreferredEvents;
        public int Entries2E5C;
        public int RotateBranch2E8C;
        public int RotateWrites2E97;
        public int RandomBranch2E9E;
        public int RandomRValues2EA5;
        public int RandomWrites2EC7;
        public int Calls2ECB;
        public int BfsEntries46D8;
        public int BfsWrites477D;
        public int Calls;
        public int ModeledCalls;
        public int BaseWriteChecks;
        public int BaseWriteMatches;
        public int BaseWriteMismatches;
        public int RandomPairChecks;
        public int RandomPairMatches;
        public int RandomPairMismatches;
        public int BfsOverrideChecks;
        public int BfsOverrideValidTargets;
        public int BfsOverrideInvalidTargets;
        public string FirstTapeEvent = string.Empty;
        public string FirstMismatch = string.Empty;
        public readonly Dictionary<string, int> Sources = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> StandardProviderSources = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> RandomRLowNibbles = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> BfsTargets = new(StringComparer.Ordinal);
    }

    private sealed class PcEvent
    {
        public string Source = string.Empty;
        public string A = string.Empty;
        public string Iy = string.Empty;
        public string PlayerDir = string.Empty;
        public string P0 = string.Empty;
        public string P1 = string.Empty;
        public string P2 = string.Empty;
        public string P3 = string.Empty;
        public string RawLine = string.Empty;
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
        public readonly List<PcEvent> RotateWrites = new();
        public readonly List<PcEvent> RandomRValues = new();
        public readonly List<PcEvent> RandomWrites = new();
        public readonly List<PcEvent> BfsWrites = new();
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> standardTraceFrames)
    {
        var counters = new Counters
        {
            StandardFrames = standardTraceFrames.Count
        };

        CountStandardProviderSources(standardTraceFrames, counters);

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
            return "Lady Bug preferred[] exact-PC tape import " + Version +
                   ": fileFound=true, importedPath=" + path +
                   ", readError=" + ex.Message +
                   ". NOTE: the standard JSONL trace remains valid; regenerate the exact-PC diagnostic if this file is locked or corrupt.";
        }

        List<PcEvent> events = ParseEvents(text, counters);
        AnalyzeImportedTape(events, counters);
        return BuildSummaryText(counters);
    }

    private static void CountStandardProviderSources(IReadOnlyList<EnemyTraceFrame> frames, Counters counters)
    {
        foreach (EnemyTraceFrame frame in frames)
        {
            if (HasAnyActiveEnemy(frame))
                counters.StandardActiveFrames++;

            if (!LadyBugEnemyPreferredGeneratorReplayShadowModel.TryBuildPreferredTupleForActiveFrame(
                    frame,
                    out _,
                    out string source,
                    out string skipReason))
            {
                if (skipReason == "unclassified")
                    counters.StandardProviderUnclassified++;
                else
                    counters.StandardProviderSkips++;

                continue;
            }

            counters.StandardProviderChecks++;
            Count(counters.StandardProviderSources, source);
        }
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

    private static bool TryFindExactPcRawLog(out string path, out int markerCount)
    {
        path = string.Empty;
        markerCount = 0;

        var candidates = new List<string>();

        // MAME -log writes debugger logerror output here. This is the reliable
        // source observed by the launcher/analyzer; the Lua-drained *_hits.log can
        // legitimately contain only header lines if the diagnostic is watchdog-killed.
        AddExplicitCandidate(candidates, "res://tools/mame/lua/error.log");

        // Keep the previous companion-file search as fallback. Some MAME/Lua runs
        // do manage to drain debugger.errorlog into this file before exit.
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
            // Best effort. A missing companion tape should not break comparison.
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

    private static string ToGlobalPath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return Godot.ProjectSettings.GlobalizePath(path);
        }

        return path;
    }

    private static List<PcEvent> ParseEvents(string text, Counters counters)
    {
        var events = new List<PcEvent>();

        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        counters.ImportedLines = lines.Length;

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

            if (!fields.TryGetValue("source", out string? source) || string.IsNullOrWhiteSpace(source))
                continue;

            var ev = new PcEvent
            {
                Source = source.Trim(),
                A = Get(fields, "a"),
                Iy = Get(fields, "iy"),
                PlayerDir = Get(fields, "playerDir"),
                P0 = Get(fields, "p0"),
                P1 = Get(fields, "p1"),
                P2 = Get(fields, "p2"),
                P3 = Get(fields, "p3"),
                RawLine = line.Trim()
            };

            events.Add(ev);
            counters.ImportedEvents++;
            Count(counters.Sources, ev.Source);
        }

        return events;
    }

    private static string Get(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out string? value) ? value.Trim() : string.Empty;
    }

    private static void AnalyzeImportedTape(List<PcEvent> events, Counters counters)
    {
        List<PcEvent> preferredEvents = events
            .Where(IsPreferredSource)
            .ToList();

        counters.PreferredEvents = preferredEvents.Count;
        counters.Entries2E5C = CountSource(preferredEvents, "2E5C_PREF_ENTRY");
        counters.RotateBranch2E8C = CountSource(preferredEvents, "2E8C_PREF_ROTATE_BRANCH");
        counters.RotateWrites2E97 = CountSource(preferredEvents, "2E97_PREF_ROTATE_WRITE");
        counters.RandomBranch2E9E = CountSource(preferredEvents, "2E9E_PREF_RANDOM_BRANCH");
        counters.RandomRValues2EA5 = CountSource(preferredEvents, "2EA5_PREF_RANDOM_R_VALUE");
        counters.RandomWrites2EC7 = CountSource(preferredEvents, "2EC7_PREF_RANDOM_WRITE");
        counters.Calls2ECB = CountSource(preferredEvents, "2ECB_PREF_CALL_BFS_OVERRIDE");
        counters.BfsEntries46D8 = CountSource(preferredEvents, "46D8_BFS_OVERRIDE_ENTRY");
        counters.BfsWrites477D = CountSource(preferredEvents, "477D_BFS_OVERRIDE_WRITE");

        foreach (PcEvent ev in preferredEvents)
        {
            if (string.IsNullOrEmpty(counters.FirstTapeEvent))
                counters.FirstTapeEvent = FormatShort(ev);

            if (ev.Source.Equals("2EA5_PREF_RANDOM_R_VALUE", StringComparison.OrdinalIgnoreCase) &&
                TryParseHexByte(ev.A, out int rValue))
            {
                Count(counters.RandomRLowNibbles, (rValue & 0x0F).ToString("X1", CultureInfo.InvariantCulture));
            }

            if (ev.Source.Equals("477D_BFS_OVERRIDE_WRITE", StringComparison.OrdinalIgnoreCase))
                Count(counters.BfsTargets, "IY=" + ev.Iy + ":A=" + ev.A);
        }

        List<PreferredCall> calls = BuildCalls(preferredEvents);
        counters.Calls = calls.Count;

        foreach (PreferredCall call in calls)
            ValidateCall(call, counters);
    }

    private static bool IsPreferredSource(PcEvent ev)
    {
        foreach (string source in PreferredSources)
        {
            if (ev.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
            else if (ev.Source.Equals("2E97_PREF_ROTATE_WRITE", StringComparison.OrdinalIgnoreCase))
                current.RotateWrites.Add(ev);
            else if (ev.Source.Equals("2E9E_PREF_RANDOM_BRANCH", StringComparison.OrdinalIgnoreCase))
                current.RandomBranch = ev;
            else if (ev.Source.Equals("2EA5_PREF_RANDOM_R_VALUE", StringComparison.OrdinalIgnoreCase))
                current.RandomRValues.Add(ev);
            else if (ev.Source.Equals("2EC7_PREF_RANDOM_WRITE", StringComparison.OrdinalIgnoreCase))
                current.RandomWrites.Add(ev);
            else if (ev.Source.Equals("477D_BFS_OVERRIDE_WRITE", StringComparison.OrdinalIgnoreCase))
                current.BfsWrites.Add(ev);
        }

        if (current != null)
            calls.Add(current);

        return calls;
    }

    private static void ValidateCall(PreferredCall call, Counters counters)
    {
        if (!TryModelBaseTuple(call, out int[] candidate, out string error))
        {
            if (string.IsNullOrEmpty(counters.FirstMismatch))
                counters.FirstMismatch = "call=" + call.Index + " cannot model base tuple: " + error;
            return;
        }

        counters.ModeledCalls++;
        List<PcEvent> baseWrites = call.RotateBranch != null ? call.RotateWrites : call.RandomWrites;
        for (int i = 0; i < baseWrites.Count && i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
        {
            counters.BaseWriteChecks++;
            int expected = candidate[i] & 0x0F;

            if (TryParseHexByte(baseWrites[i].A, out int actual) && (actual & 0x0F) == expected)
            {
                counters.BaseWriteMatches++;
            }
            else
            {
                counters.BaseWriteMismatches++;
                if (string.IsNullOrEmpty(counters.FirstMismatch))
                    counters.FirstMismatch = "call=" + call.Index + " baseWrite slot=" + i + " expected=" + FormatByte(expected) + " event=" + FormatShort(baseWrites[i]);
            }
        }

        if (call.RandomBranch != null)
        {
            int pairs = Math.Min(call.RandomRValues.Count, call.RandomWrites.Count);
            for (int i = 0; i < pairs; i++)
            {
                counters.RandomPairChecks++;
                if (TryParseHexByte(call.RandomRValues[i].A, out int rValue) &&
                    TryParseHexByte(call.RandomWrites[i].A, out int writtenDirection) &&
                    (LadyBugMonsterPreferenceSystem.DirectionFromRandomNibble(rValue) & 0x0F) == (writtenDirection & 0x0F))
                {
                    counters.RandomPairMatches++;
                }
                else
                {
                    counters.RandomPairMismatches++;
                    if (string.IsNullOrEmpty(counters.FirstMismatch))
                        counters.FirstMismatch = "call=" + call.Index + " random pair slot=" + i + " rEvent=" + FormatShort(call.RandomRValues[i]) + " writeEvent=" + FormatShort(call.RandomWrites[i]);
                }
            }
        }

        foreach (PcEvent bfsWrite in call.BfsWrites)
        {
            counters.BfsOverrideChecks++;
            int[] clone = (int[])candidate.Clone();
            if (TryParseHexWord(bfsWrite.Iy, out int iyAddress) &&
                TryParseHexByte(bfsWrite.A, out int direction) &&
                LadyBugMonsterPreferenceSystem.TryApplyBfsOverride(clone, iyAddress, direction))
            {
                counters.BfsOverrideValidTargets++;
            }
            else
            {
                counters.BfsOverrideInvalidTargets++;
                if (string.IsNullOrEmpty(counters.FirstMismatch))
                    counters.FirstMismatch = "call=" + call.Index + " invalid BFS target event=" + FormatShort(bfsWrite);
            }
        }
    }

    private static bool TryModelBaseTuple(PreferredCall call, out int[] candidate, out string error)
    {
        candidate = new[] { 0, 0, 0, 0 };
        error = string.Empty;

        if (call.RotateBranch != null)
        {
            if (!TryParseHexByte(call.RotateBranch.A, out int seed))
            {
                error = "bad rotate seed";
                return false;
            }

            candidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(seed);
            return true;
        }

        if (call.RandomBranch != null)
        {
            if (call.RandomRValues.Count < LadyBugMonsterPreferenceSystem.PreferredSlotCount)
            {
                error = "random branch has only " + call.RandomRValues.Count + " LD A,R values";
                return false;
            }

            for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
            {
                if (!TryParseHexByte(call.RandomRValues[i].A, out int rValue))
                {
                    error = "bad random R value";
                    return false;
                }

                candidate[i] = LadyBugMonsterPreferenceSystem.DirectionFromRandomNibble(rValue);
            }

            return true;
        }

        error = "missing branch marker";
        return false;
    }

    private static string BuildMissingFileSummary(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug preferred[] exact-PC tape import ").Append(Version).Append(": ");
        builder.Append("fileFound=false");
        builder.Append(", standardFrames=").Append(counters.StandardFrames);
        builder.Append(", standardActiveFrames=").Append(counters.StandardActiveFrames);
        builder.Append(", standardProviderChecks=").Append(counters.StandardProviderChecks);
        builder.Append(", standardProviderSources=").Append(DescribeDictionary(counters.StandardProviderSources));
        builder.Append(". NOTE: no exact-PC raw companion file was found. Generate the exact-PC diagnostic separately with Lua script res://tools/mame/lua/ladybug_enemywork_pc_trace.lua and prefix ladybug_sequence_v8_enemywork_pcdiag, then reload the normal JSONL trace and run Compare again. The importer first looks at res://tools/mame/lua/error.log, then falls back to *_enemywork_pc_hits.log.");
        return builder.ToString();
    }

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug preferred[] exact-PC tape import ").Append(Version).Append(": ");
        builder.Append("fileFound=").Append(counters.FileFound ? "true" : "false");
        builder.Append(", importedPath=").Append(ToDisplayPath(counters.ImportedPath));
        builder.Append(", standardFrames=").Append(counters.StandardFrames);
        builder.Append(", standardActiveFrames=").Append(counters.StandardActiveFrames);
        builder.Append(", standardProviderChecks=").Append(counters.StandardProviderChecks);
        builder.Append(", standardProviderSkips=").Append(counters.StandardProviderSkips);
        builder.Append(", standardProviderUnclassified=").Append(counters.StandardProviderUnclassified);
        builder.Append(", importedLines=").Append(counters.ImportedLines);
        builder.Append(", importedMarkerCount=").Append(counters.ImportedMarkerCount);
        builder.Append(", importedEvents=").Append(counters.ImportedEvents);
        builder.Append(", preferredEvents=").Append(counters.PreferredEvents);
        builder.Append(", entries2E5C=").Append(counters.Entries2E5C);
        builder.Append(", rotateBranch2E8C=").Append(counters.RotateBranch2E8C);
        builder.Append(", rotateWrites2E97=").Append(counters.RotateWrites2E97);
        builder.Append(", randomBranch2E9E=").Append(counters.RandomBranch2E9E);
        builder.Append(", randomRValues2EA5=").Append(counters.RandomRValues2EA5);
        builder.Append(", randomWrites2EC7=").Append(counters.RandomWrites2EC7);
        builder.Append(", calls2ECB=").Append(counters.Calls2ECB);
        builder.Append(", bfsEntries46D8=").Append(counters.BfsEntries46D8);
        builder.Append(", bfsWrites477D=").Append(counters.BfsWrites477D);
        builder.Append(", calls=").Append(counters.Calls);
        builder.Append(", modeledCalls=").Append(counters.ModeledCalls);
        builder.Append(", baseWriteChecks=").Append(counters.BaseWriteChecks);
        builder.Append(", baseWriteMatches=").Append(counters.BaseWriteMatches);
        builder.Append(", baseWriteMismatches=").Append(counters.BaseWriteMismatches);
        builder.Append(", randomPairChecks=").Append(counters.RandomPairChecks);
        builder.Append(", randomPairMatches=").Append(counters.RandomPairMatches);
        builder.Append(", randomPairMismatches=").Append(counters.RandomPairMismatches);
        builder.Append(", bfsOverrideChecks=").Append(counters.BfsOverrideChecks);
        builder.Append(", bfsOverrideValidTargets=").Append(counters.BfsOverrideValidTargets);
        builder.Append(", bfsOverrideInvalidTargets=").Append(counters.BfsOverrideInvalidTargets);
        builder.Append(", randomRLowNibbles=").Append(DescribeDictionary(counters.RandomRLowNibbles));
        builder.Append(", bfsTargets=").Append(DescribeDictionary(counters.BfsTargets));
        builder.Append(", standardProviderSources=").Append(DescribeDictionary(counters.StandardProviderSources));

        if (!string.IsNullOrEmpty(counters.FirstTapeEvent))
            builder.Append(", firstTapeEvent: ").Append(counters.FirstTapeEvent);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);
        else
            builder.Append(", firstMismatch: none");

        builder.Append(". NOTE: import-only bridge. The normal JSONL trace must remain debugger-free. This summary imports the separate exact-PC tape, preferring tools/mame/lua/error.log when the Lua-drained hits file has only headers, and validates the generator model. It does not yet time-align each exact-PC call back into the standard replay timeline.");
        return builder.ToString();
    }

    private static int CountSource(IEnumerable<PcEvent> events, string source)
    {
        return events.Count(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
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

    private static string FormatShort(PcEvent ev)
    {
        return "source=" + ev.Source + ":A=" + ev.A + ":IY=" + ev.Iy + ":pref=[" + ev.P0 + "," + ev.P1 + "," + ev.P2 + "," + ev.P3 + "]";
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static bool TryParseHexByte(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHexWord(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
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
