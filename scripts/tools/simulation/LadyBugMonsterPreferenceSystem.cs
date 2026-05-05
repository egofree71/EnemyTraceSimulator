using System;
using System.Collections.Generic;

/// <summary>
/// First C# model of Lady Bug's enemy preferred-direction generator.
///
/// This class is deliberately small and side-effect free. It does not yet update
/// LadyBugSimulationState directly. It exists so diagnostics can validate the
/// arcade model before the adapter stops reference-syncing EnemyWork.preferred[].
///
/// Current validated scope:
///
/// - 0x2E97 rotate branch:
///     starting from PLAYER_DIR_CURRENT, rotate right over the 4 direction bits
///     and produce four preferred[] slots.
///
/// - 0x2EC7 random branch:
///     model the internal R low-nibble sequence observed at the LD A,R path.
///     The R value visible at the 0x2EC7 write PC is too late; diagnostics
///     reconstruct the low nibble used by LD A,R, then this model generates
///     the same tuple by applying the direction-dependent internal R deltas.
///
/// - 0x477D BFS/chase override:
///     apply a single preferred[] overwrite when IY points at 0x61C4..0x61C7.
///
/// Direction encoding:
///     01 = left
///     02 = up
///     04 = right
///     08 = down
/// </summary>
public static class LadyBugMonsterPreferenceSystem
{
    public const int DirLeft = 0x01;
    public const int DirUp = 0x02;
    public const int DirRight = 0x04;
    public const int DirDown = 0x08;

    public const int PreferredBaseAddress = 0x61C4;
    public const int PreferredSlotCount = 4;

    public static int[] GenerateRotateBranch(int playerDirectionCurrent)
    {
        int[] preferred = new int[PreferredSlotCount];
        int direction = playerDirectionCurrent & 0x0F;

        for (int i = 0; i < preferred.Length; i++)
        {
            direction = RotateRight4(direction);
            preferred[i] = direction;
        }

        return preferred;
    }

    public static int[] GenerateRandomBranchFromUsedRLow(int usedRLowStart)
    {
        int[] preferred = new int[PreferredSlotCount];
        int usedRLow = usedRLowStart & 0x0F;

        for (int i = 0; i < preferred.Length; i++)
        {
            int direction = DirectionFromRandomNibble(usedRLow);
            preferred[i] = direction;
            usedRLow = AdvanceUsedRLowAfterDirection(usedRLow, direction);
        }

        return preferred;
    }

    public static int DirectionFromRandomNibble(int rLowNibble)
    {
        // Assembly branch at 0x2EA3:
        //
        //   LD A,R
        //   AND 0x0F
        //   SRL A
        //   INC A
        //
        // Then the value ranges are mapped to 01, 02, 04, 08.
        int value = ((rLowNibble & 0x0F) >> 1) + 1;

        if (value < 3)
            return DirLeft;

        if (value < 5)
            return DirUp;

        if (value < 7)
            return DirRight;

        return DirDown;
    }

    public static int AdvanceUsedRLowAfterDirection(int usedRLow, int generatedDirection)
    {
        return (usedRLow + UsedRLowDeltaAfterDirection(generatedDirection)) & 0x0F;
    }

    public static int UsedRLowDeltaAfterDirection(int generatedDirection)
    {
        return (generatedDirection & 0x0F) switch
        {
            DirLeft => 0x0D,
            DirUp => 0x0F,
            DirRight => 0x00,
            DirDown => 0x01,
            _ => 0x00
        };
    }

    public static int ReconstructUsedRLowFromWritePcR(int rAtWritePc, int finalDirection)
    {
        int subtract = (finalDirection & 0x0F) switch
        {
            DirLeft => 0x08,
            DirUp => 0x0A,
            DirRight => 0x0B,
            DirDown => 0x0C,
            _ => 0x00
        };

        return (rAtWritePc - subtract) & 0x0F;
    }

    public static int RotateRight4(int direction)
    {
        int value = direction & 0x0F;
        int shifted = (value >> 1) & 0x0F;

        if ((value & DirLeft) != 0)
            shifted |= DirDown;

        return shifted & 0x0F;
    }

    public static bool TryApplyBfsOverride(int[] preferred, int iyAddress, int direction)
    {
        if (preferred == null)
            throw new ArgumentNullException(nameof(preferred));

        int slot = iyAddress - PreferredBaseAddress;
        if (slot < 0 || slot >= PreferredSlotCount || slot >= preferred.Length)
            return false;

        preferred[slot] = direction & 0x0F;
        return true;
    }

    public static bool TupleEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count < PreferredSlotCount || b.Count < PreferredSlotCount)
            return false;

        for (int i = 0; i < PreferredSlotCount; i++)
        {
            if ((a[i] & 0x0F) != (b[i] & 0x0F))
                return false;
        }

        return true;
    }

    public static string FormatTuple(IReadOnlyList<int> values)
    {
        var parts = new List<string>(PreferredSlotCount);

        for (int i = 0; i < PreferredSlotCount && i < values.Count; i++)
            parts.Add((values[i] & 0x0F).ToString("X2"));

        return "[" + string.Join(",", parts) + "]";
    }
}
