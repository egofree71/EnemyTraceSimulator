using System;
using System.Collections.Generic;

/// <summary>
/// Diagnostics for the remaining unsimulated EnemyWork.preferred[] generator.
///
/// This does not change the simulation checkpoint. It compares candidate
/// generators against the preferred[] values captured from MAME and summarizes
/// safe polling-diff preferredChangeEvents.
///
/// The purpose is not to guess the final algorithm in this file. It is to expose
/// enough evidence to implement the real arcade generator later, likely starting
/// around the 0x2E5C path.
/// </summary>
public static class LadyBugPreferredGeneratorDiagnostics
{
    private const int MaxOfficialOneEnemyTick = 800;

    public static IReadOnlyList<string> BuildReport(IReadOnlyList<EnemyTraceFrame> frames)
    {
        var lines = new List<string>();

        if (frames.Count == 0)
        {
            lines.Add("preferred[] diagnostics: no trace loaded.");
            return lines;
        }

        List<PreferredSample> samples = CollectSamples(frames);
        lines.Add($"preferred[] diagnostics: samples={samples.Count}, frameLimitTick={MaxOfficialOneEnemyTick}, activeEnemies=1 only");

        if (samples.Count == 0)
        {
            lines.Add("preferred[] diagnostics: no usable one-enemy EnemyWork.preferred[] samples found.");
            return lines;
        }

        List<PreferredCandidateScore> scores = ScoreDeterministicCandidates(samples);
        scores.Sort((a, b) =>
        {
            int exactCompare = b.ExactFrameMatches.CompareTo(a.ExactFrameMatches);
            if (exactCompare != 0)
                return exactCompare;

            int slotCompare = b.SlotMatches.CompareTo(a.SlotMatches);
            if (slotCompare != 0)
                return slotCompare;

            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        int totalSlots = samples.Count * 4;
        lines.Add("preferred[] diagnostics: top deterministic candidates");

        int count = Math.Min(10, scores.Count);
        for (int i = 0; i < count; i++)
        {
            PreferredCandidateScore score = scores[i];
            lines.Add(
                $"  #{i + 1}: {score.Name}: exactFrames={score.ExactFrameMatches}/{samples.Count}, " +
                $"slotMatches={score.SlotMatches}/{totalSlots}, firstMismatch={score.FirstMismatch}");
        }

        lines.Add("preferred[] diagnostics: first reference samples");
        int sampleCount = Math.Min(12, samples.Count);
        for (int i = 0; i < sampleCount; i++)
        {
            PreferredSample sample = samples[i];
            lines.Add(
                $"  tick={sample.Tick} r={sample.RText ?? "--"} tempDir={sample.TempDir:X2} " +
                $"preferred=[{FormatDirections(sample.Expected)}]");
        }

        AppendInternalRBruteForceReport(lines, samples);
        AppendDirectionDependentDeltaReport(lines, samples);
        AppendPreferredChangeEventReport(lines, frames);

        return lines;
    }

    /// <summary>
    /// First 0x2E5C random-branch diagnostic.
    ///
    /// The branch at 0x2E9E reads R once per preferred[] slot. The trace R value is
    /// sampled at frame boundary, not at the exact LD A,R. This report ignores the
    /// boundary R and asks a narrower question: can a constant low-nibble stride
    /// between those four internal R reads generate the observed preferred[] tuple?
    /// </summary>
    private static void AppendInternalRBruteForceReport(List<string> lines, List<PreferredSample> samples)
    {
        var scores = new List<InternalRStrideScore>();

        for (int stride = 0; stride < 16; stride++)
        {
            int compatible = 0;
            int totalStartChoices = 0;
            string firstIncompatible = "none";

            foreach (PreferredSample sample in samples)
            {
                List<int> starts = FindInternalRStartsForConstantStride(sample.Expected, stride);

                if (starts.Count > 0)
                {
                    compatible++;
                    totalStartChoices += starts.Count;
                }
                else if (firstIncompatible == "none")
                {
                    firstIncompatible =
                        $"tick={sample.Tick} boundaryR={sample.RText ?? "--"} expected=[{FormatDirections(sample.Expected)}]";
                }
            }

            scores.Add(new InternalRStrideScore
            {
                Stride = stride,
                CompatibleFrames = compatible,
                TotalStartChoices = totalStartChoices,
                FirstIncompatible = firstIncompatible
            });
        }

        scores.Sort((a, b) =>
        {
            int compatibleCompare = b.CompatibleFrames.CompareTo(a.CompatibleFrames);
            if (compatibleCompare != 0)
                return compatibleCompare;

            int choiceCompare = a.TotalStartChoices.CompareTo(b.TotalStartChoices);
            if (choiceCompare != 0)
                return choiceCompare;

            return a.Stride.CompareTo(b.Stride);
        });

        lines.Add("preferred[] diagnostics: 0x2E5C random path, internal R brute force");
        lines.Add("  model: ignore frame-boundary R; brute-force internal R low-nibble start, with a constant low-nibble stride per slot");

        int count = Math.Min(8, scores.Count);
        for (int i = 0; i < count; i++)
        {
            InternalRStrideScore score = scores[i];
            lines.Add(
                $"  #{i + 1}: internalR stride={score.Stride:X1}: compatibleFrames={score.CompatibleFrames}/{samples.Count}, " +
                $"totalStartChoices={score.TotalStartChoices}, firstIncompatible={score.FirstIncompatible}");
        }

        if (scores.Count == 0)
            return;

        int bestStride = scores[0].Stride;
        lines.Add($"preferred[] diagnostics: first samples under best internalR stride={bestStride:X1}");

        int sampleCount = Math.Min(12, samples.Count);
        for (int i = 0; i < sampleCount; i++)
        {
            PreferredSample sample = samples[i];
            List<int> starts = FindInternalRStartsForConstantStride(sample.Expected, bestStride);
            lines.Add(
                $"  tick={sample.Tick} boundaryR={sample.RText ?? "--"} expected=[{FormatDirections(sample.Expected)}] " +
                $"internalRStart={FormatNibbleListOrNone(starts)}");
        }
    }

    /// <summary>
    /// Second 0x2E5C random-branch diagnostic.
    ///
    /// Constant stride is too simple because the Z80 path after each LD A,R may
    /// execute a different number of instructions depending on the generated
    /// direction. This test searches a direction-dependent low-nibble delta model:
    ///
    ///     nextR = currentR + delta[directionJustGenerated]
    ///
    /// The internal R start is still allowed to vary per frame, because the Lua
    /// trace does not capture the exact R value at 0x2EA3.
    /// </summary>
    private static void AppendDirectionDependentDeltaReport(List<string> lines, List<PreferredSample> samples)
    {
        Dictionary<int, int> observedPatternCounts = BuildObservedPatternCounts(samples);

        var scores = new List<DirectionDeltaScore>();

        for (int delta01 = 0; delta01 < 16; delta01++)
        for (int delta02 = 0; delta02 < 16; delta02++)
        for (int delta04 = 0; delta04 < 16; delta04++)
        for (int delta08 = 0; delta08 < 16; delta08++)
        {
            int[] deltas = { delta01, delta02, delta04, delta08 };

            int[] generatedKeys = new int[16];
            int generatedCount = 0;

            for (int start = 0; start < 16; start++)
            {
                int key = GenerateDirectionDependentPatternKey(start, deltas);

                if (!Contains(generatedKeys, generatedCount, key))
                    generatedKeys[generatedCount++] = key;
            }

            int compatibleFrames = 0;
            int matchedObservedPatterns = 0;

            for (int i = 0; i < generatedCount; i++)
            {
                if (observedPatternCounts.TryGetValue(generatedKeys[i], out int patternCount))
                {
                    compatibleFrames += patternCount;
                    matchedObservedPatterns++;
                }
            }

            if (compatibleFrames == 0)
                continue;

            string firstIncompatible = "none";
            foreach (PreferredSample sample in samples)
            {
                int expectedKey = DirectionsToKey(sample.Expected);
                if (!Contains(generatedKeys, generatedCount, expectedKey))
                {
                    firstIncompatible =
                        $"tick={sample.Tick} boundaryR={sample.RText ?? "--"} expected=[{FormatDirections(sample.Expected)}]";
                    break;
                }
            }

            scores.Add(new DirectionDeltaScore
            {
                Delta01 = delta01,
                Delta02 = delta02,
                Delta04 = delta04,
                Delta08 = delta08,
                CompatibleFrames = compatibleFrames,
                MatchedObservedPatterns = matchedObservedPatterns,
                GeneratedPatternCount = generatedCount,
                FirstIncompatible = firstIncompatible
            });
        }

        scores.Sort((a, b) =>
        {
            int compatibleCompare = b.CompatibleFrames.CompareTo(a.CompatibleFrames);
            if (compatibleCompare != 0)
                return compatibleCompare;

            int matchedPatternCompare = b.MatchedObservedPatterns.CompareTo(a.MatchedObservedPatterns);
            if (matchedPatternCompare != 0)
                return matchedPatternCompare;

            int generatedPatternCompare = a.GeneratedPatternCount.CompareTo(b.GeneratedPatternCount);
            if (generatedPatternCompare != 0)
                return generatedPatternCompare;

            return string.Compare(a.FormatDeltas(), b.FormatDeltas(), StringComparison.Ordinal);
        });

        lines.Add("preferred[] diagnostics: 0x2E5C random path, direction-dependent R delta search");
        lines.Add("  model: ignore frame-boundary R; brute-force internal R start plus delta after generated 01/02/04/08");

        int count = Math.Min(10, scores.Count);
        for (int i = 0; i < count; i++)
        {
            DirectionDeltaScore score = scores[i];
            lines.Add(
                $"  #{i + 1}: {score.FormatDeltas()}: compatibleFrames={score.CompatibleFrames}/{samples.Count}, " +
                $"matchedPatterns={score.MatchedObservedPatterns}, generatedPatterns={score.GeneratedPatternCount}, " +
                $"firstIncompatible={score.FirstIncompatible}");
        }

        if (scores.Count == 0)
            return;

        DirectionDeltaScore best = scores[0];
        int[] bestDeltas = { best.Delta01, best.Delta02, best.Delta04, best.Delta08 };

        lines.Add($"preferred[] diagnostics: first samples under best direction-delta model {best.FormatDeltas()}");

        int sampleCount = Math.Min(12, samples.Count);
        for (int i = 0; i < sampleCount; i++)
        {
            PreferredSample sample = samples[i];
            List<int> starts = FindInternalRStartsForDirectionDelta(sample.Expected, bestDeltas);
            lines.Add(
                $"  tick={sample.Tick} boundaryR={sample.RText ?? "--"} expected=[{FormatDirections(sample.Expected)}] " +
                $"internalRStart={FormatNibbleListOrNone(starts)}");
        }
    }

    /// <summary>
    /// Summarizes the frame-to-frame changes captured by the safe Lua polling trace.
    ///
    /// These events are reliable for transition analysis, but not for exact PC/R
    /// attribution because they are sampled at the frame boundary.
    /// </summary>
    private static void AppendPreferredChangeEventReport(List<string> lines, IReadOnlyList<EnemyTraceFrame> frames)
    {
        var frameEvents = new List<PreferredFrameChange>();

        foreach (EnemyTraceFrame frame in frames)
        {
            if (frame.frame > MaxOfficialOneEnemyTick)
                break;

            if (CountActiveEnemies(frame) != 1)
                continue;

            if (frame.preferredChangeEvents == null || frame.preferredChangeEvents.Count == 0)
                continue;

            frameEvents.Add(new PreferredFrameChange(frame));
        }

        lines.Add($"preferred[] change events: framesWithChanges={frameEvents.Count}, activeEnemies=1 only");

        if (frameEvents.Count == 0)
        {
            lines.Add("preferred[] change events: none found. Regenerate the trace with the safe polling diagnostics script.");
            return;
        }

        var transitionCounts = new Dictionary<string, int>();
        var slotCounts = new Dictionary<int, int>();
        var changeCountHistogram = new Dictionary<int, int>();

        foreach (PreferredFrameChange change in frameEvents)
        {
            string key = $"[{FormatDirections(change.Before)}] -> [{FormatDirections(change.After)}]";
            transitionCounts.TryGetValue(key, out int count);
            transitionCounts[key] = count + 1;

            changeCountHistogram.TryGetValue(change.Events.Count, out int histogramCount);
            changeCountHistogram[change.Events.Count] = histogramCount + 1;

            foreach (EnemyTracePreferredChangeEvent e in change.Events)
            {
                slotCounts.TryGetValue(e.slot, out int slotCount);
                slotCounts[e.slot] = slotCount + 1;
            }
        }

        lines.Add("preferred[] change events: first transitions");
        int firstCount = Math.Min(16, frameEvents.Count);
        for (int i = 0; i < firstCount; i++)
        {
            PreferredFrameChange change = frameEvents[i];
            lines.Add(
                $"  tick={change.Tick} r={change.RText ?? "--"} changes={change.Events.Count} " +
                $"[{FormatDirections(change.Before)}] -> [{FormatDirections(change.After)}] " +
                $"slots={FormatChangedSlots(change.Events)}");
        }

        lines.Add("preferred[] change events: top transitions");
        foreach (KeyValuePair<string, int> pair in TopCounts(transitionCounts, 12))
            lines.Add($"  {pair.Value}x {pair.Key}");

        lines.Add("preferred[] change events: change-count histogram");
        for (int i = 1; i <= 4; i++)
        {
            changeCountHistogram.TryGetValue(i, out int histogramCount);
            lines.Add($"  {i} changed slot(s): {histogramCount}");
        }

        lines.Add("  note: polling only records value changes; a slot may have been written with the same value and therefore not appear here.");

        lines.Add("preferred[] change events: first partial transitions");
        int partialCount = 0;
        foreach (PreferredFrameChange change in frameEvents)
        {
            if (change.Events.Count >= 4)
                continue;

            lines.Add(
                $"  tick={change.Tick} r={change.RText ?? "--"} changes={change.Events.Count} " +
                $"[{FormatDirections(change.Before)}] -> [{FormatDirections(change.After)}] " +
                $"slots={FormatChangedSlots(change.Events)}");

            partialCount++;
            if (partialCount >= 12)
                break;
        }

        if (partialCount == 0)
            lines.Add("  none in the current one-enemy window.");

        lines.Add("preferred[] change events: slot write counts");
        for (int i = 0; i < 4; i++)
        {
            slotCounts.TryGetValue(i, out int count);
            lines.Add($"  slot {i} / 0x{0x61C4 + i:X4}: {count}");
        }
    }

    private static List<PreferredSample> CollectSamples(IReadOnlyList<EnemyTraceFrame> frames)
    {
        var samples = new List<PreferredSample>();

        foreach (EnemyTraceFrame frame in frames)
        {
            if (frame.frame > MaxOfficialOneEnemyTick)
                break;

            if (frame.enemyWork == null || frame.enemyWork.preferred.Count < 4)
                continue;

            if (CountActiveEnemies(frame) != 1)
                continue;

            samples.Add(new PreferredSample
            {
                Tick = frame.frame,
                FrameIndex = samples.Count,
                RText = frame.r,
                R = ParseTraceByteOrZero(frame.r),
                TempDir = frame.enemyWork.tempDir,
                Expected =
                {
                    frame.enemyWork.preferred[0],
                    frame.enemyWork.preferred[1],
                    frame.enemyWork.preferred[2],
                    frame.enemyWork.preferred[3]
                }
            });
        }

        return samples;
    }

    private static List<PreferredCandidateScore> ScoreDeterministicCandidates(List<PreferredSample> samples)
    {
        var scores = new List<PreferredCandidateScore>();

        AddCandidate(scores, samples, "fill tempDir", sample =>
            new[] { sample.TempDir, sample.TempDir, sample.TempDir, sample.TempDir });

        AddCandidate(scores, samples, "rotate-right from tempDir", sample =>
            GenerateRotateRightSequence(sample.TempDir));

        AddCandidate(scores, samples, "rotate-left from tempDir", sample =>
            GenerateRotateLeftSequence(sample.TempDir));

        // Diagnostic hypothesis only:
        // The random branch at 0x2E9E uses LD A,R, AND 0x0F, SRL, INC, then
        // maps ranges to 01/02/04/08. We only have one sampled R value per trace
        // frame, not the internal R values at each LD A,R. Try a small grid of
        // offsets/strides to see whether the captured R is still predictive.
        for (int offset = 0; offset < 16; offset++)
        {
            for (int stride = 0; stride < 8; stride++)
            {
                int capturedOffset = offset;
                int capturedStride = stride;
                AddCandidate(
                    scores,
                    samples,
                    $"boundary R-map offset={capturedOffset:X1} stride={capturedStride}",
                    sample => GenerateRMapSequence(sample.R, capturedOffset, capturedStride));
            }
        }

        return scores;
    }

    private static void AddCandidate(
        List<PreferredCandidateScore> scores,
        List<PreferredSample> samples,
        string name,
        Func<PreferredSample, int[]> generator)
    {
        int exact = 0;
        int slotMatches = 0;
        string firstMismatch = "none";

        foreach (PreferredSample sample in samples)
        {
            int[] actual = generator(sample);
            bool frameExact = true;

            for (int i = 0; i < 4; i++)
            {
                int actualValue = i < actual.Length ? actual[i] : -1;
                if (actualValue == sample.Expected[i])
                {
                    slotMatches++;
                }
                else
                {
                    frameExact = false;
                }
            }

            if (frameExact)
            {
                exact++;
            }
            else if (firstMismatch == "none")
            {
                firstMismatch =
                    $"tick={sample.Tick} expected=[{FormatDirections(sample.Expected)}] actual=[{FormatDirections(actual)}]";
            }
        }

        scores.Add(new PreferredCandidateScore
        {
            Name = name,
            ExactFrameMatches = exact,
            SlotMatches = slotMatches,
            FirstMismatch = firstMismatch
        });
    }

    private static int[] GenerateRotateRightSequence(int sourceDirection)
    {
        int[] result = new int[4];
        int direction = sourceDirection;

        for (int i = 0; i < 4; i++)
        {
            direction = RotateRight4(direction);
            result[i] = direction;
        }

        return result;
    }

    private static int[] GenerateRotateLeftSequence(int sourceDirection)
    {
        int[] result = new int[4];
        int direction = sourceDirection;

        for (int i = 0; i < 4; i++)
        {
            direction = RotateLeft4(direction);
            result[i] = direction;
        }

        return result;
    }

    private static int[] GenerateRMapSequence(int capturedR, int offset, int stride)
    {
        int[] result = new int[4];

        for (int i = 0; i < 4; i++)
        {
            int r = (capturedR + offset + stride * i) & 0x0F;
            result[i] = DirectionFromRandomNibble(r);
        }

        return result;
    }

    private static List<int> FindInternalRStartsForConstantStride(IReadOnlyList<int> expected, int stride)
    {
        var starts = new List<int>();

        for (int start = 0; start < 16; start++)
        {
            bool matches = true;

            for (int slot = 0; slot < 4; slot++)
            {
                int r = (start + stride * slot) & 0x0F;
                int direction = DirectionFromRandomNibble(r);

                if (direction != expected[slot])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                starts.Add(start);
        }

        return starts;
    }

    private static List<int> FindInternalRStartsForDirectionDelta(IReadOnlyList<int> expected, int[] deltas)
    {
        var starts = new List<int>();

        for (int start = 0; start < 16; start++)
        {
            int r = start;
            bool matches = true;

            for (int slot = 0; slot < 4; slot++)
            {
                int direction = DirectionFromRandomNibble(r);

                if (direction != expected[slot])
                {
                    matches = false;
                    break;
                }

                int deltaIndex = DirectionToDeltaIndex(direction);
                r = (r + deltas[deltaIndex]) & 0x0F;
            }

            if (matches)
                starts.Add(start);
        }

        return starts;
    }

    private static int GenerateDirectionDependentPatternKey(int start, int[] deltas)
    {
        int r = start;
        int key = 0;

        for (int slot = 0; slot < 4; slot++)
        {
            int direction = DirectionFromRandomNibble(r);
            key = (key << 4) | (direction & 0x0F);

            int deltaIndex = DirectionToDeltaIndex(direction);
            r = (r + deltas[deltaIndex]) & 0x0F;
        }

        return key;
    }

    private static Dictionary<int, int> BuildObservedPatternCounts(List<PreferredSample> samples)
    {
        var counts = new Dictionary<int, int>();

        foreach (PreferredSample sample in samples)
        {
            int key = DirectionsToKey(sample.Expected);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        return counts;
    }

    private static int DirectionsToKey(IReadOnlyList<int> values)
    {
        int key = 0;

        for (int i = 0; i < 4; i++)
            key = (key << 4) | (values[i] & 0x0F);

        return key;
    }

    private static bool Contains(int[] values, int count, int target)
    {
        for (int i = 0; i < count; i++)
        {
            if (values[i] == target)
                return true;
        }

        return false;
    }

    private static int DirectionToDeltaIndex(int direction)
    {
        return direction switch
        {
            0x01 => 0,
            0x02 => 1,
            0x04 => 2,
            0x08 => 3,
            _ => 0
        };
    }

    private static int DirectionFromRandomNibble(int rLowNibble)
    {
        int value = ((rLowNibble & 0x0F) >> 1) + 1;

        if (value < 3)
            return 0x01;

        if (value < 5)
            return 0x02;

        if (value < 7)
            return 0x04;

        return 0x08;
    }

    private static int RotateRight4(int direction)
    {
        int shifted = (direction >> 1) & 0x0F;

        if ((direction & 0x01) != 0)
            shifted |= 0x08;

        return shifted & 0x0F;
    }

    private static int RotateLeft4(int direction)
    {
        int shifted = (direction << 1) & 0x0F;

        if ((direction & 0x08) != 0)
            shifted |= 0x01;

        return shifted & 0x0F;
    }

    private static int CountActiveEnemies(EnemyTraceFrame frame)
    {
        if (frame.enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (enemy.active)
                count++;
        }

        return count;
    }

    private static int ParseTraceByteOrZero(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        string trimmed = text.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexValue)
                ? hexValue & 0xFF
                : 0;
        }

        bool looksHex = trimmed.Length <= 2;
        foreach (char c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
        }

        if (looksHex &&
            int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out int compactHexValue))
        {
            return compactHexValue & 0xFF;
        }

        return int.TryParse(trimmed, out int decimalValue)
            ? decimalValue & 0xFF
            : 0;
    }

    private static string FormatDirections(IReadOnlyList<int> values)
    {
        var parts = new List<string>(values.Count);

        foreach (int value in values)
            parts.Add(value < 0 ? "--" : value.ToString("X2"));

        return string.Join(",", parts);
    }

    private static string FormatNibbleListOrNone(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return "none";

        var parts = new List<string>(values.Count);
        foreach (int value in values)
            parts.Add(value.ToString("X1"));

        return string.Join(",", parts);
    }

    private static string FormatChangedSlots(IReadOnlyList<EnemyTracePreferredChangeEvent> events)
    {
        var parts = new List<string>(events.Count);

        foreach (EnemyTracePreferredChangeEvent e in events)
            parts.Add($"{e.slot}:{e.old:X2}->{e.@new:X2}");

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

    private sealed class PreferredFrameChange
    {
        public PreferredFrameChange(EnemyTraceFrame frame)
        {
            Tick = frame.frame;
            RText = frame.r;
            Events = frame.preferredChangeEvents ?? new List<EnemyTracePreferredChangeEvent>();

            EnemyTracePreferredChangeEvent first = Events[0];

            Before.AddRange(first.preferredBefore);
            After.AddRange(first.preferredAfter);

            if (Before.Count == 0 && frame.enemyWork != null)
                Before.AddRange(frame.enemyWork.preferred);

            if (After.Count == 0 && frame.enemyWork != null)
                After.AddRange(frame.enemyWork.preferred);
        }

        public int Tick { get; }
        public string? RText { get; }
        public IReadOnlyList<EnemyTracePreferredChangeEvent> Events { get; }
        public List<int> Before { get; } = new();
        public List<int> After { get; } = new();
    }

    private sealed class PreferredSample
    {
        public int Tick { get; init; }
        public int FrameIndex { get; init; }
        public string? RText { get; init; }
        public int R { get; init; }
        public int TempDir { get; init; }
        public List<int> Expected { get; } = new();
    }

    private sealed class PreferredCandidateScore
    {
        public string Name { get; init; } = string.Empty;
        public int ExactFrameMatches { get; init; }
        public int SlotMatches { get; init; }
        public string FirstMismatch { get; init; } = string.Empty;
    }

    private sealed class InternalRStrideScore
    {
        public int Stride { get; init; }
        public int CompatibleFrames { get; init; }
        public int TotalStartChoices { get; init; }
        public string FirstIncompatible { get; init; } = string.Empty;
    }

    private sealed class DirectionDeltaScore
    {
        public int Delta01 { get; init; }
        public int Delta02 { get; init; }
        public int Delta04 { get; init; }
        public int Delta08 { get; init; }
        public int CompatibleFrames { get; init; }
        public int MatchedObservedPatterns { get; init; }
        public int GeneratedPatternCount { get; init; }
        public string FirstIncompatible { get; init; } = string.Empty;

        public string FormatDeltas()
        {
            return $"delta01={Delta01:X1}, delta02={Delta02:X1}, delta04={Delta04:X1}, delta08={Delta08:X1}";
        }
    }
}
