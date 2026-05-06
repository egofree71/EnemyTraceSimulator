using System;
using System.Collections.Generic;

/// <summary>
/// Source-first transcription scaffold for the Lady Bug arcade enemy decision path.
///
/// This class is intentionally additive: it does not replace the current
/// reference-synced adapter yet.  The goal is to validate each reconstructed Z80
/// block in isolation before wiring it into LadyBugSimulationState.
///
/// Main source: LadyBug_enemy_management_extract.txt
/// Relevant Z80 blocks:
/// - 0x3911..0x3955  logical maze direction validation
/// - 0x4130..0x4188  local door/tile direction validation
/// - 0x4189..0x4223  door-local forced reversal probe
/// - 0x4224..0x4240  one-pixel temp movement
/// - 0x4241..0x427D  fallback direction selection
/// - 0x42BA..0x4356  Enemy_UpdateOne / preferred / reversal path
/// - 0x43BA..0x43CB  apply movement step and 0x61C2 helper increment
/// - 0x43D4..0x43EF  commit temp state back to enemy slot
/// </summary>
public static class LadyBugEnemyDecisionModel
{
    // Enemy direction encoding from LadyBug_enemy_management_extract.txt.
    public const int DirLeft = 0x01;
    public const int DirUp = 0x02;
    public const int DirRight = 0x04;
    public const int DirDown = 0x08;

    public const int EnemyTempDirAddress = 0x61BD;
    public const int EnemyTempXAddress = 0x61BE;
    public const int EnemyTempYAddress = 0x61BF;
    public const int EnemyRejectedDirMaskAddress = 0x61C1;
    public const int EnemyFallbackHelperAddress = 0x61C2;
    public const int EnemyPreferredBaseAddress = 0x61C4;

    /// <summary>
    /// Minimal enemy-slot state corresponding to the 5-byte arcade slot.
    ///
    /// Arcade layout:
    /// +0 = high nibble direction + flags, bit 1 active/enabled.
    /// +1 = X coordinate.
    /// +2 = Y coordinate.
    /// +3/+4 = sprite/attribute bytes, not modified by this decision model.
    /// </summary>
    public sealed class EnemySlotState
    {
        public int Raw { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Sprite { get; set; }
        public int Attribute { get; set; }

        public int Direction
        {
            get => (Raw >> 4) & 0x0F;
            set => Raw = ((value & 0x0F) << 4) | (Raw & 0x0F);
        }

        public bool Active
        {
            get => (Raw & 0x02) != 0;
            set
            {
                if (value)
                    Raw |= 0x02;
                else
                    Raw &= ~0x02;
            }
        }
    }

    /// <summary>
    /// Scratch work area matching 0x61BD..0x61C2.
    /// </summary>
    public sealed class EnemyDecisionScratch
    {
        public int TempDir { get; set; }
        public int TempX { get; set; }
        public int TempY { get; set; }
        public int RejectedDirMask { get; set; }
        public int FallbackHelper { get; set; }

        public EnemyDecisionScratch Clone()
        {
            return new EnemyDecisionScratch
            {
                TempDir = TempDir & 0xFF,
                TempX = TempX & 0xFF,
                TempY = TempY & 0xFF,
                RejectedDirMask = RejectedDirMask & 0xFF,
                FallbackHelper = FallbackHelper & 0xFF
            };
        }
    }

    public readonly struct LogicalMazeValidationResult
    {
        public LogicalMazeValidationResult(
            bool accepted,
            int tableIndex,
            int packedByte,
            int allowedMask,
            int candidateDirection)
        {
            Accepted = accepted;
            TableIndex = tableIndex;
            PackedByte = packedByte & 0xFF;
            AllowedMask = allowedMask & 0x0F;
            CandidateDirection = candidateDirection & 0x0F;
        }

        public bool Accepted { get; }
        public bool Rejected => !Accepted;
        public int TableIndex { get; }
        public int PackedByte { get; }
        public int AllowedMask { get; }
        public int CandidateDirection { get; }
    }

    public readonly struct DirectionAttemptResult
    {
        public DirectionAttemptResult(
            bool accepted,
            int selectedDirection,
            int rejectedMask,
            int allowedMask,
            bool fallbackEntered,
            string source)
        {
            Accepted = accepted;
            SelectedDirection = selectedDirection & 0x0F;
            RejectedMask = rejectedMask & 0x0F;
            AllowedMask = allowedMask & 0x0F;
            FallbackEntered = fallbackEntered;
            Source = source;
        }

        public bool Accepted { get; }
        public int SelectedDirection { get; }
        public int RejectedMask { get; }
        public int AllowedMask { get; }
        public bool FallbackEntered { get; }
        public string Source { get; }
    }

