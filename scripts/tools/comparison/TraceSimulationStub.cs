using System.Collections.Generic;

/// <summary>
/// Temporary simulation source used to validate the comparison pipeline before the
/// real C# enemy movement adapter is connected.
/// </summary>
public static class TraceSimulationStub
{
    public static List<SimulationFrame> CreateIdentitySimulation(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var simulation = new List<SimulationFrame>(referenceFrames.Count);

        for (int i = 0; i < referenceFrames.Count; i++)
            simulation.Add(ConvertReferenceFrame(i, referenceFrames[i]));

        return simulation;
    }

    public static List<SimulationFrame> CreateFirstActiveEnemyXMismatchSimulation(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        List<SimulationFrame> simulation = CreateIdentitySimulation(referenceFrames);

        foreach (SimulationFrame frame in simulation)
        {
            foreach (SimulationActorState enemy in frame.Enemies)
            {
                if (!enemy.Active)
                    continue;

                enemy.X = (enemy.X + 1) & 0xFF;
                return simulation;
            }
        }

        foreach (SimulationFrame frame in simulation)
        {
            if (frame.Player == null)
                continue;

            frame.Player.X = (frame.Player.X + 1) & 0xFF;
            return simulation;
        }

        return simulation;
    }

    private static SimulationFrame ConvertReferenceFrame(int index, EnemyTraceFrame referenceFrame)
    {
        var frame = new SimulationFrame
        {
            FrameIndex = index,
            Tick = referenceFrame.frame,
            Schema = referenceFrame.schema,
            Phase = referenceFrame.phase,
            MameFrame = referenceFrame.mameFrame,
            Pc = referenceFrame.pc,
            R = referenceFrame.r,
            Player = referenceFrame.player == null ? null : ConvertActor(referenceFrame.player),
            EnemyWork = referenceFrame.enemyWork == null ? null : ConvertEnemyWork(referenceFrame.enemyWork),
            Timers = referenceFrame.timers == null ? null : ConvertTimers(referenceFrame.timers),
            Ports = referenceFrame.ports == null ? null : ConvertPorts(referenceFrame.ports)
        };

        if (referenceFrame.enemies != null)
        {
            foreach (EnemyTraceActor enemy in referenceFrame.enemies)
                frame.Enemies.Add(ConvertActor(enemy));
        }

        if (referenceFrame.gates != null)
        {
            foreach (EnemyTraceGateState gate in referenceFrame.gates)
                frame.Gates.Add(ConvertGate(gate));
        }

        return frame;
    }

    private static SimulationActorState ConvertActor(EnemyTraceActor actor)
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

    private static SimulationGateState ConvertGate(EnemyTraceGateState gate)
    {
        return new SimulationGateState
        {
            GateId = gate.gate_id,
            Orientation = gate.orientation,
            PivotX = gate.pivot_x,
            PivotY = gate.pivot_y
        };
    }

    private static SimulationEnemyWorkState ConvertEnemyWork(EnemyTraceEnemyWorkState enemyWork)
    {
        var state = new SimulationEnemyWorkState
        {
            TempDir = enemyWork.tempDir,
            TempX = enemyWork.tempX,
            TempY = enemyWork.tempY,
            RejectedMask = enemyWork.rejectedMask,
            FallbackMask = enemyWork.fallbackMask,
            ChaseRoundRobin = enemyWork.chaseRoundRobin
        };

        state.Preferred.AddRange(enemyWork.preferred);
        state.ChaseTimers.AddRange(enemyWork.chaseTimers);
        return state;
    }

    private static SimulationTimersState ConvertTimers(EnemyTraceTimersState timers)
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

    private static SimulationPortsState ConvertPorts(EnemyTracePortsState ports)
    {
        return new SimulationPortsState
        {
            In0_9000 = ports.in0_9000,
            In1_9001 = ports.in1_9001,
            Dsw0_9002 = ports.dsw0_9002,
            Dsw1_9003 = ports.dsw1_9003
        };
    }
}
