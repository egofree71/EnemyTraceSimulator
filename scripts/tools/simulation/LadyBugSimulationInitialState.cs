using System.Collections.Generic;

/// <summary>
/// Initial state consumed by the future Lady Bug enemy simulation.
/// v0.6.2 keeps this as a data container built from the first MAME trace frame.
/// </summary>
public sealed class LadyBugSimulationInitialState
{
    public int InitialTick { get; init; }
    public SimulationActorState? Player { get; init; }
    public List<SimulationActorState> Enemies { get; } = new();
    public List<SimulationGateState> Gates { get; } = new();
    public SimulationEnemyWorkState? EnemyWork { get; init; }
    public SimulationTimersState? Timers { get; init; }
    public SimulationPortsState? Ports { get; init; }

    public int ActiveEnemyCount
    {
        get
        {
            int count = 0;
            foreach (SimulationActorState enemy in Enemies)
            {
                if (enemy.Active)
                    count++;
            }

            return count;
        }
    }

    public int KnownEnemySlotCount
    {
        get
        {
            int count = 0;
            foreach (SimulationActorState enemy in Enemies)
            {
                if (enemy.HasKnownPosition)
                    count++;
            }

            return count;
        }
    }

    public string Summary =>
        $"tick={InitialTick}, enemies={Enemies.Count}, active={ActiveEnemyCount}, known={KnownEnemySlotCount}, gates={Gates.Count}";

    public static LadyBugSimulationInitialState FromFirstFrame(EnemyTraceFrame frame)
    {
        var state = new LadyBugSimulationInitialState
        {
            InitialTick = frame.frame,
            Player = frame.player == null ? null : CopyActor(frame.player),
            EnemyWork = frame.enemyWork == null ? null : CopyEnemyWork(frame.enemyWork),
            Timers = frame.timers == null ? null : CopyTimers(frame.timers),
            Ports = frame.ports == null ? null : CopyPorts(frame.ports)
        };

        if (frame.enemies != null)
        {
            foreach (EnemyTraceActor enemy in frame.enemies)
                state.Enemies.Add(CopyActor(enemy));
        }

        if (frame.gates != null)
        {
            foreach (EnemyTraceGateState gate in frame.gates)
                state.Gates.Add(CopyGate(gate));
        }

        return state;
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

    private static SimulationEnemyWorkState CopyEnemyWork(EnemyTraceEnemyWorkState enemyWork)
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
}
