using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Guarded preferred[] provider for v0.9.12.
///
/// This is the first step where the movement replay can use a modeled
/// preferred[] tuple instead of always reading the selected preferred direction
/// directly from the trace.
///
/// Current autonomous subset:
/// - deterministic 0x2E97 rotate branch, generated from the current player
///   direction through LadyBugMonsterPreferenceSystem.GenerateRotateBranch().
///
/// Still trace-synced:
/// - 0x2EC7 random/R-register tuples;
/// - 0x477D BFS/chase overrides;
/// - any tuple that is not exactly explained by the deterministic rotate model.
///
/// The trace tuple is still required as a guard in this package. This makes the
/// change safe for the visual replay while establishing a real modeled-preferred
/// path for the deterministic rotate family.
/// </summary>
public static class LadyBugPreferredHybridProvider
{
    public static bool TryGetPreferred(EnemyTraceFrame frame, int activeSlot, out Result result)
    {
        result = Result.Missing(activeSlot);

        if (!TryCopyReferencePreferredTuple(frame, out int[] referenceTuple))
            return false;

        if (activeSlot < 0 || activeSlot >= LadyBugMonsterPreferenceSystem.PreferredSlotCount)
            return false;

        if (!IsOneHotDirection(referenceTuple[activeSlot]))
            return false;

        if (TryGetPlayerDirection(frame, out int playerDirection))
        {
            int[] rotateTuple = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(playerDirection);
            if (LadyBugMonsterPreferenceSystem.TupleEquals(rotateTuple, referenceTuple))
            {
                result = Result.ModeledRotate(activeSlot, rotateTuple, playerDirection);
                return true;
            }
        }

        result = Result.TraceFallback(activeSlot, referenceTuple, "trace-sync-random-or-bfs-or-unmatched-rotate");
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

    private static bool TryGetPlayerDirection(EnemyTraceFrame frame, out int direction)
    {
        direction = 0;

        if (frame.player == null)
            return false;

        if (TryParseDirectionText(frame.player.dir, out direction))
            return true;

        direction = DirectionFromRaw(frame.player.raw);
        return IsOneHotDirection(direction);
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
        return IsOneHotDirection(direction);
    }

    private static int DirectionFromRaw(int raw)
    {
        return raw < 0 ? 0 : (raw >> 4) & 0x0F;
    }

    private static bool IsOneHotDirection(int direction)
    {
        int d = direction & 0x0F;
        return d == LadyBugMonsterPreferenceSystem.DirLeft ||
               d == LadyBugMonsterPreferenceSystem.DirUp ||
               d == LadyBugMonsterPreferenceSystem.DirRight ||
               d == LadyBugMonsterPreferenceSystem.DirDown;
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    public readonly struct Result
    {
        private Result(
            int activeSlot,
            int[] preferredTuple,
            bool usedModeledRotate,
            string source)
        {
            ActiveSlot = activeSlot;
            PreferredTuple = preferredTuple;
            UsedModeledRotate = usedModeledRotate;
            Source = source;
        }

        public int ActiveSlot { get; }
        public int[] PreferredTuple { get; }
        public bool UsedModeledRotate { get; }
        public string Source { get; }

        public int Preferred =>
            ActiveSlot >= 0 && ActiveSlot < PreferredTuple.Length
                ? PreferredTuple[ActiveSlot] & 0x0F
                : 0;

        public static Result Missing(int activeSlot)
        {
            return new Result(activeSlot, new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount], false, "missing");
        }

        public static Result ModeledRotate(int activeSlot, int[] preferredTuple, int playerDirection)
        {
            return new Result(
                activeSlot,
                CopyTuple(preferredTuple),
                true,
                "2E97_MODELED_ROTATE_FROM_" + Hex2(playerDirection));
        }

        public static Result TraceFallback(int activeSlot, int[] preferredTuple, string source)
        {
            return new Result(activeSlot, CopyTuple(preferredTuple), false, source);
        }

        private static int[] CopyTuple(IReadOnlyList<int> values)
        {
            var tuple = new int[LadyBugMonsterPreferenceSystem.PreferredSlotCount];
            for (int i = 0; i < tuple.Length && i < values.Count; i++)
                tuple[i] = values[i] & 0x0F;
            return tuple;
        }
    }
}
