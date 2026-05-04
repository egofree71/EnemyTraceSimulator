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

    private static SimulationFrame ConvertReferenceFrame(int index, EnemyTraceFrame referenceFrame)
    {
        var frame = new SimulationFrame
        {
            FrameIndex = index,
            Tick = referenceFrame.frame,
            Player = referenceFrame.player == null ? null : ConvertActor(referenceFrame.player)
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
}
