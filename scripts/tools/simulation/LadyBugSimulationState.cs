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
    private readonly HashSet<int> _previousReferenceActiveSlots = new();
    private int _referenceEnemyActivationEvents;
    private int _denExitCandidateEvents;
    private string _firstDenExitCandidate = string.Empty;
    private int _referenceRejectedMaskSyncs;
    private int _referenceFallbackMaskSyncs;
    private int _rejectedMaskShadowChecks;
    private int _rejectedMaskShadowMatches;
    private int _rejectedMaskShadowMismatches;
    private string _firstRejectedMaskShadowMismatch = string.Empty;
    private readonly Dictionary<string, int> _rejectedMaskShadowSourceCounts = new();
    private int _fallbackHelperShadowChecks;
    private int _fallbackHelperShadowMatches;
    private int _fallbackHelperShadowMismatches;
    private string _firstFallbackHelperShadowMismatch = string.Empty;
    private readonly Dictionary<string, int> _fallbackHelperShadowSourceCounts = new();


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
        TrackReferenceEnemyActivationDiagnostics(referenceFrame);
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

    /// <summary>
    /// Diagnostic-only detector for frames where a reference enemy slot becomes active.
    ///
    /// The den-release sequence is not normal free-roaming movement: the enemy can
    /// appear in a constrained corridor and be forced upward while rejected/fallback
    /// scratch values take special-looking shapes. This detector does not change
    /// simulation state; it only adds context to the adapter summary.
    /// </summary>
    private void TrackReferenceEnemyActivationDiagnostics(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
        {
            _previousReferenceActiveSlots.Clear();
            return;
        }

        var currentActiveSlots = new HashSet<int>();

        foreach (EnemyTraceActor referenceEnemy in referenceFrame.enemies)
        {
            if (!referenceEnemy.active)
                continue;

            currentActiveSlots.Add(referenceEnemy.slot);

            if (_previousReferenceActiveSlots.Contains(referenceEnemy.slot))
                continue;

            _referenceEnemyActivationEvents++;

            if (IsDenExitCandidate(referenceFrame, referenceEnemy))
            {
                _denExitCandidateEvents++;

                if (string.IsNullOrEmpty(_firstDenExitCandidate))
                    _firstDenExitCandidate = BuildDenExitCandidateDiagnostic(referenceFrame, referenceEnemy);
            }
        }

        _previousReferenceActiveSlots.Clear();
        foreach (int slot in currentActiveSlots)
            _previousReferenceActiveSlots.Add(slot);
    }

    private static bool IsDenExitCandidate(EnemyTraceFrame referenceFrame, EnemyTraceActor referenceEnemy)
    {
        if (referenceFrame.enemyWork == null)
            return false;

        if (!TryParseTraceByte(referenceEnemy.dir, out int directionValue))
            return false;

        EnemyTraceEnemyWorkState enemyWork = referenceFrame.enemyWork;

        bool tempMatchesActiveEnemy =
            enemyWork.tempDir == directionValue &&
            enemyWork.tempX == referenceEnemy.x &&
            enemyWork.tempY == referenceEnemy.y;

        bool forcedUpShape = directionValue == 0x08;
        bool constrainedScratch = enemyWork.rejectedMask != 0;

        return tempMatchesActiveEnemy && forcedUpShape && constrainedScratch;
    }

    private static string BuildDenExitCandidateDiagnostic(
        EnemyTraceFrame referenceFrame,
        EnemyTraceActor referenceEnemy)
    {
        EnemyTraceEnemyWorkState? enemyWork = referenceFrame.enemyWork;

        return "tick=" + referenceFrame.frame +
               " mameFrame=" + referenceFrame.mameFrame +
               " pc=" + referenceFrame.pc +
               " r=" + referenceFrame.r +
               " slot=" + referenceEnemy.slot +
               " raw=" + FormatByte(referenceEnemy.raw) +
               " enemyXY=(" + FormatByte(referenceEnemy.x) + "," + FormatByte(referenceEnemy.y) + ")" +
               " enemyDir=" + referenceEnemy.dir +
               " tempDir=" + FormatByte(enemyWork?.tempDir ?? 0) +
               " tempX=" + FormatByte(enemyWork?.tempX ?? 0) +
               " tempY=" + FormatByte(enemyWork?.tempY ?? 0) +
               " rejectedMask=" + FormatByte(enemyWork?.rejectedMask ?? 0) +
               " preferred=" + (enemyWork == null ? "[missing]" : FormatPreferredTuple(enemyWork.preferred)) +
               " activeEnemies=" + CountActiveReferenceEnemies(referenceFrame);
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

        int rejectedMaskShadow = DeriveRejectedMaskCandidate(
            previousTempDir,
            directionValue,
            previousTempX,
            previousTempY,
            previousRejectedMask,
            referenceFrame.enemyWork,
            out string rejectedMaskShadowSource);

        EnemyWork.RejectedMask = rejectedMaskShadow;
        EnemyWork.RejectedMaskShadow = rejectedMaskShadow;
        EnemyWork.RejectedMaskShadowSource = rejectedMaskShadowSource;
        UpdateRejectedMaskShadowDiagnostics(
            rejectedMaskShadow,
            rejectedMaskShadowSource,
            referenceFrame,
            referenceFrame.enemyWork);

        int fallbackHelperShadow = DeriveFallbackHelperCandidate(
            referenceFrame.enemyWork,
            out string fallbackHelperShadowSource);

        EnemyWork.FallbackHelperShadow = fallbackHelperShadow;
        EnemyWork.FallbackHelperShadowSource = fallbackHelperShadowSource;
        UpdateFallbackHelperShadowDiagnostics(
            fallbackHelperShadow,
            fallbackHelperShadowSource,
            referenceFrame,
            referenceFrame.enemyWork);

        SyncReferenceRejectedMaskState(EnemyWork, referenceFrame.enemyWork);
        SyncReferenceFallbackState(EnemyWork, referenceFrame.enemyWork);

        if (!SyncReferencePreferredState(EnemyWork, referenceFrame.enemyWork))
            UpdatePreferredDirectionCandidate(EnemyWork, simulatedEnemy);

        SyncReferenceChaseState(EnemyWork, referenceFrame.enemyWork);
        UpdatePreferredShadowDiagnostics(EnemyWork, referenceFrame, referenceFrame.enemyWork);
    }

    /// <summary>
    /// Shadow diagnostic for 0x61C1 / EnemyRejectedDirMask.
    ///
    /// This does not drive the comparison yet. The adapter still syncs the
    /// authoritative RejectedMask value from MAME immediately after this check.
    /// The shadow path records whether the local decision-center heuristic can
    /// explain the end-of-frame rejectedMask seen in the standard JSONL trace.
    /// </summary>
    private void UpdateRejectedMaskShadowDiagnostics(
        int rejectedMaskShadow,
        string source,
        EnemyTraceFrame referenceFrame,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        if (referenceEnemyWork == null)
            return;

        _rejectedMaskShadowChecks++;
        AddRejectedMaskShadowSource(source);

        int referenceRejectedMask = referenceEnemyWork.rejectedMask & 0x0F;
        int candidate = rejectedMaskShadow & 0x0F;

        if (candidate == referenceRejectedMask)
        {
            _rejectedMaskShadowMatches++;
            return;
        }

        _rejectedMaskShadowMismatches++;

        if (!string.IsNullOrEmpty(_firstRejectedMaskShadowMismatch))
            return;

        _firstRejectedMaskShadowMismatch =
            "tick=" + referenceFrame.frame +
            " mameFrame=" + referenceFrame.mameFrame +
            " pc=" + referenceFrame.pc +
            " r=" + referenceFrame.r +
            " activeEnemies=" + CountActiveReferenceEnemies(referenceFrame) +
            " tempDir=" + FormatByte(referenceEnemyWork.tempDir) +
            " tempX=" + FormatByte(referenceEnemyWork.tempX) +
            " tempY=" + FormatByte(referenceEnemyWork.tempY) +
            " source=" + source +
            " reference=" + FormatByte(referenceRejectedMask) +
            " shadow=" + FormatByte(candidate) +
            " preferred=" + FormatPreferredTuple(referenceEnemyWork.preferred);
    }

    private void AddRejectedMaskShadowSource(string source)
    {
        if (!_rejectedMaskShadowSourceCounts.TryGetValue(source, out int count))
            count = 0;

        _rejectedMaskShadowSourceCounts[source] = count + 1;
    }


    /// <summary>
    /// Shadow diagnostic for 0x61C2.
    ///
    /// The old DTO name is FallbackMask, but exact-PC traces show that 0x61C2 behaves
    /// like a fallback step counter/helper. In the current one-enemy standard JSONL
    /// traces, the end-of-cycle value is expected to be 01: 42CF resets it, then the
    /// fallback-step loop increments/reads it once around 43C4/43C5.
    ///
    /// This does not drive the simulation yet. The authoritative FallbackMask field
    /// is still synced from MAME immediately after this shadow check.
    /// </summary>
    private void UpdateFallbackHelperShadowDiagnostics(
        int fallbackHelperShadow,
        string source,
        EnemyTraceFrame referenceFrame,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        if (referenceEnemyWork == null)
            return;

        _fallbackHelperShadowChecks++;
        AddFallbackHelperShadowSource(source);

        int referenceFallbackHelper = referenceEnemyWork.fallbackMask & 0xFF;
        int candidate = fallbackHelperShadow & 0xFF;

        if (candidate == referenceFallbackHelper)
        {
            _fallbackHelperShadowMatches++;
            return;
        }

        _fallbackHelperShadowMismatches++;

        if (!string.IsNullOrEmpty(_firstFallbackHelperShadowMismatch))
            return;

        _firstFallbackHelperShadowMismatch =
            "tick=" + referenceFrame.frame +
            " mameFrame=" + referenceFrame.mameFrame +
            " pc=" + referenceFrame.pc +
            " r=" + referenceFrame.r +
            " activeEnemies=" + CountActiveReferenceEnemies(referenceFrame) +
            " tempDir=" + FormatByte(referenceEnemyWork.tempDir) +
            " tempX=" + FormatByte(referenceEnemyWork.tempX) +
            " tempY=" + FormatByte(referenceEnemyWork.tempY) +
            " source=" + source +
            " reference=" + FormatByte(referenceFallbackHelper) +
            " shadow=" + FormatByte(candidate) +
            " rejectedMask=" + FormatByte(referenceEnemyWork.rejectedMask) +
            " preferred=" + FormatPreferredTuple(referenceEnemyWork.preferred);
    }

    private void AddFallbackHelperShadowSource(string source)
    {
        if (!_fallbackHelperShadowSourceCounts.TryGetValue(source, out int count))
            count = 0;

        _fallbackHelperShadowSourceCounts[source] = count + 1;
    }

    /// <summary>
    /// Temporary bridge for rejectedMask.
    ///
    /// rejectedMask is a short-lived scratch field produced by the direction
    /// validation / wall rejection logic. The current adapter does not yet fully
    /// simulate that decision pipeline, and the den-exit trace showed that a
    /// partial rejectedMask model can create a few misleading mismatches even
    /// when position, direction, fallback, and preferred[] all match MAME.
    ///
    /// Until the real rejection/fallback generator is implemented, keep this
    /// scratch field aligned with the reference trace, like fallback helper,
    /// preferred[], and chase state.
    /// </summary>
    private void SyncReferenceRejectedMaskState(
        SimulationEnemyWorkState enemyWork,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        if (referenceEnemyWork == null)
            return;

        if (enemyWork.RejectedMask != referenceEnemyWork.rejectedMask)
            _referenceRejectedMaskSyncs++;

        enemyWork.RejectedMask = referenceEnemyWork.rejectedMask;
    }

    /// <summary>
    /// Temporary bridge for the fallback helper at 0x61C2.
    ///
    /// The source string already describes fallback as reference-synced. In the
    /// den-exit trace this value is stable at 01 while the simulation still held
    /// the initial B0, creating hundreds of unhelpful mismatches unrelated to
    /// preferred[] or pixel movement. Until the fallback generator is implemented,
    /// keep it aligned with MAME just like preferred[] and chase timers.
    /// </summary>
    private void SyncReferenceFallbackState(
        SimulationEnemyWorkState enemyWork,
        EnemyTraceEnemyWorkState? referenceEnemyWork)
    {
        if (referenceEnemyWork == null)
            return;

        if (enemyWork.FallbackMask != referenceEnemyWork.fallbackMask)
            _referenceFallbackMaskSyncs++;

        enemyWork.FallbackMask = referenceEnemyWork.fallbackMask;
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
        EnemyTraceFrame referenceFrame,
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

            RegisterPreferredShadowMismatch(referenceFrame, referenceEnemyWork, source, candidate);
            return;
        }

        enemyWork.PreferredShadow.Clear();
        enemyWork.PreferredShadowSource = "unclassified";
        AddPreferredShadowSource("unclassified");
        RegisterPreferredShadowMismatch(referenceFrame, referenceEnemyWork, "unclassified", new[] { 0, 0, 0, 0 });
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

        // The previous shadow classifier only recognized the rotate branch when
        // PLAYER_DIR_CURRENT was 08, because that was the first validated trace.
        // A den-wait / den-exit trace with the player facing 04 exposed the same
        // 2E97 rotate branch as [02,01,08,04].  Recognize all four direction seeds
        // here; this is still a classifier over end-of-frame JSONL tuples, not the
        // final PC-exact branch selector.
        foreach (int rotateSeed in PreferredRotateSeeds())
        {
            int[] rotateCandidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(rotateSeed);
            string rotateSource = "2E97_ROTATE_FROM_" + FormatNibbleDirection(rotateSeed);

            if (PreferredTupleEquals(rotateCandidate, referencePreferred))
            {
                candidate = rotateCandidate;
                source = rotateSource;
                return true;
            }
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
        foreach (int rotateSeed in PreferredRotateSeeds())
        {
            int[] rotateCandidate = LadyBugMonsterPreferenceSystem.GenerateRotateBranch(rotateSeed);
            string rotateSource = "2E97_ROTATE_FROM_" + FormatNibbleDirection(rotateSeed);

            if (TryBuildSlot0OverrideCandidate(referencePreferred, rotateCandidate, rotateSource, out candidate, out source))
                return true;
        }

        for (int rLow = 0; rLow < 16; rLow++)
        {
            int[] randomCandidate = LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow(rLow);

            if (TryBuildSlot0OverrideCandidate(referencePreferred, randomCandidate, "2EC7_RANDOM_RLOW_" + rLow.ToString("X1"), out candidate, out source))
                return true;
        }

        return false;
    }

    private static IEnumerable<int> PreferredRotateSeeds()
    {
        yield return LadyBugMonsterPreferenceSystem.DirLeft;
        yield return LadyBugMonsterPreferenceSystem.DirUp;
        yield return LadyBugMonsterPreferenceSystem.DirRight;
        yield return LadyBugMonsterPreferenceSystem.DirDown;
    }

    private static string FormatNibbleDirection(int direction)
    {
        return (direction & 0x0F).ToString("X2");
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
        EnemyTraceFrame referenceFrame,
        EnemyTraceEnemyWorkState referenceEnemyWork,
        string source,
        int[] candidate)
    {
        _preferredShadowMismatches++;

        if (!string.IsNullOrEmpty(_firstPreferredShadowMismatch))
            return;

        _firstPreferredShadowMismatch =
            "tick=" + referenceFrame.frame +
            " mameFrame=" + referenceFrame.mameFrame +
            " pc=" + referenceFrame.pc +
            " r=" + referenceFrame.r +
            " activeEnemies=" + CountActiveReferenceEnemies(referenceFrame) +
            " tempDir=" + FormatByte(referenceEnemyWork.tempDir) +
            " tempX=" + FormatByte(referenceEnemyWork.tempX) +
            " tempY=" + FormatByte(referenceEnemyWork.tempY) +
            " chaseTimers=" + FormatChaseTimers(referenceEnemyWork) +
            " chaseRoundRobin=" + FormatByte(referenceEnemyWork.chaseRoundRobin) +
            " source=" + source +
            " reference=" + FormatPreferredTuple(referenceEnemyWork.preferred) +
            " shadow=" + LadyBugMonsterPreferenceSystem.FormatTuple(candidate);
    }

    private static int CountActiveReferenceEnemies(EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in referenceFrame.enemies)
        {
            if (enemy.active)
                count++;
        }

        return count;
    }

    private static string FormatChaseTimers(EnemyTraceEnemyWorkState enemyWork)
    {
        if (enemyWork.chaseTimers.Count == 0)
            return "[]";

        var builder = new StringBuilder();
        builder.Append("[");

        for (int i = 0; i < enemyWork.chaseTimers.Count; i++)
        {
            if (i > 0)
                builder.Append(",");

            builder.Append(FormatByte(enemyWork.chaseTimers[i]));
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }

    private void AddPreferredShadowSource(string source)
    {
        if (!_preferredShadowSourceCounts.TryGetValue(source, out int count))
            count = 0;

        _preferredShadowSourceCounts[source] = count + 1;
    }

    public string BuildPreferredShadowDiagnosticSummary()
    {
        var builder = new StringBuilder();

        if (_preferredShadowChecks == 0)
        {
            builder.Append("preferred[] shadow model: no checks were run");
            AppendRejectedMaskShadowDiagnosticSummary(builder);
            AppendFallbackHelperShadowDiagnosticSummary(builder);
            return builder.ToString();
        }

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

        if (_referenceEnemyActivationEvents > 0)
        {
            builder.Append(", enemy activations=");
            builder.Append(_referenceEnemyActivationEvents);
            builder.Append(", den-exit candidates=");
            builder.Append(_denExitCandidateEvents);

            if (!string.IsNullOrEmpty(_firstDenExitCandidate))
            {
                builder.Append(", first den-exit candidate: ");
                builder.Append(_firstDenExitCandidate);
            }
        }

        if (_referenceRejectedMaskSyncs > 0)
        {
            builder.Append(", reference rejectedMask syncs=");
            builder.Append(_referenceRejectedMaskSyncs);
        }

        if (_referenceFallbackMaskSyncs > 0)
        {
            builder.Append(", reference fallback helper syncs=");
            builder.Append(_referenceFallbackMaskSyncs);
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

        AppendRejectedMaskShadowDiagnosticSummary(builder);
        AppendFallbackHelperShadowDiagnosticSummary(builder);

        return builder.ToString();
    }

    private void AppendRejectedMaskShadowDiagnosticSummary(StringBuilder builder)
    {
        if (_rejectedMaskShadowChecks == 0)
        {
            builder.Append("; rejectedMask shadow model: no checks were run");
            return;
        }

        builder.Append("; rejectedMask shadow model checks=");
        builder.Append(_rejectedMaskShadowChecks);
        builder.Append(", matches=");
        builder.Append(_rejectedMaskShadowMatches);
        builder.Append(", mismatches=");
        builder.Append(_rejectedMaskShadowMismatches);

        if (!string.IsNullOrEmpty(_firstRejectedMaskShadowMismatch))
        {
            builder.Append(", first mismatch: ");
            builder.Append(_firstRejectedMaskShadowMismatch);
        }

        builder.Append(", sources: ");
        bool first = true;
        foreach (KeyValuePair<string, int> pair in _rejectedMaskShadowSourceCounts)
        {
            if (!first)
                builder.Append("; ");

            first = false;
            builder.Append(pair.Key);
            builder.Append("=");
            builder.Append(pair.Value);
        }
    }

    private void AppendFallbackHelperShadowDiagnosticSummary(StringBuilder builder)
    {
        if (_fallbackHelperShadowChecks == 0)
        {
            builder.Append("; fallback helper shadow model: no checks were run");
            return;
        }

        builder.Append("; fallback helper shadow model checks=");
        builder.Append(_fallbackHelperShadowChecks);
        builder.Append(", matches=");
        builder.Append(_fallbackHelperShadowMatches);
        builder.Append(", mismatches=");
        builder.Append(_fallbackHelperShadowMismatches);

        if (!string.IsNullOrEmpty(_firstFallbackHelperShadowMismatch))
        {
            builder.Append(", first mismatch: ");
            builder.Append(_firstFallbackHelperShadowMismatch);
        }

        builder.Append(", sources: ");
        bool first = true;
        foreach (KeyValuePair<string, int> pair in _fallbackHelperShadowSourceCounts)
        {
            if (!first)
                builder.Append("; ");

            first = false;
            builder.Append(pair.Key);
            builder.Append("=");
            builder.Append(pair.Value);
        }
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
    /// Partial fallback-helper shadow model for 0x61C2.
    ///
    /// In the validated exact-PC EnemyWork trace, 42CF resets 0x61C2 and the
    /// 43C4/43C5 step loop leaves it at 01 once per Enemy_UpdateOne cycle.
    /// The current standard JSONL traces capture that end-of-cycle value.
    ///
    /// This deliberately starts as a narrow classifier. If a future trace needs more
    /// than one fallback-step read, this shadow summary should expose the mismatch
    /// before the authoritative reference-sync is removed.
    /// </summary>
    private static int DeriveFallbackHelperCandidate(
        EnemyTraceEnemyWorkState? referenceEnemyWork,
        out string source)
    {
        if (referenceEnemyWork == null)
        {
            source = "NO_REFERENCE_ENEMYWORK";
            return 0;
        }

        source = "ONE_STEP_PER_ENEMY_UPDATE";
        return 0x01;
    }

    /// <summary>
    /// Partial rejectedMask shadow model.
    ///
    /// The real arcade code rejects directions while testing preferred candidates
    /// against maze/door constraints. Until preferred[] and the local maze checks
    /// are generated locally, this method still uses reference preferred[0] as the
    /// candidate that MAME appears to have tried at the decision center.
    /// </summary>
    private static int DeriveRejectedMaskCandidate(
        int previousTempDir,
        int currentTempDir,
        int previousTempX,
        int previousTempY,
        int previousRejectedMask,
        EnemyTraceEnemyWorkState? referenceEnemyWork,
        out string source)
    {
        if (referenceEnemyWork == null)
        {
            source = "NO_REFERENCE_ENEMYWORK";
            return 0;
        }

        // Better 61C1 model:
        //
        // 61C1 is the direction candidate that was tried and rejected at a decision
        // center, not simply "the previous direction when direction changes".
        //
        // We do not simulate preferred[] yet. For now, while preferred[] is filtered,
        // use the reference preferred[0] as the candidate that the arcade tried at
        // the center before the movement step. This matches the observed cases:
        //
        // - center candidate accepted       -> C1=00
        // - 4315 only                      -> candidate rejected, current direction kept
        // - 4315 -> 4331 -> 4241 fallback  -> candidate and temp direction rejected
        if (IsAtDecisionCenter(previousTempX, previousTempY))
        {
            int preferred0 = GetReferencePreferredDirection(referenceEnemyWork, 0);

            if (IsDirectionBit(preferred0))
            {
                if (preferred0 == currentTempDir)
                {
                    source = "DECISION_CENTER_PREFERRED_ACCEPTED";
                    return 0;
                }

                // Observed shape:
                // previous direction is kept while preferred[0] points to the
                // opposite direction. The arcade does not record that as a rejected
                // candidate in the current standard trace window.
                if (previousTempDir == currentTempDir &&
                    AreOppositeDirections(preferred0, currentTempDir))
                {
                    source = "DECISION_CENTER_REVERSE_IGNORED";
                    return 0;
                }

                int rejectedMask = preferred0;
                source = "DECISION_CENTER_REJECT_PREFERRED";

                // Some decision-center fallbacks reject more than one candidate.
                // Observed exact-PC pattern:
                // 4315 rejects preferred[0], then 4331 ORs the current temp dir
                // before entering 4241 fallback.
                if (IsDirectionBit(previousTempDir) && previousTempDir != currentTempDir)
                {
                    rejectedMask |= previousTempDir;
                    source = "DECISION_CENTER_REJECT_PREFERRED_AND_PREVIOUS";
                }

                return rejectedMask & 0x0F;
            }

            source = "DECISION_CENTER_NO_PREFERRED0";
            return 0;
        }

        // Safety fallback kept from the earlier narrow model, but made explicit as
        // a source-classified shadow case so it can be measured and removed later.
        if (previousTempDir == 0x02 && currentTempDir == 0x08)
        {
            source = "SAFETY_PREVIOUS_02_TO_08";
            return 0x02;
        }

        source = "PLAIN_STEP";
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
            PreferredShadowSource = enemyWork.PreferredShadowSource,
            RejectedMaskShadow = enemyWork.RejectedMaskShadow,
            RejectedMaskShadowSource = enemyWork.RejectedMaskShadowSource,
            FallbackHelperShadow = enemyWork.FallbackHelperShadow,
            FallbackHelperShadowSource = enemyWork.FallbackHelperShadowSource
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
