using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// v0.9.14c source-path single-enemy replay adapter.
///
/// Modeled preferred[] subset:
/// - 0x2E97 rotate tuples are generated and used;
/// - visible 0x477D BFS/chase overrides are generated from the source 0x45DC
///   coordinate-to-logical-maze guidance path and used when the context is safe;
/// - 0x2EC7 random base tuples remain trace-synced by design.
///
/// The actual enemy movement step remains the reconstructed source path:
///   0x427E -> 0x42E6 / 0x4325 / 0x4241 -> 0x43BA.
/// </summary>
public sealed class LadyBugSourcePathSingleEnemyReplayAdapter : IEnemySimulationAdapter
{
    private const string Version = "v0.9.14c";

    public string Name => "Lady Bug source-path single-enemy replay";

    public string Description =>
        "v0.9.14c: compute active mono-enemy movement with the source path; " +
        "use modeled 0x2E97 rotate preferred[] and visible 0x477D BFS/chase overrides from 0x45DC+0x6200 when safe; " +
        "keep random preferred[], release timing, player, gates, VRAM context and multi-enemy frames trace-synced.";

    public bool ExpectedToMismatch => false;

    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames == null || referenceFrames.Count == 0)
            return new SimulationAdapterResult(new List<SimulationFrame>(), "empty trace; no initial state created");

        var stats = new ReplayStats { Frames = referenceFrames.Count };
        var simulatedEnemies = CopyReferenceEnemies(referenceFrames[0]);
        var frames = new List<SimulationFrame>(referenceFrames.Count)
        {
            BuildFrame(referenceFrames[0], 0, simulatedEnemies, CopyEnemyWork(referenceFrames[0].enemyWork))
        };

        for (int i = 1; i < referenceFrames.Count; i++)
        {
            EnemyTraceFrame sourceFrame = referenceFrames[i - 1];
            EnemyTraceFrame resultFrame = referenceFrames[i];
            stats.Transitions++;

            int activeStart = CountActiveKnown(simulatedEnemies);
            int activeResult = CountActiveKnown(resultFrame.enemies);

            if (activeStart == 0)
            {
                if (activeResult == 0)
                    stats.NoActiveSyncTransitions++;
                else
                    stats.ReleaseSyncTransitions++;

                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            if (activeStart != 1 || activeResult != 1)
            {
                stats.MultiEnemySyncTransitions++;
                if (string.IsNullOrEmpty(stats.FirstMultiEnemySync))
                {
                    stats.FirstMultiEnemySync =
                        $"firstMultiEnemySync startIndex={i - 1} startTick={sourceFrame.frame} " +
                        $"resultIndex={i} resultTick={resultFrame.frame} activeStart={activeStart} activeResult={activeResult}";
                }

                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            SimulationActorState? startEnemy = FindActiveKnownSimulationEnemy(simulatedEnemies);
            EnemyTraceActor? referenceResultEnemy = FindActiveKnownReferenceEnemy(resultFrame.enemies);
            if (startEnemy == null || referenceResultEnemy == null)
            {
                stats.InvalidSingleEnemyShapeSyncTransitions++;
                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            if (startEnemy.Slot != referenceResultEnemy.slot)
            {
                stats.SlotChangedSyncTransitions++;
                if (string.IsNullOrEmpty(stats.FirstSlotChangeSync))
                {
                    stats.FirstSlotChangeSync =
                        $"firstSlotChangeSync startIndex={i - 1} startTick={sourceFrame.frame} " +
                        $"resultIndex={i} resultTick={resultFrame.frame} startSlot={startEnemy.Slot} resultSlot={referenceResultEnemy.slot}";
                }

                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            if (!LadyBugPreferredHybridProvider.TryGetPreferred(
                    sourceFrame,
                    resultFrame,
                    startEnemy.Slot,
                    startEnemy.X,
                    startEnemy.Y,
                    out LadyBugPreferredHybridProvider.Result preferredResult))
            {
                stats.MissingPreferredSyncTransitions++;
                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            stats.RecordPreferredProvider(preferredResult);
            int preferred = preferredResult.Preferred;

            if (!LadyBugMameLocalTile4130Oracle.TryCreate(resultFrame, out LadyBugMameLocalTile4130Oracle? localTileOracle) || localTileOracle == null)
            {
                stats.MissingVramSyncTransitions++;
                simulatedEnemies = CopyReferenceEnemies(resultFrame);
                frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, CopyEnemyWork(resultFrame.enemyWork)));
                continue;
            }

            SourceStepResult step = ComputeSourcePathStep(startEnemy, preferred, localTileOracle);
            stats.RecordModeledTransition(step);

            ApplyModeledEnemyState(simulatedEnemies, startEnemy.Slot, step);
            SyncInactiveAndMissingEnemySlotsFromReference(simulatedEnemies, resultFrame);

            SimulationEnemyWorkState modeledWork = BuildModeledEnemyWork(resultFrame.enemyWork, step, preferredResult);
            CompareModeledResult(i, resultFrame, referenceResultEnemy, step, modeledWork, stats);

            frames.Add(BuildFrame(resultFrame, i, simulatedEnemies, modeledWork));
        }

        return new SimulationAdapterResult(frames, BuildSummary(stats));
    }

    private static SourceStepResult ComputeSourcePathStep(
        SimulationActorState startEnemy,
        int preferred,
        LadyBugMameLocalTile4130Oracle localTileOracle)
    {
        int startDir = DirectionFromRaw(startEnemy.Raw);
        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = startDir,
            TempX = startEnemy.X & 0xFF,
            TempY = startEnemy.Y & 0xFF,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        LadyBugEnemyDecisionGate427EModel.Result gate =
            LadyBugEnemyDecisionGate427EModel.Evaluate(scratch.TempDir, scratch.TempX, scratch.TempY);

        string path;
        bool fallbackEntered = false;
        bool forcedReversal = false;
        int testedDirections = 0;

        if (gate.CarrySet)
        {
            testedDirections++;
            if (SourceAcceptsDirection(preferred, scratch.TempX, scratch.TempY, localTileOracle))
            {
                scratch.TempDir = preferred & 0x0F;
                path = "42E6_PREFERRED_ACCEPTED";
            }
            else
            {
                scratch.RejectedDirMask = (scratch.RejectedDirMask | preferred) & 0x0F;
                int current = scratch.TempDir & 0x0F;
                testedDirections++;

                if (SourceAcceptsDirection(current, scratch.TempX, scratch.TempY, localTileOracle))
                {
                    path = "4315_PREFERRED_REJECTED_CURRENT_KEPT";
                }
                else
                {
                    scratch.RejectedDirMask = (scratch.RejectedDirMask | current) & 0x0F;
                    fallbackEntered = true;
                    path = FindFallbackDirection(scratch, localTileOracle, ref testedDirections);
                }
            }
        }
        else
        {
            forcedReversal = LadyBugEnemyDecisionModel.CheckDoorForcedReversal(
                scratch.TempDir,
                scratch.TempX,
                scratch.TempY,
                localTileOracle.ReadTileAtProbe);

            if (forcedReversal)
            {
                scratch.TempDir = LadyBugEnemyDecisionModel.ReverseDirection(scratch.TempDir);
                path = "433A_OUTSIDE_CENTER_FORCED_REVERSAL";
            }
            else
            {
                path = "433A_OUTSIDE_CENTER_KEEP_DIRECTION";
            }
        }

        int selectedDirection = scratch.TempDir & 0x0F;
        int beforeStepX = scratch.TempX & 0xFF;
        int beforeStepY = scratch.TempY & 0xFF;
        int rejectedBeforeStep = scratch.RejectedDirMask & 0x0F;

        LadyBugEnemyDecisionModel.ApplyEnemyTempMovementStep(scratch);

        int modeledRaw = WithDirection(startEnemy.Raw, selectedDirection);
        return new SourceStepResult(
            startEnemy.Slot,
            startEnemy.Raw,
            startEnemy.X,
            startEnemy.Y,
            preferred,
            selectedDirection,
            modeledRaw,
            beforeStepX,
            beforeStepY,
            scratch.TempX,
            scratch.TempY,
            rejectedBeforeStep,
            scratch.FallbackHelper,
            path,
            gate,
            fallbackEntered,
            forcedReversal,
            testedDirections);
    }

    private static string FindFallbackDirection(
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        LadyBugMameLocalTile4130Oracle localTileOracle,
        ref int testedDirections)
    {
        int scanMask = scratch.RejectedDirMask & 0x0F;
        int direction = LadyBugEnemyDecisionModel.DirLeft;

        for (int attempt = 1; attempt <= 8; attempt++)
        {
            bool alreadyRejected = (scanMask & 0x01) != 0;

            if (!alreadyRejected)
            {
                testedDirections++;
                if (SourceAcceptsDirection(direction, scratch.TempX, scratch.TempY, localTileOracle))
                {
                    scratch.TempDir = direction;
                    return "4331_FALLBACK_SELECTED";
                }
            }

            direction <<= 1;
            scanMask >>= 1;

            if ((direction & 0x10) != 0)
            {
                scanMask = 0;
                direction = LadyBugEnemyDecisionModel.DirLeft;
            }
        }

        return "4331_FALLBACK_NOT_FOUND";
    }

    private static bool SourceAcceptsDirection(
        int direction,
        int x,
        int y,
        LadyBugMameLocalTile4130Oracle localTileOracle)
    {
        LadyBugEnemyDecisionModel.LogicalMazeValidationResult logical =
            LadyBugEnemyDecisionModel.ValidateLogicalMazeDirection(
                direction,
                x,
                y,
                LadyBugStaticMazeRomTable.Table0DA2);

        if (!logical.Accepted)
            return false;

        return localTileOracle.Probe(x, y, direction).Allowed;
    }

    private static SimulationEnemyWorkState BuildModeledEnemyWork(
        EnemyTraceEnemyWorkState? referenceWork,
        SourceStepResult step,
        LadyBugPreferredHybridProvider.Result preferredResult)
    {
        SimulationEnemyWorkState work = CopyEnemyWork(referenceWork) ?? new SimulationEnemyWorkState();
        work.TempDir = step.SelectedDirection;
        work.TempX = step.ModeledX;
        work.TempY = step.ModeledY;
        work.RejectedMask = step.RejectedMaskBeforeStep;
        work.FallbackMask = step.ModeledFallbackHelper;

        work.Preferred.Clear();
        for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
            work.Preferred.Add(preferredResult.PreferredTuple[i] & 0x0F);

        work.PreferredShadow.Clear();
        for (int i = 0; i < LadyBugMonsterPreferenceSystem.PreferredSlotCount; i++)
            work.PreferredShadow.Add(preferredResult.PreferredTuple[i] & 0x0F);
        work.PreferredShadowSource = preferredResult.Source;

        return work;
    }

    private static void CompareModeledResult(
        int frameIndex,
        EnemyTraceFrame resultFrame,
        EnemyTraceActor referenceEnemy,
        SourceStepResult step,
        SimulationEnemyWorkState modeledWork,
        ReplayStats stats)
    {
        bool slotMatch =
            ((referenceEnemy.x & 0xFF) == step.ModeledX) &&
            ((referenceEnemy.y & 0xFF) == step.ModeledY) &&
            (DirectionFromRaw(referenceEnemy.raw) == step.SelectedDirection);

        if (slotMatch)
        {
            stats.ModeledSlotMatches++;
        }
        else
        {
            stats.ModeledSlotMismatches++;
            if (string.IsNullOrEmpty(stats.FirstModeledSlotMismatch))
            {
                stats.FirstModeledSlotMismatch =
                    $"firstModeledSlotMismatch frameIndex={frameIndex} tick={resultFrame.frame} slot={step.Slot} " +
                    $"path={step.Path} start=({Hex2(step.StartX)},{Hex2(step.StartY)}) " +
                    $"model=dir:{DirLabel(step.SelectedDirection)} xy=({Hex2(step.ModeledX)},{Hex2(step.ModeledY)}) raw={Hex2(step.ModeledRaw)} " +
                    $"reference=dir:{DirLabel(DirectionFromRaw(referenceEnemy.raw))} xy=({Hex2(referenceEnemy.x)},{Hex2(referenceEnemy.y)}) raw={Hex2(referenceEnemy.raw)}";
            }
        }

        if (resultFrame.enemyWork == null)
        {
            stats.ModeledEnemyWorkMismatches++;
            if (string.IsNullOrEmpty(stats.FirstModeledEnemyWorkMismatch))
                stats.FirstModeledEnemyWorkMismatch = $"firstModeledEnemyWorkMismatch frameIndex={frameIndex} tick={resultFrame.frame}: missing reference enemyWork";
            return;
        }

        bool workMatch =
            ((resultFrame.enemyWork.tempDir & 0x0F) == (modeledWork.TempDir & 0x0F)) &&
            ((resultFrame.enemyWork.tempX & 0xFF) == (modeledWork.TempX & 0xFF)) &&
            ((resultFrame.enemyWork.tempY & 0xFF) == (modeledWork.TempY & 0xFF)) &&
            ((resultFrame.enemyWork.rejectedMask & 0x0F) == (modeledWork.RejectedMask & 0x0F)) &&
            ((resultFrame.enemyWork.fallbackMask & 0xFF) == (modeledWork.FallbackMask & 0xFF));

        if (workMatch)
        {
            stats.ModeledEnemyWorkMatches++;
        }
        else
        {
            stats.ModeledEnemyWorkMismatches++;
            if (string.IsNullOrEmpty(stats.FirstModeledEnemyWorkMismatch))
            {
                stats.FirstModeledEnemyWorkMismatch =
                    $"firstModeledEnemyWorkMismatch frameIndex={frameIndex} tick={resultFrame.frame} slot={step.Slot} path={step.Path} " +
                    $"model=rej:{Hex2(modeledWork.RejectedMask)} helper:{Hex2(modeledWork.FallbackMask)} dir:{DirLabel(modeledWork.TempDir)} xy=({Hex2(modeledWork.TempX)},{Hex2(modeledWork.TempY)}) " +
                    $"reference=rej:{Hex2(resultFrame.enemyWork.rejectedMask)} helper:{Hex2(resultFrame.enemyWork.fallbackMask)} dir:{DirLabel(resultFrame.enemyWork.tempDir)} xy=({Hex2(resultFrame.enemyWork.tempX)},{Hex2(resultFrame.enemyWork.tempY)})";
            }
        }
    }

    private static SimulationFrame BuildFrame(
        EnemyTraceFrame referenceFrame,
        int frameIndex,
        List<SimulationActorState> simulatedEnemies,
        SimulationEnemyWorkState? enemyWork)
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
            Player = referenceFrame.player == null ? null : CopyActor(referenceFrame.player),
            EnemyWork = enemyWork,
            Timers = CopyTimers(referenceFrame.timers),
            Ports = CopyPorts(referenceFrame.ports)
        };

        foreach (SimulationActorState enemy in simulatedEnemies)
            frame.Enemies.Add(CloneActor(enemy));

        if (referenceFrame.gates != null)
        {
            foreach (EnemyTraceGateState gate in referenceFrame.gates)
                frame.Gates.Add(CopyGate(gate));
        }

        return frame;
    }

    private static List<SimulationActorState> CopyReferenceEnemies(EnemyTraceFrame frame)
    {
        var result = new List<SimulationActorState>();
        if (frame.enemies == null)
            return result;

        foreach (EnemyTraceActor enemy in frame.enemies)
            result.Add(CopyActor(enemy));

        return result;
    }

    private static void SyncInactiveAndMissingEnemySlotsFromReference(
        List<SimulationActorState> simulatedEnemies,
        EnemyTraceFrame referenceFrame)
    {
        if (referenceFrame.enemies == null)
            return;

        foreach (EnemyTraceActor referenceEnemy in referenceFrame.enemies)
        {
            if (referenceEnemy.active && referenceEnemy.HasKnownPosition)
                continue;

            ReplaceOrAddEnemy(simulatedEnemies, CopyActor(referenceEnemy));
        }
    }

    private static void ApplyModeledEnemyState(
        List<SimulationActorState> simulatedEnemies,
        int slot,
        SourceStepResult step)
    {
        SimulationActorState? enemy = FindSimulationEnemyBySlot(simulatedEnemies, slot);
        if (enemy == null)
        {
            enemy = new SimulationActorState { Slot = slot };
            simulatedEnemies.Add(enemy);
        }

        enemy.Raw = step.ModeledRaw;
        enemy.X = step.ModeledX;
        enemy.Y = step.ModeledY;
        enemy.Active = true;
        enemy.Direction = Hex2(step.SelectedDirection);
    }

    private static void ReplaceOrAddEnemy(List<SimulationActorState> enemies, SimulationActorState replacement)
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Slot == replacement.Slot)
            {
                enemies[i] = replacement;
                return;
            }
        }

        enemies.Add(replacement);
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

    private static SimulationTimersState? CopyTimers(EnemyTraceTimersState? timers)
    {
        if (timers == null)
            return null;

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

    private static SimulationPortsState? CopyPorts(EnemyTracePortsState? ports)
    {
        if (ports == null)
            return null;

        return new SimulationPortsState
        {
            In0_9000 = ports.in0_9000,
            In1_9001 = ports.in1_9001,
            Dsw0_9002 = ports.dsw0_9002,
            Dsw1_9003 = ports.dsw1_9003
        };
    }

    private static SimulationEnemyWorkState? CopyEnemyWork(EnemyTraceEnemyWorkState? work)
    {
        if (work == null)
            return null;

        var result = new SimulationEnemyWorkState
        {
            TempDir = work.tempDir,
            TempX = work.tempX,
            TempY = work.tempY,
            RejectedMask = work.rejectedMask,
            FallbackMask = work.fallbackMask,
            ChaseRoundRobin = work.chaseRoundRobin
        };

        result.Preferred.AddRange(work.preferred);
        result.ChaseTimers.AddRange(work.chaseTimers);
        return result;
    }

    private static SimulationActorState? FindActiveKnownSimulationEnemy(List<SimulationActorState> enemies)
    {
        foreach (SimulationActorState enemy in enemies)
        {
            if (enemy.Active && enemy.HasKnownPosition)
                return enemy;
        }

        return null;
    }

    private static EnemyTraceActor? FindActiveKnownReferenceEnemy(IReadOnlyList<EnemyTraceActor>? enemies)
    {
        if (enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in enemies)
        {
            if (enemy.active && enemy.HasKnownPosition)
                return enemy;
        }

        return null;
    }

    private static SimulationActorState? FindSimulationEnemyBySlot(List<SimulationActorState> enemies, int slot)
    {
        foreach (SimulationActorState enemy in enemies)
        {
            if (enemy.Slot == slot)
                return enemy;
        }

        return null;
    }

    private static int CountActiveKnown(List<SimulationActorState> enemies)
    {
        int count = 0;
        foreach (SimulationActorState enemy in enemies)
        {
            if (enemy.Active && enemy.HasKnownPosition)
                count++;
        }

        return count;
    }

    private static int CountActiveKnown(IReadOnlyList<EnemyTraceActor>? enemies)
    {
        if (enemies == null)
            return 0;

        int count = 0;
        foreach (EnemyTraceActor enemy in enemies)
        {
            if (enemy.active && enemy.HasKnownPosition)
                count++;
        }

        return count;
    }

    private static int DirectionFromRaw(int raw)
    {
        return raw < 0 ? 0 : (raw >> 4) & 0x0F;
    }

    private static int WithDirection(int raw, int direction)
    {
        return (((direction & 0x0F) << 4) | (raw & 0x0F) | 0x02) & 0xFF;
    }

    private static string BuildSummary(ReplayStats stats)
    {
        bool modeledClean =
            stats.ModeledSlotMismatches == 0 &&
            stats.ModeledEnemyWorkMismatches == 0 &&
            stats.MissingPreferredSyncTransitions == 0 &&
            stats.MissingVramSyncTransitions == 0 &&
            stats.SlotChangedSyncTransitions == 0 &&
            stats.InvalidSingleEnemyShapeSyncTransitions == 0;

        bool fullScopeClean = modeledClean && stats.MultiEnemySyncTransitions == 0;

        var builder = new StringBuilder();
        builder.Append($"Lady Bug source-path single-enemy replay {Version}: ");
        builder.Append($"frames={stats.Frames}, ");
        builder.Append($"transitions={stats.Transitions}, ");
        builder.Append($"modeledSingleEnemyTransitions={stats.ModeledSingleEnemyTransitions}, ");
        builder.Append($"noActiveSyncTransitions={stats.NoActiveSyncTransitions}, ");
        builder.Append($"releaseSyncTransitions={stats.ReleaseSyncTransitions}, ");
        builder.Append($"multiEnemySyncTransitions={stats.MultiEnemySyncTransitions}, ");
        builder.Append($"invalidSingleEnemyShapeSyncTransitions={stats.InvalidSingleEnemyShapeSyncTransitions}, ");
        builder.Append($"slotChangedSyncTransitions={stats.SlotChangedSyncTransitions}, ");
        builder.Append($"missingPreferredSyncTransitions={stats.MissingPreferredSyncTransitions}, ");
        builder.Append($"missingVramSyncTransitions={stats.MissingVramSyncTransitions}, ");
        builder.Append($"decisionGateCarrySet={stats.DecisionGateCarrySet}, ");
        builder.Append($"decisionGateCarryClear={stats.DecisionGateCarryClear}, ");
        builder.Append($"preferredAccepted={stats.PreferredAccepted}, ");
        builder.Append($"preferredRejectedCurrentKept={stats.PreferredRejectedCurrentKept}, ");
        builder.Append($"fallbackSelected={stats.FallbackSelected}, ");
        builder.Append($"fallbackNotFound={stats.FallbackNotFound}, ");
        builder.Append($"outsideCenterKeep={stats.OutsideCenterKeep}, ");
        builder.Append($"outsideCenterForcedReversal={stats.OutsideCenterForcedReversal}, ");
        builder.Append($"testedDirectionProbes={stats.TestedDirectionProbes}, ");
        builder.Append($"modeledSlotMatches={stats.ModeledSlotMatches}, ");
        builder.Append($"modeledSlotMismatches={stats.ModeledSlotMismatches}, ");
        builder.Append($"modeledEnemyWorkMatches={stats.ModeledEnemyWorkMatches}, ");
        builder.Append($"modeledEnemyWorkMismatches={stats.ModeledEnemyWorkMismatches}, ");
        builder.Append($"preferredModeledRotateTransitions={stats.PreferredModeledRotateTransitions}, ");
        builder.Append($"preferredModeledBfsTransitions={stats.PreferredModeledBfsTransitions}, ");
        builder.Append($"preferredTraceFallbackTransitions={stats.PreferredTraceFallbackTransitions}, ");
        builder.Append($"modeledClean={(modeledClean ? "true" : "false")}, ");
        builder.Append($"fullScopeClean={(fullScopeClean ? "true" : "false")}, ");
        builder.Append("releaseMode=reference-synced, ");
        builder.Append("preferredMode=hybrid-modeled-2E97-rotate-and-visible-477D-bfs-else-trace-synced, ");
        builder.Append("environmentMode=trace-synced, multiEnemyMode=reference-sync-after-scope-limit");

        if (modeledClean)
            builder.Append("; firstModeledProblem: none");
        else
            AppendProblems(builder, stats);

        AppendOptional(builder, stats.FirstMultiEnemySync);
        AppendOptional(builder, stats.FirstPreferredModeledRotate);
        AppendOptional(builder, stats.FirstPreferredModeledBfs);
        AppendOptional(builder, stats.FirstPreferredTraceFallback);

        builder.Append("; NOTE: v0.9.14c computes the active single-enemy movement step through the source path. It uses modeled 0x2E97 rotate preferred[] and visible 0x477D BFS/chase overrides from 0x45DC+0x6200 when safe. Random 0x2EC7 base tuples, release timing, player, gates, timers, VRAM context, inactive slots, and multi-enemy frames remain synchronized from MAME.");
        return builder.ToString();
    }

    private static void AppendProblems(StringBuilder builder, ReplayStats stats)
    {
        AppendOptional(builder, stats.FirstModeledSlotMismatch);
        AppendOptional(builder, stats.FirstModeledEnemyWorkMismatch);
        AppendOptional(builder, stats.FirstSlotChangeSync);

        if (stats.MissingPreferredSyncTransitions > 0)
            builder.Append("; missingPreferredSyncTransitions: trace lacks preferred[] for a modeled mono-enemy transition");

        if (stats.MissingVramSyncTransitions > 0)
            builder.Append("; missingVramSyncTransitions: trace lacks rawMemory.vramD000_D3FF for a modeled mono-enemy transition");

        if (stats.InvalidSingleEnemyShapeSyncTransitions > 0)
            builder.Append("; invalidSingleEnemyShapeSyncTransitions: active enemy shape changed unexpectedly inside mono-enemy scope");
    }

    private static void AppendOptional(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        builder.Append("; ");
        builder.Append(value);
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string DirLabel(int direction)
    {
        return LadyBugDirectionBits.ToLabel(direction);
    }

    private sealed class ReplayStats
    {
        public int Frames;
        public int Transitions;
        public int NoActiveSyncTransitions;
        public int ReleaseSyncTransitions;
        public int MultiEnemySyncTransitions;
        public int InvalidSingleEnemyShapeSyncTransitions;
        public int SlotChangedSyncTransitions;
        public int MissingPreferredSyncTransitions;
        public int MissingVramSyncTransitions;
        public int ModeledSingleEnemyTransitions;
        public int DecisionGateCarrySet;
        public int DecisionGateCarryClear;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int FallbackSelected;
        public int FallbackNotFound;
        public int OutsideCenterKeep;
        public int OutsideCenterForcedReversal;
        public int TestedDirectionProbes;
        public int ModeledSlotMatches;
        public int ModeledSlotMismatches;
        public int ModeledEnemyWorkMatches;
        public int ModeledEnemyWorkMismatches;
        public int PreferredModeledRotateTransitions;
        public int PreferredModeledBfsTransitions;
        public int PreferredTraceFallbackTransitions;
        public string FirstModeledSlotMismatch = string.Empty;
        public string FirstModeledEnemyWorkMismatch = string.Empty;
        public string FirstMultiEnemySync = string.Empty;
        public string FirstSlotChangeSync = string.Empty;
        public string FirstPreferredModeledRotate = string.Empty;
        public string FirstPreferredModeledBfs = string.Empty;
        public string FirstPreferredTraceFallback = string.Empty;

        public void RecordPreferredProvider(LadyBugPreferredHybridProvider.Result result)
        {
            if (result.UsedModeledRotate)
            {
                PreferredModeledRotateTransitions++;
                if (string.IsNullOrEmpty(FirstPreferredModeledRotate))
                    FirstPreferredModeledRotate = "firstPreferredModeledRotate source=" + result.Source;
            }
            else if (result.UsedModeledBfs)
            {
                PreferredModeledBfsTransitions++;
                if (string.IsNullOrEmpty(FirstPreferredModeledBfs))
                    FirstPreferredModeledBfs = "firstPreferredModeledBfs source=" + result.Source + " " + result.Guidance;
            }
            else
            {
                PreferredTraceFallbackTransitions++;
                if (string.IsNullOrEmpty(FirstPreferredTraceFallback))
                    FirstPreferredTraceFallback = "firstPreferredTraceFallback source=" + result.Source;
            }
        }

        public void RecordModeledTransition(SourceStepResult step)
        {
            ModeledSingleEnemyTransitions++;
            TestedDirectionProbes += step.TestedDirections;

            if (step.Gate.CarrySet)
                DecisionGateCarrySet++;
            else
                DecisionGateCarryClear++;

            switch (step.Path)
            {
                case "42E6_PREFERRED_ACCEPTED":
                    PreferredAccepted++;
                    break;
                case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
                    PreferredRejectedCurrentKept++;
                    break;
                case "4331_FALLBACK_SELECTED":
                    FallbackSelected++;
                    break;
                case "4331_FALLBACK_NOT_FOUND":
                    FallbackNotFound++;
                    break;
                case "433A_OUTSIDE_CENTER_FORCED_REVERSAL":
                    OutsideCenterForcedReversal++;
                    break;
                default:
                    if (step.Path.StartsWith("433A_OUTSIDE_CENTER", StringComparison.Ordinal))
                        OutsideCenterKeep++;
                    break;
            }
        }
    }

    private readonly struct SourceStepResult
    {
        public SourceStepResult(
            int slot,
            int startRaw,
            int startX,
            int startY,
            int preferred,
            int selectedDirection,
            int modeledRaw,
            int beforeStepX,
            int beforeStepY,
            int modeledX,
            int modeledY,
            int rejectedMaskBeforeStep,
            int modeledFallbackHelper,
            string path,
            LadyBugEnemyDecisionGate427EModel.Result gate,
            bool fallbackEntered,
            bool forcedReversal,
            int testedDirections)
        {
            Slot = slot;
            StartRaw = startRaw;
            StartX = startX & 0xFF;
            StartY = startY & 0xFF;
            Preferred = preferred & 0x0F;
            SelectedDirection = selectedDirection & 0x0F;
            ModeledRaw = modeledRaw & 0xFF;
            BeforeStepX = beforeStepX & 0xFF;
            BeforeStepY = beforeStepY & 0xFF;
            ModeledX = modeledX & 0xFF;
            ModeledY = modeledY & 0xFF;
            RejectedMaskBeforeStep = rejectedMaskBeforeStep & 0x0F;
            ModeledFallbackHelper = modeledFallbackHelper & 0xFF;
            Path = path;
            Gate = gate;
            FallbackEntered = fallbackEntered;
            ForcedReversal = forcedReversal;
            TestedDirections = testedDirections;
        }

        public int Slot { get; }
        public int StartRaw { get; }
        public int StartX { get; }
        public int StartY { get; }
        public int Preferred { get; }
        public int SelectedDirection { get; }
        public int ModeledRaw { get; }
        public int BeforeStepX { get; }
        public int BeforeStepY { get; }
        public int ModeledX { get; }
        public int ModeledY { get; }
        public int RejectedMaskBeforeStep { get; }
        public int ModeledFallbackHelper { get; }
        public string Path { get; }
        public LadyBugEnemyDecisionGate427EModel.Result Gate { get; }
        public bool FallbackEntered { get; }
        public bool ForcedReversal { get; }
        public int TestedDirections { get; }
    }
}
