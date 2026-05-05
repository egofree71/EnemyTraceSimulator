using System.Collections.Generic;
using System.Text;

/// <summary>
/// Mutable state owned by the Lady Bug enemy simulation adapter.
///
/// This class is the boundary between reference-assisted validation and the
/// future real arcade simulation. Fields should be carefully documented as either
/// simulated, derived, or temporarily synchronized from MAME.
///
/// v0.6.7 adds the first tick-advance hook. It currently syncs external
/// player, port, gate, timer, and enemy control state from the reference trace. Active enemies are moved by one pixel using the reference direction.
/// </summary>
public sealed class LadyBugSimulationState
{
    public SimulationActorState? Player { get; private set; }
    public List<SimulationActorState> Enemies { get; } = new();
    public List<SimulationGateState> Gates { get; } = new();
    public SimulationEnemyWorkState? EnemyWork { get; private set; }
    public SimulationTimersState? Timers { get; private set; }
    public SimulationPortsState? Ports { get; private set; }

    private int _stableEnemyWorkCandidateTicks;
    private int _preferredShadowChecks;
    private int _preferredShadowMatches;
    private int _preferredShadowMismatches;
    private string _firstPreferredShadowMismatch = string.Empty;
    private readonly Dictionary<string, int> _preferredShadowSourceCounts = new();


    public static LadyBugSimulationState FromInitialState(LadyBugSimulationInitialState initialState)
    {
        var state = new LadyBugSimulationState
        {
            Player = initialState.Player == null ? null : CloneActor(initialState.Player),
            EnemyWork = initialState.EnemyWork == null ? null : CloneEnemyWork(initialState.EnemyWork),
            Timers = initialState.Timers == null ? null : CloneTimers(initialState.Timers),
            Ports = initialState.Ports == null ? null : ClonePorts(initialState.Ports)
        };

        foreach (SimulationActorState enemy in initialState.Enemies)
            state.Enemies.Add(CloneActor(enemy));

        foreach (SimulationGateState gate in initialState.Gates)
            state.Gates.Add(CloneGate(gate));

        return state;
    }

    public void AdvanceOneTick(EnemyTraceFrame referenceFrame)
    {
        SyncReferenceInputs(referenceFrame);
        SyncReferenceEnvironment(referenceFrame);
        AdvanceEnemiesUsingReferenceControlState(referenceFrame);
        UpdateEnemyWorkTempMovementFields(referenceFrame);

        // Intentionally not updated yet:
        // - enemy decision logic
        // - rejection / fallback masks
        // - preferred directions
        // - chase timers
        //
        // This step validates the low-level coordinate movement convention and mirrors
        // the temp movement candidate fields that naturally follow from the
        // reference-direction step. Later patches should replace the reference direction
        // with the real Lady Bug enemy decision and enemy-work update sequence.
    }

    private void SyncReferenceInputs(EnemyTraceFrame referenceFrame)
    {
        // The player position and input ports are treated as external inputs for enemy
        // validation. This lets the future enemy simulation chase the same player state
        // observed in MAME while still owning enemy movement internally.
        Player = referenceFrame.player == null
            ? null
            : CopyActor(referenceFrame.player);

        Ports = referenceFrame.ports == null
            ? null
            : CopyPorts(referenceFrame.ports);
    }

    private void SyncReferenceEnvironment(EnemyTraceFrame referenceFrame)
    {
        // Gates and global timers are synced from MAME for now so the first real
        // validation target can focus on enemy movement. Once enemy movement matches,
        // these can be replaced by simulated gate/timer logic.
        Gates.Clear();
        if (referenceFrame.gates != null)
        {
            foreach (EnemyTraceGateState gate in referenceFrame.gates)
                Gates.Add(CopyGate(gate));
        }

        Timers = referenceFrame.timers == null
            ? null
            : CopyTimers(referenceFrame.timers);
    }

    private void AdvanceEnemiesUsingReferenceControlState(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
            return;

        foreach (EnemyTraceActor referenceEnemy in referenceFrame.enemies)
        {
            SimulationActorState? enemy = FindEnemyBySlot(referenceEnemy.slot);

            if (enemy == null)
            {
                Enemies.Add(CopyActor(referenceEnemy));
                continue;
            }

            if (!referenceEnemy.active)
            {
                // Inactive slots can be reshuffled by the arcade object manager.
                // They are not part of the movement validation target yet, so keep
                // them aligned with the reference trace.
                CopyActorInto(referenceEnemy, enemy);
                continue;
            }

            if (!enemy.HasKnownPosition)
            {
                CopyActorInto(referenceEnemy, enemy);
                continue;
            }

            enemy.Raw = referenceEnemy.raw;
            enemy.Active = referenceEnemy.active;
            enemy.Direction = referenceEnemy.dir;

            MoveActorOnePixel(enemy, referenceEnemy.dir);
        }
    }

