using System.Collections.Generic;

/// <summary>
/// Converts a neutral simulation frame into the trace DTO already understood by
/// EnemyTraceBoardView. Coordinates remain in the raw MAME coordinate convention;
/// the board view performs the Y mirror only when drawing.
/// </summary>
public static class VisualReplayFrameConverter
{
    public static EnemyTraceFrame ToTraceFrame(SimulationFrame frame)
    {
        var trace = new EnemyTraceFrame
        {
            schema = frame.Schema,
            frame = frame.Tick,
            phase = frame.Phase,
            mameFrame = frame.MameFrame,
            pc = frame.Pc,
            r = frame.R,
            player = frame.Player == null ? null : ToTraceActor(frame.Player),
            enemies = new List<EnemyTraceActor>(),
            gates = new List<EnemyTraceGateState>(),
            enemyWork = frame.EnemyWork == null ? null : ToTraceEnemyWork(frame.EnemyWork)
        };

        foreach (SimulationActorState enemy in frame.Enemies)
            trace.enemies.Add(ToTraceActor(enemy));

        foreach (SimulationGateState gate in frame.Gates)
            trace.gates.Add(ToTraceGate(gate));

        return trace;
    }

    private static EnemyTraceActor ToTraceActor(SimulationActorState actor)
    {
        return new EnemyTraceActor
        {
            slot = actor.Slot,
            raw = actor.Raw,
            x = actor.X,
            y = actor.Y,
            dir = actor.Direction,
            active = actor.Active
        };
    }

    private static EnemyTraceGateState ToTraceGate(SimulationGateState gate)
    {
        return new EnemyTraceGateState
        {
            gate_id = gate.GateId,
            orientation = gate.Orientation,
            pivot_x = gate.PivotX,
            pivot_y = gate.PivotY
        };
    }

    private static EnemyTraceEnemyWorkState ToTraceEnemyWork(SimulationEnemyWorkState work)
    {
        return new EnemyTraceEnemyWorkState
        {
            tempDir = work.TempDir,
            tempX = work.TempX,
            tempY = work.TempY,
            rejectedMask = work.RejectedMask,
            fallbackMask = work.FallbackMask,
            preferred = new List<int>(work.Preferred),
            chaseTimers = new List<int>(work.ChaseTimers),
            chaseRoundRobin = work.ChaseRoundRobin
        };
    }
}