    public readonly struct FallbackResult
    {
        public FallbackResult(bool found, int direction, int attempts, int finalScanMask)
        {
            Found = found;
            Direction = direction & 0x0F;
            Attempts = attempts;
            FinalScanMask = finalScanMask & 0xFF;
        }

        public bool Found { get; }
        public int Direction { get; }
        public int Attempts { get; }
        public int FinalScanMask { get; }
    }

    /// <summary>
    /// Equivalent to the simple alignment part of 0x427E.
    ///
    /// 0x427F..0x428B checks:
    ///   (x & 0x0F) == 0x08
    ///   (y & 0x0F) == 0x06
    ///
    /// The later 0x428D..0x42B8 helper calls still need focused validation.
    /// This function should therefore be treated as the pixel-center predicate,
    /// not yet as the complete carry behavior of 0x427E.
    /// </summary>
    public static bool EnemyIsAtDecisionCenter(int x, int y)
    {
        return (x & 0x0F) == 0x08 && (y & 0x0F) == 0x06;
    }

    /// <summary>
    /// Transcription of 0x3911..0x3955.
    ///
    /// Inputs mirror the Z80 call convention used by 0x42F9 and 0x425F:
    /// - IX points to X/Y bytes. Here these are x/y arguments.
    /// - D contains candidateDirection.
    /// - The packed table starts at arcade address 0x0DA2.
    ///
    /// The table is addressed as:
    ///   base + 6 * (x >> 4) + (((y >> 4) - 3) >> 1)
    /// Then the low or high nibble is selected from the packed byte according to
    /// bit 0 of ((y >> 4) - 3).
    ///
    /// Carry set in Z80 means rejection.  The result returns Accepted=false.
    /// </summary>
    public static LogicalMazeValidationResult ValidateLogicalMazeDirection(
        int candidateDirection,
        int x,
        int y,
        IReadOnlyList<int> packedMazeTable0DA2)
    {
        if (packedMazeTable0DA2 == null)
            throw new ArgumentNullException(nameof(packedMazeTable0DA2));

        int xCell = (x & 0xFF) >> 4;
        int columnOffset = xCell * 6;

        int ySelector = (((y & 0xFF) >> 4) - 3) & 0xFF;
        int tableIndex = columnOffset + (ySelector >> 1);

        if (tableIndex < 0 || tableIndex >= packedMazeTable0DA2.Count)
            throw new ArgumentOutOfRangeException(
                nameof(packedMazeTable0DA2),
                "0x3911 logical-maze table index is outside the supplied 0x0DA2 table.");

        int packedByte = packedMazeTable0DA2[tableIndex] & 0xFF;
        int allowedMask = (ySelector & 0x01) != 0
            ? (packedByte >> 4) & 0x0F
            : packedByte & 0x0F;

        bool accepted = (allowedMask & (candidateDirection & 0x0F)) != 0;

        return new LogicalMazeValidationResult(
            accepted,
            tableIndex,
            packedByte,
            allowedMask,
            candidateDirection);
    }

    /// <summary>
    /// Transcription of 0x4130..0x4188.
    ///
    /// The arcade calls GetTileUnderPlayerProbe at 0x3C0A after applying a small
    /// direction-specific probe offset.  Tile addressing is deliberately delegated
    /// to readTileAtProbe because 0x3C0A is a separate routine and should not be
    /// guessed here.
    ///
    /// Return value follows C# naming: true means the direction is locally valid;
    /// false corresponds to Z80 carry set at 0x4187.
    /// </summary>
    public static bool ValidateLocalDoorBlock(
        int candidateDirection,
        int x,
        int y,
        Func<int, int, int> readTileAtProbe)
    {
        if (readTileAtProbe == null)
            throw new ArgumentNullException(nameof(readTileAtProbe));

        int direction = candidateDirection & 0x0F;
        int d = x & 0xFF;
        int e = y & 0xFF;
        int tile;

        if ((direction & DirLeft) != 0)
        {
            // 0x414F..0x415F: D = X - 1; reject 3D / 3F.
            d = Byte(d - 1);
            tile = ReadTile(readTileAtProbe, d, e);
            return tile != 0x3D && tile != 0x3F;
        }

        if ((direction & DirUp) != 0)
        {
            // 0x4162..0x4172: E = Y - 7; reject 35 / 37.
            e = Byte(e - 7);
            tile = ReadTile(readTileAtProbe, d, e);
            return tile != 0x35 && tile != 0x37;
        }

        if ((direction & DirRight) != 0)
        {
            // 0x4175..0x4185: D = X + 8; reject 3F / 3D.
            d = Byte(d + 8);
            tile = ReadTile(readTileAtProbe, d, e);
            return tile != 0x3F && tile != 0x3D;
        }

        // 0x413C..0x414C: default branch is down. E = Y + 2; reject 35 / 37.
        e = Byte(e + 2);
        tile = ReadTile(readTileAtProbe, d, e);
        return tile != 0x35 && tile != 0x37;
    }