    private void UpdateEnemyWorkTempMovementFields(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemyWork == null || referenceFrame.enemies == null)
            return;

        EnemyTraceActor? activeReferenceEnemy = SelectReferenceEnemyForEnemyWork(referenceFrame);
        if (activeReferenceEnemy == null)
            return;

        SimulationActorState? simulatedEnemy = FindEnemyBySlot(activeReferenceEnemy.slot);
        if (simulatedEnemy == null)
            return;

        if (!TryParseTraceByte(activeReferenceEnemy.dir, out int directionValue))
            return;

        EnemyWork ??= new SimulationEnemyWorkState();

        int previousTempDir = EnemyWork.TempDir;
        int previousTempX = EnemyWork.TempX;
        int previousTempY = EnemyWork.TempY;
        int previousRejectedMask = EnemyWork.RejectedMask;

        EnemyWork.TempDir = directionValue;
        EnemyWork.TempX = simulatedEnemy.X;
        EnemyWork.TempY = simulatedEnemy.Y;
        EnemyWork.RejectedMask = DeriveRejectedMaskCandidate(
            previousTempDir,
            directionValue,
            previousTempX,
            previousTempY,
            previousRejectedMask,
            referenceFrame.enemyWork);

        if (!SyncReferencePreferredState(EnemyWork, referenceFrame.enemyWork))
            UpdatePreferredDirectionCandidate(EnemyWork, simulatedEnemy);

