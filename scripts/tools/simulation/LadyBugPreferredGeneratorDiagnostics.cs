using System;
using System.Collections.Generic;

/// <summary>
/// Diagnostics for the remaining unsimulated EnemyWork.preferred[] generator.
///
/// This does not change the simulation checkpoint. It compares simple candidate
/// generators against the preferred[] values captured from MAME so the future
/// implementation of the arcade 0x2E5C path can be guided by evidence instead
/// of hard-coded trace fixes.
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

        List<PreferredCandidateScore> scores = ScoreCandidates(samples);
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
        lines.Add("preferred[] diagnostics: top candidates");

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

        return lines;
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

    private static List<PreferredCandidateScore> ScoreCandidates(List<PreferredSample> samples)
    {
        var scores = new List<PreferredCandidateScore>();

        AddCandidate(scores, samples, "fill tempDir", sample =>
            new[] { sample.TempDir, sample.TempDir, sample.TempDir, sample.TempDir });

        AddCandidate(scores, samples, "rotate-right from tempDir", sample =>
            GenerateRotateRightSequence(sample.TempDir));

        AddCandidate(scores, samples, "rotate-left from tempDir", sample =>
            GenerateRotateLeftSequence(sample.TempDir));

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
                    $"R-map offset={capturedOffset:X1} stride={capturedStride}",
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
}