    /// <summary>
    /// Transcription of 0x4189..0x4223.
    ///
    /// This is the door-local forced reversal probe used outside the normal
    /// decision-center path.  It returns true when the Z80 routine would set carry
    /// at 0x4222, causing 0x4347 to reverse EnemyTemp_Dir.
    /// </summary>
    public static bool CheckDoorForcedReversal(
        int currentDirection,
        int x,
        int y,
        Func<int, int, int> readTileAtProbe)
    {
        if (readTileAtProbe == null)
            throw new ArgumentNullException(nameof(readTileAtProbe));

        int direction = currentDirection & 0x0F;
        int d = x & 0xFF;
        int e = y & 0xFF;
        int b;

        if ((direction & DirLeft) != 0)
        {
            // 0x41B9..0x41DA.
            b = Byte(d - 1);
            if (IsAny(ReadTile(readTileAtProbe, b, e), 0x45, 0x46))
                return true;

            d = Byte(b - 2);
            return IsAny(ReadTile(readTileAtProbe, d, e), 0x4A, 0x41);
        }

        if ((direction & DirUp) != 0)
        {
            // 0x41DC..0x41FD.
            b = Byte(e - 1);
            if (IsAny(ReadTile(readTileAtProbe, d, b), 0x49, 0x43))
                return true;

            e = Byte(b - 6);
            return IsAny(ReadTile(readTileAtProbe, d, e), 0x41, 0x46);
        }

        if ((direction & DirRight) != 0)
        {
            // 0x41FF..0x421E.
            b = Byte(d + 2);
            if (IsAny(ReadTile(readTileAtProbe, b, e), 0x44, 0x47))
                return true;

            d = Byte(b + 6);
            return IsAny(ReadTile(readTileAtProbe, d, e), 0x4A, 0x41);
        }

        // 0x4195..0x41B7: default branch is down.
        b = Byte(e + 2);
        if (IsAny(ReadTile(readTileAtProbe, d, b), 0x4A, 0x45))
            return true;

        e = Byte(b + 2);
        return IsAny(ReadTile(readTileAtProbe, d, e), 0x41, 0x46);
    }

    /// <summary>
    /// Transcription of 0x4347..0x4356.
    ///
    /// Z80 formula:
    ///   A = dir; B = dir;
    ///   A >>= 2; B <<= 2;
    ///   dir = (A | B) & 0x0F
    ///
    /// One-hot result:
    ///   01 left  -> 04 right
    ///   02 up    -> 08 down
    ///   04 right -> 01 left
    ///   08 down  -> 02 up
    /// </summary>
    public static int ReverseDirection(int direction)
    {
        int a = (direction & 0x0F) >> 2;
        int b = (direction & 0x0F) << 2;
        return (a | b) & 0x0F;
    }

    /// <summary>
    /// Transcription of 0x4224..0x4240.
    /// Moves the temporary enemy position by exactly one arcade pixel.
    /// </summary>
    public static void MoveTempOnePixel(EnemyDecisionScratch scratch)
    {
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));