        SyncReferenceChaseState(EnemyWork, referenceFrame.enemyWork);
        UpdatePreferredShadowDiagnostics(EnemyWork, referenceFrame.enemyWork);
    }

    /// <summary>
    /// Temporary bridge for the unsolved preferred[] generator.
    ///
    /// This sync must eventually disappear. It exists so the rest of the one-enemy
    /// validation pipeline can remain exact while preferred[] is reverse-engineered.
    /// </summary>
    private static bool SyncReferencePreferredState(
        SimulationEnemyWorkState enemyWork,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        // Temporary bridge for v0.6: preferred[] is produced by the arcade
        // base-preference / pseudo-random generator around 0x2E5C. That generator
        // is not implemented yet, so sync the reference preferred[] values when
        // they are available. This lets the one-enemy trace be validated without
        // filtering preferred[] while keeping the preferred generator as the next
        // real implementation target.
        if (referenceEnemyWork == null || referenceEnemyWork.preferred.Count == 0)
            return false;

        enemyWork.Preferred.Clear();
        enemyWork.Preferred.AddRange(referenceEnemyWork.preferred);
        return true;
    }

    private static void SyncReferenceChaseState(
        SimulationEnemyWorkState enemyWork,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        // The current adapter validates reference-driven movement and partial
        // decision-center state. BFS/chase timing is a separate subsystem
        // (0x3A4C / 0x46D8 / 0x471E..0x4752), so keep it aligned with MAME for
        // now and continue validating temp/rejected/fallback state.
        if (referenceEnemyWork == null)
            return;

        enemyWork.ChaseTimers.Clear();
        enemyWork.ChaseTimers.AddRange(referenceEnemyWork.chaseTimers);
        enemyWork.ChaseRoundRobin = referenceEnemyWork.chaseRoundRobin;
    }

    /// <summary>
    /// Shadow diagnostic for the preferred[] generator.
    ///
    /// This does not drive the simulation yet. The adapter still keeps preferred[]
    /// synced from MAME. The shadow path only checks whether the end-of-frame
    /// preferred[] tuple seen in the standard JSONL trace can be explained by the
    /// C# MonsterPreferenceSystem model plus the currently observed slot-0 BFS
    /// override shape.
    /// </summary>
    private void UpdatePreferredShadowDiagnostics(
        SimulationEnemyWorkState enemyWork,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        if (referenceEnemyWork == null || referenceEnemyWork.preferred.Count < 4)
            return;

        _preferredShadowChecks++;

        if (TryBuildPreferredShadowCandidate(referenceEnemyWork, out int[] candidate, out string source))
        {
            enemyWork.PreferredShadow.Clear();
            enemyWork.PreferredShadow.AddRange(candidate);
            enemyWork.PreferredShadowSource = source;

            AddPreferredShadowSource(source);

            if (PreferredTupleEquals(candidate, referenceEnemyWork.preferred))
            {
                _preferredShadowMatches++;
                return;
            }

            RegisterPreferredShadowMismatch(referenceEnemyWork, source, candidate);
            return;
        }

        enemyWork.PreferredShadow.Clear();
        enemyWork.PreferredShadowSource = "unclassified";
        AddPreferredShadowSource("unclassified");
        RegisterPreferredShadowMismatch(referenceEnemyWork, "unclassified", new[] { 0, 0, 0, 0 });
    }

    private static bool TryBuildPreferredShadowCandidate(
        EnemyTraceEnemyWorkState referenceEnemyWork,
        out int[] candidate,
        out string source)
    {
        candidate = new[] { 0, 0, 0, 0 };
        source = "none";

        if (referenceEnemyWork.preferred.Count < 4)
            return false;

        int[] referencePreferred =
        {
            referenceEnemyWork.preferred[0] & 0x0F,
            referenceEnemyWork.preferred[1] & 0x0F,
            referenceEnemyWork.preferred[2] & 0x0F,
            referenceEnemyWork.preferred[3] & 0x0F
        };

        int[] rotateFromDown = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(
            LadyBugMonsterPreferenceSystem.DirDown);

        if (PreferredTupleEquals(rotateFromDown, referencePreferred))
        {
            candidate = rotateFromDown;
            source = "2E97_ROTATE_FROM_08";
            return true;
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] randomCandidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);

            if (PreferredTupleEquals(randomCandidate, referencePreferred))
            {
                candidate = randomCandidate;
                source = "2EC7_RANDOM_RLOW_" + rLow.ToString("X1");
                return true;
            }
        }

        // End-of-frame JSONL does not contain the exact 477D PC hit. However, the
        // exact-PC diagnostic proved that in the current one-enemy window BFS/chase
        // overrides preferred[0] / 0x61C4. Therefore, for shadow classification only,
        // accept a base tuple whose tail slots match and whose slot 0 can be replaced
        // by the observed reference preferred[0].
        if (TryBuildSlot0OverrideCandidate(referencePreferred, rotateFromDown, "2E97_ROTATE_FROM_08", out candidate, out source))
            return true;

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] randomCandidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);

            if (TryBuildSlot0OverrideCandidate(referencePreferred, randomCandidate, "2EC7_RANDOM_RLOW_" + rLow.ToString("X1"), out candidate, out source))
                return true;
        }

        return false;
    }

    private static bool TryBuildSlot0OverrideCandidate(
        int[] referencePreferred,
        int[] baseCandidate,
        string baseSource,
        out int[] candidate,
        out string source)
    {
        candidate = new[] { 0, 0, 0, 0 };
        source = "none";

        if ((baseCandidate[1] & 0x0F) != (referencePreferred[1] & 0x0F) ||
            (baseCandidate[2] & 0x0F) != (referencePreferred[2] & 0x0F) ||
            (baseCandidate[3] & 0x0F) != (referencePreferred[3] & 0x0F))
        {
            return false;
        }

        candidate = new[]
        {
            baseCandidate[0] & 0x0F,
            baseCandidate[1] & 0x0F,
            baseCandidate[2] & 0x0F,
            baseCandidate[3] & 0x0F
        };

        LadyBugMonsterPreferenceSystem.TryApplyBfsOverride(
            candidate,
            LadyBugMonsterPreferenceSystem.PreferredBaseAddress,
            referencePreferred[0]);

        source = "477D_OBSERVED_SLOT0_OVER_" + baseSource;
        return true;
    }

    private void RegisterPreferredShadowMismatch(
        EnemyTraceEnemyWorkState referenceEnemyWork,
        string source,
        int[] candidate)
    {
        _preferredShadowMismatches++;

        if (!string.IsNullOrEmpty(_firstPreferredShadowMismatch))
            return;

        _firstPreferredShadowMismatch =
            "source=" + source +
            " reference=" + FormatPreferredTuple(referenceEnemyWork.preferred) +
            " shadow=" + LadyBugMonsterPreferenceSystem.FormatTuple(candidate);
    }

    private void AddPreferredShadowSource(string source)
    {
        if (!_preferredShadowSourceCounts.TryGetValue(source, out int count))
            count = 0;

        _preferredShadowSourceCounts[source] = count + 1;
    }

    public string BuildPreferredShadowDiagnosticSummary()
    {
        if (_preferredShadowChecks == 0)
            return "preferred[] shadow model: no checks were run";

        var builder = new StringBuilder();

        builder.Append("preferred[] shadow model checks=");
        builder.Append(_preferredShadowChecks);
        builder.Append(", matches=");
        builder.Append(_preferredShadowMatches);
        builder.Append(", mismatches=");
        builder.Append(_preferredShadowMismatches);

        if (!string.IsNullOrEmpty(_firstPreferredShadowMismatch))
        {
            builder.Append(", first mismatch: ");
            builder.Append(_firstPreferredShadowMismatch);
        }

        builder.Append(", sources: ");
        bool first = true;
        foreach (KeyValuePair<string, int> pair in _preferredShadowSourceCounts)
        {
            if (!first)
                builder.Append("; ");

            first = false;
            builder.Append(pair.Key);
            builder.Append("=");
            builder.Append(pair.Value);
        }

        return builder.ToString();
    }

    private static bool PreferredTupleEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count < 4 || b.Count < 4)
            return false;

        return ((a[0] & 0x0F) == (b[0] & 0x0F)) &&
               ((a[1] & 0x0F) == (b[1] & 0x0F)) &&
               ((a[2] & 0x0F) == (b[2] & 0x0F)) &&
               ((a[3] & 0x0F) == (b[3] & 0x0F));
    }

    private static string FormatPreferredTuple(IReadOnlyList<int> preferred)
    {
        if (preferred.Count < 4)
            return "[missing]";

        return "[" +
               (preferred[0] & 0x0F).ToString("X2") + "," +
               (preferred[1] & 0x0F).ToString("X2") + "," +
               (preferred[2] & 0x0F).ToString("X2") + "," +
               (preferred[3] & 0x0F).ToString("X2") + "]";
    }

    private void UpdatePreferredDirectionCandidate(
        SimulationEnemyWorkState enemyWork,
        SimulationActorState activeEnemy)
    {
        EnsurePreferredCount(enemyWork, 4);

        if (enemyWork.RejectedMask != 0)
        {
            // First observed fallback case after deriving rejectedMask:
            // MAME writes the rejected candidate 02 into preferred[2] and preferred[3].
            _stableEnemyWorkCandidateTicks = 0;
            enemyWork.Preferred[2] = enemyWork.RejectedMask;
            enemyWork.Preferred[3] = enemyWork.RejectedMask;
            return;
        }

        _stableEnemyWorkCandidateTicks++;

        int primaryChaseDirection = DerivePrimaryChaseDirection(activeEnemy);

        if (_stableEnemyWorkCandidateTicks == 3)
        {
            // The current trace exposes a one-tick rotated-preference pulse:
            // tick 6/7 : 04,04,04,04
            // tick 8   : 02,02,02,01
            // tick 9   : 04,... again
            //
            // So apply the 0x2E5C-style rotated shape only for the first observed
            // pulse after the fallback has been stable for a few ticks.
            ApplyRotatedBasePreferredDirections(enemyWork, primaryChaseDirection);
            return;
        }

        FillPreferredDirections(enemyWork, primaryChaseDirection);
    }

    private static void FillPreferredDirections(SimulationEnemyWorkState enemyWork, int direction)
    {
        EnsurePreferredCount(enemyWork, 4);

        for (int i = 0; i < 4; i++)
            enemyWork.Preferred[i] = direction;
    }

    private static void ApplyRotatedBasePreferredDirections(SimulationEnemyWorkState enemyWork, int sourceDirection)
    {
        EnsurePreferredCount(enemyWork, 4);

        int firstRotatedDirection = RotateDirectionRight4(sourceDirection);
        int secondRotatedDirection = RotateDirectionRight4(firstRotatedDirection);

        // Observed sequence for source direction 04 at tick 8:
        // preferred[0] = 02
        // preferred[1] = 02
        // preferred[2] = 02
        // preferred[3] = 01
        //
        // So this one-tick rotated-preference pulse writes the first rotated direction
        // into the first three slots, then the second rotated direction into the tail.
        // This remains a narrow reconstruction of the currently observed trace, not
        // the final full 0x2E5C preference generator.
        enemyWork.Preferred[0] = firstRotatedDirection;
        enemyWork.Preferred[1] = firstRotatedDirection;
        enemyWork.Preferred[2] = firstRotatedDirection;
        enemyWork.Preferred[3] = secondRotatedDirection;
    }

    private static int RotateDirectionRight4(int direction)
    {
        int shifted = (direction >> 1) & 0x0F;

        if ((direction & 0x01) != 0)
            shifted |= 0x08;

        return shifted & 0x0F;
    }

    private int DerivePrimaryChaseDirection(SimulationActorState activeEnemy)
    {
        if (Player == null)
            return EnemyWorkSafeDirection(activeEnemy);

        if (Player.X > activeEnemy.X)
            return 0x04;

        if (Player.X < activeEnemy.X)
            return 0x01;

        if (Player.Y > activeEnemy.Y)
            return 0x08;

        if (Player.Y < activeEnemy.Y)
            return 0x02;

        return EnemyWorkSafeDirection(activeEnemy);
    }

    private static int EnemyWorkSafeDirection(SimulationActorState activeEnemy)
    {
        return TryParseTraceByte(activeEnemy.Direction, out int directionValue)
            ? directionValue
            : 0;
    }

    private static void EnsurePreferredCount(SimulationEnemyWorkState enemyWork, int count)
    {
        while (enemyWork.Preferred.Count < count)
            enemyWork.Preferred.Add(0);
    }

    /// <summary>
    /// Partial rejectedMask model.
    ///
    /// The real arcade code rejects directions while testing preferred candidates
    /// against maze/door constraints. Until preferred[] is generated locally, this
    /// method still uses the reference preferred[0] as evidence for the attempted
    /// candidate.
    /// </summary>
    private static int DeriveRejectedMaskCandidate(
        int previousTempDir,
        int currentTempDir,
        int previousTempX,
        int previousTempY,
        int previousRejectedMask,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        // Better 61C1 model:
        //
        // 61C1 is the direction candidate that was tried and rejected at a decision
        // center, not simply "the previous direction when direction changes".
        //
        // We do not simulate preferred[] yet. For now, while preferred[] is filtered,
        // use the reference preferred[0] as the candidate that the arcade tried at
        // the center before the movement step. This matches the observed cases:
        //
        // - tick 21  : previous center (58,96), preferred[0]=01, final=01 -> C1=00
        // - tick 37  : previous center (48,96), preferred[0]=04, final=04 -> C1=00
        // - tick 69  : previous center (68,96), preferred[0]=04, final=01 -> C1=04
        // - tick 101 : previous center (48,96), preferred[0]=01, final=02 -> C1=01
        // - tick 245 : previous center (58,76), preferred[0]=08, final=01 -> C1=08
        if (IsAtDecisionCenter(previousTempX, previousTempY))
        {
            int preferred0 = GetReferencePreferredDirection(referenceEnemyWork, 0);

            if (IsDirectionBit(preferred0))
            {
                if (preferred0 == currentTempDir)
                    return 0;

                // Newly observed case:
                // tick 373 : previous=08, preferred[0]=02, final=08, MAME C1=00.
                //
                // This looks like an ignored reverse preference while the enemy keeps
                // moving in the same direction. Do not mark that as rejected.
                if (previousTempDir == currentTempDir &&
                    AreOppositeDirections(preferred0, currentTempDir))
                {
                    return 0;
                }

                int rejectedMask = preferred0;

                // Some decision-center fallbacks reject more than one candidate.
                // Observed case:
                // tick 309 : previousTempDir=02, preferred[0]=01, final=08,
                //            MAME rejectedMask=03.
                //
                // So if preferred[0] fails and the previous movement direction was
                // also not the final direction, keep it in the rejected mask too.
                // This still preserves earlier validated cases:
                // - tick 245: previous=01, preferred[0]=08, final=01 -> C1=08
                // - tick 277: preferred[0]=08, final=08 -> C1=00
                if (IsDirectionBit(previousTempDir) && previousTempDir != currentTempDir)
                    rejectedMask |= previousTempDir;

                return rejectedMask & 0x0F;
            }
        }

        // Safety fallback for the first validated release case, but only when no
        // usable preferred[0] candidate was available at a decision center.
        //
        // This must not run after a valid preferred[0] matched the final direction.
        // Example from the current trace:
        // tick 277 : previous center (48,66), preferred[0]=08, final=08 -> C1=00.
        if (previousTempDir == 0x02 && currentTempDir == 0x08)
            return 0x02;

        return 0;
    }

    private static bool IsAtDecisionCenter(int x, int y)
    {
        return (x & 0x0F) == 0x08 && (y & 0x0F) == 0x06;
    }

    private static bool AreOppositeDirections(int a, int b)
    {
        return (a == 0x01 && b == 0x04) ||
               (a == 0x04 && b == 0x01) ||
               (a == 0x02 && b == 0x08) ||
               (a == 0x08 && b == 0x02);
    }

    private static int GetReferencePreferredDirection(EnemyTraceEnemyWorkState? enemyWork, int index)
    {
        if (enemyWork == null)
            return 0;

        if (index < 0 || index >= enemyWork.preferred.Count)
            return 0;

        return enemyWork.preferred[index];
    }

    private static bool IsDirectionBit(int value)
    {
        return value is 0x01 or 0x02 or 0x04 or 0x08;
    }

    /// <summary>
    /// Selects which enemy slot owns the current shared EnemyWork scratch tuple.
    ///
    /// With one enemy, the first active slot is sufficient. With multiple active
    /// enemies, EnemyWork may belong to whichever slot the arcade routine is
    /// processing, so we match tempDir/tempX/tempY when possible.
    /// </summary>
    private static EnemyTraceActor? SelectReferenceEnemyForEnemyWork(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
            return null;

        // EnemyWork is a scratch area for the enemy currently being processed.
        // With one active enemy, this is usually the first active slot. Once a second
        // enemy is released, the current scratch state may refer to another slot.
        //
        // The reference trace already contains the scratch temp tuple. Use it only
        // to select which reference slot owns the scratch state; the simulation still
        // builds tempDir/tempX/tempY from the selected simulated slot.
        EnemyTraceEnemyWorkState? enemyWork = referenceFrame.enemyWork;
        if (enemyWork != null)
        {
            foreach (EnemyTraceActor enemy in referenceFrame.enemies)
            {
                if (!enemy.active)
                    continue;

                if (!TryParseTraceByte(enemy.dir, out int directionValue))
                    continue;

                if (directionValue == enemyWork.tempDir &&
                    enemy.x == enemyWork.tempX &&
                    enemy.y == enemyWork.tempY)
                {
                    return enemy;
                }
            }
        }

        return FindFirstActiveReferenceEnemy(referenceFrame);
    }

    private static EnemyTraceActor? FindFirstActiveReferenceEnemy(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in referenceFrame.enemies)
        {
            if (enemy.active)
                return enemy;
        }

        return null;
    }

    private SimulationActorState? FindEnemyBySlot(int slot)
    {
        foreach (SimulationActorState enemy in Enemies)
        {
            if (enemy.Slot == slot)
                return enemy;
        }

        return null;
    }

    private static void MoveActorOnePixel(SimulationActorState actor, string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return;

        if (!TryParseTraceByte(direction, out int directionValue))
            return;

        switch (directionValue)
        {
            case 0x01: // left
                actor.X = (actor.X - 1) & 0xFF;
                break;

            case 0x02: // down in arcade visual space; MAME actor Y decreases
                actor.Y = (actor.Y - 1) & 0xFF;
                break;

            case 0x04: // right
                actor.X = (actor.X + 1) & 0xFF;
                break;

            case 0x08: // up in arcade visual space; MAME actor Y increases
                actor.Y = (actor.Y + 1) & 0xFF;
                break;
        }
    }

    private static bool TryParseTraceByte(string? text, out int value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();

        if (trimmed.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            return int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value);

        bool looksHex = trimmed.Length <= 2;
        foreach (char c in trimmed)
        {
            if (!System.Uri.IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
        }

        if (looksHex)
            return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out value);

        return int.TryParse(trimmed, out value);
    }

    private static void CopyActorInto(EnemyTraceActor source, SimulationActorState destination)
    {
        destination.Slot = source.slot;
        destination.Raw = source.raw;
        destination.X = source.x;
        destination.Y = source.y;
        destination.Direction = source.dir;
        destination.Active = source.active;
    }

    public SimulationFrame BuildFrame(int frameIndex, EnemyTraceFrame referenceFrame)
    {
        var frame = new SimulationFrame
        {
            FrameIndex = frameIndex,
            Tick = referenceFrame.frame,
            Schema = referenceFrame.schema,
            Phase = referenceFrame.phase,
            MameFrame = referenceFrame.mameFrame,
            Pc = referenceFrame.pc,
            R = referenceFrame.r,
            Player = Player == null ? null : CloneActor(Player),
            EnemyWork = EnemyWork == null ? null : CloneEnemyWork(EnemyWork),
            Timers = Timers == null ? null : CloneTimers(Timers),
            Ports = Ports == null ? null : ClonePorts(Ports)
        };

        foreach (SimulationActorState enemy in Enemies)
            frame.Enemies.Add(CloneActor(enemy));

        foreach (SimulationGateState gate in Gates)
            frame.Gates.Add(CloneGate(gate));

        return frame;
    }

    private static SimulationGateState CopyGate(EnemyTraceGateState gate)
    {
        return new SimulationGateState
        {
            GateId = gate.gate_id,
            Orientation = gate.orientation,
            PivotX = gate.pivot_x,
            PivotY = gate.pivot_y
        };
    }

    private static SimulationTimersState CopyTimers(EnemyTraceTimersState timers)
    {
        return new SimulationTimersState
        {
            Timer61B4 = timers.timer61B4,
            Timer61B5 = timers.timer61B5,
            Timer61B6 = timers.timer61B6,
            Timer61B7 = timers.timer61B7,
            Timer61B8 = timers.timer61B8,
            Timer61B9 = timers.timer61B9,
            Freeze61E1 = timers.freeze61E1,
            CollectibleColorCounter6199 = timers.collectibleColorCounter6199
        };
    }

    private static SimulationActorState CopyActor(EnemyTraceActor actor)
    {
        return new SimulationActorState
        {
            Slot = actor.slot,
            Raw = actor.raw,
            X = actor.x,
            Y = actor.y,
            Direction = actor.dir,
            Active = actor.active
        };
    }

    private static SimulationPortsState CopyPorts(EnemyTracePortsState ports)
    {
        return new SimulationPortsState
        {
            In0_9000 = ports.in0_9000,
            In1_9001 = ports.in1_9001,
            Dsw0_9002 = ports.dsw0_9002,
            Dsw1_9003 = ports.dsw1_9003
        };
    }

    private static SimulationActorState CloneActor(SimulationActorState actor)
    {
        return new SimulationActorState
        {
            Slot = actor.Slot,
            Raw = actor.Raw,
            X = actor.X,
            Y = actor.Y,
            Direction = actor.Direction,
            Active = actor.Active
        };
    }

    private static SimulationGateState CloneGate(SimulationGateState gate)
    {
        return new SimulationGateState
        {
            GateId = gate.GateId,
            Orientation = gate.Orientation,
            PivotX = gate.PivotX,
            PivotY = gate.PivotY
        };
    }

    private static SimulationEnemyWorkState CloneEnemyWork(SimulationEnemyWorkState enemyWork)
    {
        var clone = new SimulationEnemyWorkState
        {
            TempDir = enemyWork.TempDir,
            TempX = enemyWork.TempX,
            TempY = enemyWork.TempY,
            RejectedMask = enemyWork.RejectedMask,
            FallbackMask = enemyWork.FallbackMask,
            ChaseRoundRobin = enemyWork.ChaseRoundRobin,
            PreferredShadowSource = enemyWork.PreferredShadowSource
        };

        clone.Preferred.AddRange(enemyWork.Preferred);
        clone.PreferredShadow.AddRange(enemyWork.PreferredShadow);
        clone.ChaseTimers.AddRange(enemyWork.ChaseTimers);
        return clone;
    }

    private static SimulationTimersState CloneTimers(SimulationTimersState timers)
    {
        return new SimulationTimersState
        {
            Timer61B4 = timers.Timer61B4,
            Timer61B5 = timers.Timer61B5,
            Timer61B6 = timers.Timer61B6,
            Timer61B7 = timers.Timer61B7,
            Timer61B8 = timers.Timer61B8,
            Timer61B9 = timers.Timer61B9,
            Freeze61E1 = timers.Freeze61E1,
            CollectibleColorCounter6199 = timers.CollectibleColorCounter6199
        };
    }

    private static SimulationPortsState ClonePorts(SimulationPortsState ports)
    {
        return new SimulationPortsState
        {
            In0_9000 = ports.In0_9000,
            In1_9001 = ports.In1_9001,
            Dsw0_9002 = ports.Dsw0_9002,
            Dsw1_9003 = ports.Dsw1_9003
        };
    }
}
