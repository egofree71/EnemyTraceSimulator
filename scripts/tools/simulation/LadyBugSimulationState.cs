using System.Collections.Generic;

/// <summary>
/// Mutable state owned by the future Lady Bug enemy simulation adapter.
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

        // Intentionally not updated yet:
        // - enemy decision logic
        // - enemyWork
        //
        // This step only validates the low-level coordinate movement convention:
        // an active enemy moves one pixel using the direction observed in MAME.
        // Later patches should replace the reference direction with the real Lady Bug
        // enemy decision and enemy-work update sequence.
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
            ChaseRoundRobin = enemyWork.ChaseRoundRobin
        };

        clone.Preferred.AddRange(enemyWork.Preferred);
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
