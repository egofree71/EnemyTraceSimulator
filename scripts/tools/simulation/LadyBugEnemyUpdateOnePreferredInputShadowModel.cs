using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// v0.7.08 bridge diagnostic between the validated preferred[] generator model
/// and the source-first Enemy_UpdateOne shadow.
///
/// v0.7.07b proved that the standard trace preferred[] tuple can be represented
/// by the source-first generator on every active frame of the current static-player
/// sequence.  This diagnostic checks the actual value that Enemy_UpdateOne would
/// consume for the selected enemy slot: preferred[slot].
///
/// Important: this still uses the standard trace tuple as a replay classifier
/// because the JSONL trace does not yet contain an exact-PC 0x2EA5 LD A,R tape.
/// It is therefore a bridge check, not a fully autonomous preferred[] producer.
/// </summary>
public static class LadyBugEnemyUpdateOnePreferredInputShadowModel
{
    private const string Version = "v0.7.08";

    private sealed class Counters
    {
        public int Frames;
        public int Transitions;
        public int Checks;
        public int Matches;
        public int Mismatches;
        public int SkippedMissingEnemyWork;
        public int SkippedNoActiveEnemy;
        public int SkippedMissingReferencePreferred;
        public int SkippedPreferredProvider;
        public int Slot0;
        public int Slot1;
        public int Slot2;
        public int Slot3;
        public string FirstMatch = string.Empty;
        public string FirstMismatch = string.Empty;
        public readonly Dictionary<string, int> ProviderSources = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> SelectedPreferredValues = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Skips = new(StringComparer.Ordinal);
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var counters = new Counters { Frames = referenceFrames.Count };

        for (int i = 1; i < referenceFrames.Count; i++)
            AnalyzeTransition(referenceFrames[i - 1], referenceFrames[i], counters);

        return BuildSummaryText(counters);
    }

    private static void AnalyzeTransition(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        Counters counters)
    {
        counters.Transitions++;

        if (currentFrame.enemyWork == null)
        {
            counters.SkippedMissingEnemyWork++;
            return;
        }

        if (!TrySelectEnemyWorkSlot(currentFrame, out EnemyTraceActor? currentEnemy) || currentEnemy == null)
        {
            counters.SkippedNoActiveEnemy++;
            return;
        }

        int slot = ClampSlot(currentEnemy.slot);
        CountSlot(counters, slot);

        if (currentFrame.enemyWork.preferred == null || currentFrame.enemyWork.preferred.Count <= slot)
        {
            counters.SkippedMissingReferencePreferred++;
            return;
        }

        if (!LadyBugEnemyPreferredGeneratorReplayShadowModel.TryBuildPreferredTupleForActiveFrame(
                currentFrame,
                out int[] replayTuple,
                out string providerSource,
                out string skipReason))
        {
            counters.SkippedPreferredProvider++;
            Count(counters.Skips, skipReason.Length == 0 ? "unknown" : skipReason);
            return;
        }

        counters.Checks++;
        Count(counters.ProviderSources, providerSource);

        int modeledPreferred = replayTuple[slot] & 0x0F;
        int referencePreferred = currentFrame.enemyWork.preferred[slot] & 0x0F;
        Count(counters.SelectedPreferredValues, "slot" + slot.ToString(CultureInfo.InvariantCulture) + ":" + FormatByte(modeledPreferred));

        string context =
            "tick=" + currentFrame.frame +
            " mameFrame=" + currentFrame.mameFrame +
            " slot=" + slot.ToString(CultureInfo.InvariantCulture) +
            " provider=" + providerSource +
            " preferredRef=" + FormatByte(referencePreferred) +
            " preferredModel=" + FormatByte(modeledPreferred) +
            " tuple=" + LadyBugMonsterPreferenceSystem.FormatTuple(replayTuple) +
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
            return;
        }

        counters.Mismatches++;
        if (string.IsNullOrEmpty(counters.FirstMismatch))
            counters.FirstMismatch = context;
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

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug Enemy_UpdateOne preferred-input replay shadow ").Append(Version).Append(": ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", checks=").Append(counters.Checks);
        builder.Append(", matches=").Append(counters.Matches);
        builder.Append(", mismatches=").Append(counters.Mismatches);
        builder.Append(", skippedMissingEnemyWork=").Append(counters.SkippedMissingEnemyWork);
        builder.Append(", skippedNoActiveEnemy=").Append(counters.SkippedNoActiveEnemy);
        builder.Append(", skippedMissingReferencePreferred=").Append(counters.SkippedMissingReferencePreferred);
        builder.Append(", skippedPreferredProvider=").Append(counters.SkippedPreferredProvider);
        builder.Append(", slots=[0:").Append(counters.Slot0)
            .Append(",1:").Append(counters.Slot1)
            .Append(",2:").Append(counters.Slot2)
            .Append(",3:").Append(counters.Slot3).Append("]");

        if (!string.IsNullOrEmpty(counters.FirstMatch))
            builder.Append(", firstMatch: ").Append(counters.FirstMatch);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);

        builder.Append(", providerSources: ").Append(DescribeDictionary(counters.ProviderSources));
        builder.Append(", selectedPreferredValues: ").Append(DescribeDictionary(counters.SelectedPreferredValues));
        builder.Append(", skips: ").Append(DescribeDictionary(counters.Skips));
        builder.Append(". NOTE: bridge-only. This proves that the selected preferred[slot] consumed by the current Enemy_UpdateOne shadow can be supplied by the replay/classifier provider for the current one-enemy static-player sequence. The adapter still keeps preferred[] reference-synced until the exact-PC random tape is injected into the standard replay path.");
        return builder.ToString();
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
        foreach (KeyValuePair<string, int> pair in values)
            parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));

        return string.Join("; ", parts);
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }
}