        switch (scratch.TempDir & 0x0F)
        {
            case DirLeft:
                scratch.TempX = Byte(scratch.TempX - 1);
                break;
            case DirUp:
                scratch.TempY = Byte(scratch.TempY - 1);
                break;
            case DirRight:
                scratch.TempX = Byte(scratch.TempX + 1);
                break;
            default:
                scratch.TempY = Byte(scratch.TempY + 1);
                break;
        }
    }

    /// <summary>
    /// Transcription of 0x43BA..0x43CB for the single-step part:
    /// - call 0x4224 one-pixel temp movement;
    /// - increment 0x61C2 fallback helper.
    ///
    /// The original code compares the incremented 0x61C2 value with a stack-restored
    /// E register and may loop back to 0x42D2.  This method exposes only the atomic
    /// step so the loop condition can be validated separately against exact-PC logs.
    /// </summary>
    public static void ApplyEnemyTempMovementStep(EnemyDecisionScratch scratch)
    {
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));

        MoveTempOnePixel(scratch);
        scratch.FallbackHelper = Byte(scratch.FallbackHelper + 1);
    }

    /// <summary>
    /// Transcription of 0x4241..0x427D.
    ///
    /// The fallback scanner starts from the rejected-direction mask in A and scans
    /// candidate directions in order 01, 02, 04, 08. A set bit means "already
    /// rejected". A clear bit is tested through 0x3911 and 0x4130. If all four bits
    /// are set or rejected, the arcade code clears A and restarts the scan.
    ///
    /// maxAttempts defaults to 8 to avoid an infinite loop in test harnesses when
    /// the caller supplies an impossible maze/tile setup. In valid arcade states,
    /// a direction should be found before that guard is hit.
    /// </summary>
    public static FallbackResult FindFallbackDirection(
        EnemyDecisionScratch scratch,
        IReadOnlyList<int> packedMazeTable0DA2,
        Func<int, int, int> readTileAtProbe,
        int maxAttempts = 8)
    {
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));
        if (readTileAtProbe == null)
            throw new ArgumentNullException(nameof(readTileAtProbe));
        if (maxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        int scanMask = scratch.RejectedDirMask & 0x0F;
        int direction = DirLeft;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool alreadyRejected = (scanMask & 0x01) != 0;

            if (!alreadyRejected)
            {
                LogicalMazeValidationResult logical = ValidateLogicalMazeDirection(
                    direction,
                    scratch.TempX,
                    scratch.TempY,
                    packedMazeTable0DA2);

                if (logical.Accepted &&
                    ValidateLocalDoorBlock(direction, scratch.TempX, scratch.TempY, readTileAtProbe))
                {
                    scratch.TempDir = direction;
                    return new FallbackResult(true, direction, attempt, scanMask);
                }
            }

            direction <<= 1;
            scanMask >>= 1;

            if ((direction & 0x10) != 0)
            {
                // 0x4256 XOR A; 0x4257 JP 0x424B.
                scanMask = 0;
                direction = DirLeft;
            }
        }

        return new FallbackResult(false, 0, maxAttempts, scanMask);
    }

    /// <summary>
    /// Transcription of the decision part of 0x42E6..0x4337.
    ///
    /// It tries the preferred candidate first. If that candidate is rejected by
    /// 0x3911 or 0x4130, it writes 0x61C1 |= preferred. Then it tests whether the
    /// current temp direction is still allowed by the logical mask returned by
    /// 0x3911 and by local-door validation. If the current direction is rejected
    /// too, it writes 0x61C1 |= current and enters the fallback scanner at 0x4241.
    /// </summary>
    public static DirectionAttemptResult TryPreferredDirection(
        EnemyDecisionScratch scratch,
        int preferredDirection,
        IReadOnlyList<int> packedMazeTable0DA2,
        Func<int, int, int> readTileAtProbe)
    {
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));
        if (readTileAtProbe == null)
            throw new ArgumentNullException(nameof(readTileAtProbe));

        int preferred = preferredDirection & 0x0F;
        LogicalMazeValidationResult logical = ValidateLogicalMazeDirection(
            preferred,
            scratch.TempX,
            scratch.TempY,
            packedMazeTable0DA2);

        bool preferredAccepted = logical.Accepted &&
            ValidateLocalDoorBlock(preferred, scratch.TempX, scratch.TempY, readTileAtProbe);

        if (preferredAccepted)
        {
            // 0x430C..0x4310: EnemyTemp_Dir = preferred; RET.
            scratch.TempDir = preferred;
            return new DirectionAttemptResult(
                accepted: true,
                selectedDirection: preferred,
                rejectedMask: scratch.RejectedDirMask,
                allowedMask: logical.AllowedMask,
                fallbackEntered: false,
                source: "42E6_PREFERRED_ACCEPTED");
        }

        // 0x4311..0x4315: rejectedMask |= preferred candidate.
        scratch.RejectedDirMask = (scratch.RejectedDirMask | preferred) & 0x0F;

        int current = scratch.TempDir & 0x0F;
        bool currentLogicalAllowed = (current & logical.AllowedMask) != 0;

        if (currentLogicalAllowed &&
            ValidateLocalDoorBlock(current, scratch.TempX, scratch.TempY, readTileAtProbe))
        {
            // 0x4325 then RET NC: keep current temp direction.
            return new DirectionAttemptResult(
                accepted: true,
                selectedDirection: current,
                rejectedMask: scratch.RejectedDirMask,
                allowedMask: logical.AllowedMask,
                fallbackEntered: false,
                source: "4315_PREFERRED_REJECTED_CURRENT_KEPT");
        }

        // 0x4329..0x4331: rejectedMask |= current temp direction.
        scratch.RejectedDirMask = (scratch.RejectedDirMask | current) & 0x0F;

        FallbackResult fallback = FindFallbackDirection(
            scratch,
            packedMazeTable0DA2,
            readTileAtProbe);

        return new DirectionAttemptResult(
            accepted: fallback.Found,
            selectedDirection: scratch.TempDir,
            rejectedMask: scratch.RejectedDirMask,
            allowedMask: logical.AllowedMask,
            fallbackEntered: true,
            source: fallback.Found ? "4331_FALLBACK_SELECTED" : "4331_FALLBACK_NOT_FOUND");
    }

    /// <summary>
    /// Source-first high-level skeleton for 0x42BA Enemy_UpdateOne.
    ///
    /// This intentionally does not model 0x43C5's stack-dependent loop yet. It is
    /// sufficient for unit-level validation of decision, forced reversal, and the
    /// atomic movement/helper step.
    /// </summary>
    public static DirectionAttemptResult EnemyUpdateOneSingleStep(
        EnemySlotState enemy,
        int preferredDirection,
        IReadOnlyList<int> packedMazeTable0DA2,
        Func<int, int, int> readTileAtProbe)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));
        if (readTileAtProbe == null)
            throw new ArgumentNullException(nameof(readTileAtProbe));

        EnemyDecisionScratch scratch = LoadCurrentStateToTemp(enemy);

        // 0x42CC / 0x42CF.
        scratch.RejectedDirMask = 0;
        scratch.FallbackHelper = 0;

        DirectionAttemptResult decision;

        if (EnemyIsAtDecisionCenter(scratch.TempX, scratch.TempY))
        {
            decision = TryPreferredDirection(
                scratch,
                preferredDirection,
                packedMazeTable0DA2,
                readTileAtProbe);
        }
        else
        {
            if (CheckDoorForcedReversal(scratch.TempDir, scratch.TempX, scratch.TempY, readTileAtProbe))
                scratch.TempDir = ReverseDirection(scratch.TempDir);

            decision = new DirectionAttemptResult(
                accepted: true,
                selectedDirection: scratch.TempDir,
                rejectedMask: scratch.RejectedDirMask,
                allowedMask: 0,
                fallbackEntered: false,
                source: "433A_OUTSIDE_CENTER_KEEP_OR_REVERSE");
        }

        ApplyEnemyTempMovementStep(scratch);
        CommitTempState(enemy, scratch);
        return decision;
    }

    /// <summary>
    /// Transcription of 0x43F0..0x4405.
    /// </summary>
    public static EnemyDecisionScratch LoadCurrentStateToTemp(EnemySlotState enemy)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));

        return new EnemyDecisionScratch
        {
            TempDir = enemy.Direction,
            TempX = enemy.X & 0xFF,
            TempY = enemy.Y & 0xFF,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };
    }

    /// <summary>
    /// Transcription of 0x43D4..0x43EF.
    /// </summary>
    public static void CommitTempState(EnemySlotState enemy, EnemyDecisionScratch scratch)
    {
        if (enemy == null)
            throw new ArgumentNullException(nameof(enemy));
        if (scratch == null)
            throw new ArgumentNullException(nameof(scratch));

        enemy.Direction = scratch.TempDir;
        enemy.Active = true;
        enemy.X = scratch.TempX & 0xFF;
        enemy.Y = scratch.TempY & 0xFF;
    }

    private static int ReadTile(Func<int, int, int> readTileAtProbe, int x, int y)
    {
        return readTileAtProbe(Byte(x), Byte(y)) & 0xFF;
    }

    private static bool IsAny(int value, int a, int b)
    {
        int v = value & 0xFF;
        return v == (a & 0xFF) || v == (b & 0xFF);
    }

    private static int Byte(int value)
    {
        return value & 0xFF;
    }
}
