using System;
using System.Collections.Generic;

/// <summary>
/// Compares a MAME reference trace against a simulation-produced frame sequence.
/// v0.5 intentionally keeps this strict and explicit.
/// </summary>
public static class TraceComparisonRunner
{
    public static TraceComparisonResult Compare(
        IReadOnlyList<EnemyTraceFrame> referenceFrames,
        IReadOnlyList<SimulationFrame> simulationFrames)
    {
        var result = new TraceComparisonResult();
        int count = Math.Min(referenceFrames.Count, simulationFrames.Count);
        result.ComparedFrameCount = count;

        if (referenceFrames.Count != simulationFrames.Count)
        {
            result.Frames.Add(new ComparisonFrame
            {
                FrameIndex = count,
                ReferenceTick = count < referenceFrames.Count ? referenceFrames[count].frame : -1,
                SimulationTick = count < simulationFrames.Count ? simulationFrames[count].Tick : -1
            });

            result.Frames[^1].Mismatches.Add(new TraceMismatch
            {
                Kind = TraceMismatchKind.Count,
                FrameIndex = count,
                Tick = count < referenceFrames.Count ? referenceFrames[count].frame : -1,
                Field = "frameCount",
                Expected = referenceFrames.Count.ToString(),
                Actual = simulationFrames.Count.ToString(),
                Description = "Reference and simulation frame counts differ."
            });
        }

        for (int i = 0; i < count; i++)
        {
            EnemyTraceFrame referenceFrame = referenceFrames[i];
            SimulationFrame simulationFrame = simulationFrames[i];

            var comparisonFrame = new ComparisonFrame
            {
                FrameIndex = i,
                ReferenceTick = referenceFrame.frame,
                SimulationTick = simulationFrame.Tick
            };

            CompareFrameHeader(referenceFrame, simulationFrame, comparisonFrame);
            ComparePlayer(referenceFrame.player, simulationFrame.Player, comparisonFrame);
            CompareEnemies(referenceFrame, simulationFrame, comparisonFrame);
            CompareGates(referenceFrame, simulationFrame, comparisonFrame);
            CompareEnemyWork(referenceFrame.enemyWork, simulationFrame.EnemyWork, comparisonFrame);
            CompareTimers(referenceFrame.timers, simulationFrame.Timers, comparisonFrame);
            ComparePorts(referenceFrame.ports, simulationFrame.Ports, comparisonFrame);

            if (comparisonFrame.HasMismatch)
                result.Frames.Add(comparisonFrame);
        }

        return result;
    }

    private static void CompareFrameHeader(EnemyTraceFrame reference, SimulationFrame simulation, ComparisonFrame comparison)
    {
        CompareInt(reference.frame, simulation.Tick, "tick", TraceMismatchKind.Frame, comparison);
        CompareString(reference.schema, simulation.Schema, "schema", TraceMismatchKind.Metadata, comparison);
        CompareString(reference.phase, simulation.Phase, "phase", TraceMismatchKind.Metadata, comparison);
        CompareInt(reference.mameFrame, simulation.MameFrame, "mameFrame", TraceMismatchKind.Metadata, comparison);
        CompareString(reference.pc, simulation.Pc, "pc", TraceMismatchKind.Metadata, comparison);
        CompareString(reference.r, simulation.R, "r", TraceMismatchKind.Metadata, comparison);
    }

    private static void ComparePlayer(EnemyTraceActor? reference, SimulationActorState? simulation, ComparisonFrame comparison)
    {
        if (reference == null && simulation == null)
            return;

        if (reference == null || simulation == null)
        {
            AddMismatch(
                comparison,
                TraceMismatchKind.Player,
                "player",
                reference == null ? "null" : "present",
                simulation == null ? "null" : "present",
                "Player presence differs.");
            return;
        }

        CompareActor(reference, simulation, TraceMismatchKind.Player, comparison);
    }

    private static void CompareEnemies(EnemyTraceFrame referenceFrame, SimulationFrame simulationFrame, ComparisonFrame comparison)
    {
        if (referenceFrame.enemies == null)
            return;

        foreach (EnemyTraceActor referenceEnemy in referenceFrame.enemies)
        {
            SimulationActorState? simulationEnemy = FindSimulationEnemy(simulationFrame, referenceEnemy.slot);

            if (simulationEnemy == null)
            {
                AddMismatch(
                    comparison,
                    TraceMismatchKind.Enemy,
                    "enemySlot",
                    "present",
                    "missing",
                    $"Enemy slot {referenceEnemy.slot} missing in simulation.",
                    slot: referenceEnemy.slot);
                continue;
            }

            CompareActor(referenceEnemy, simulationEnemy, TraceMismatchKind.Enemy, comparison);
        }
    }

    private static void CompareGates(EnemyTraceFrame referenceFrame, SimulationFrame simulationFrame, ComparisonFrame comparison)
    {
        if (referenceFrame.gates == null)
            return;

        foreach (EnemyTraceGateState referenceGate in referenceFrame.gates)
        {
            SimulationGateState? simulationGate = FindSimulationGate(simulationFrame, referenceGate.gate_id);

            if (simulationGate == null)
            {
                AddMismatch(
                    comparison,
                    TraceMismatchKind.Gate,
                    "gate",
                    "present",
                    "missing",
                    $"Gate {referenceGate.gate_id} missing in simulation.",
                    gateId: referenceGate.gate_id);
                continue;
            }

            CompareString(referenceGate.orientation, simulationGate.Orientation, "orientation", TraceMismatchKind.Gate, comparison, gateId: referenceGate.gate_id);
            CompareInt(referenceGate.pivot_x, simulationGate.PivotX, "pivot_x", TraceMismatchKind.Gate, comparison, gateId: referenceGate.gate_id);
            CompareInt(referenceGate.pivot_y, simulationGate.PivotY, "pivot_y", TraceMismatchKind.Gate, comparison, gateId: referenceGate.gate_id);
        }
    }

    private static void CompareEnemyWork(EnemyTraceEnemyWorkState? reference, SimulationEnemyWorkState? simulation, ComparisonFrame comparison)
    {
        if (reference == null && simulation == null)
            return;

        if (reference == null || simulation == null)
        {
            AddMismatch(
                comparison,
                TraceMismatchKind.EnemyWork,
                "enemyWork",
                reference == null ? "null" : "present",
                simulation == null ? "null" : "present",
                "Enemy work presence differs.");
            return;
        }

        CompareInt(reference.tempDir, simulation.TempDir, "tempDir", TraceMismatchKind.EnemyWork, comparison);
        CompareInt(reference.tempX, simulation.TempX, "tempX", TraceMismatchKind.EnemyWork, comparison);
        CompareInt(reference.tempY, simulation.TempY, "tempY", TraceMismatchKind.EnemyWork, comparison);
        CompareInt(reference.rejectedMask, simulation.RejectedMask, "rejectedMask", TraceMismatchKind.EnemyWork, comparison);
        CompareInt(reference.fallbackMask, simulation.FallbackMask, "fallbackMask", TraceMismatchKind.EnemyWork, comparison);
        CompareInt(reference.chaseRoundRobin, simulation.ChaseRoundRobin, "chaseRoundRobin", TraceMismatchKind.EnemyWork, comparison);
        CompareIntList(reference.preferred, simulation.Preferred, "preferred", TraceMismatchKind.EnemyWork, comparison);
        CompareIntList(reference.chaseTimers, simulation.ChaseTimers, "chaseTimers", TraceMismatchKind.EnemyWork, comparison);
    }

    private static void CompareTimers(EnemyTraceTimersState? reference, SimulationTimersState? simulation, ComparisonFrame comparison)
    {
        if (reference == null && simulation == null)
            return;

        if (reference == null || simulation == null)
        {
            AddMismatch(
                comparison,
                TraceMismatchKind.Timer,
                "timers",
                reference == null ? "null" : "present",
                simulation == null ? "null" : "present",
                "Timer presence differs.");
            return;
        }

        CompareInt(reference.timer61B4, simulation.Timer61B4, "61B4", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.timer61B5, simulation.Timer61B5, "61B5", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.timer61B6, simulation.Timer61B6, "61B6", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.timer61B7, simulation.Timer61B7, "61B7", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.timer61B8, simulation.Timer61B8, "61B8", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.timer61B9, simulation.Timer61B9, "61B9", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.freeze61E1, simulation.Freeze61E1, "freeze61E1", TraceMismatchKind.Timer, comparison);
        CompareInt(reference.collectibleColorCounter6199, simulation.CollectibleColorCounter6199, "collectibleColorCounter6199", TraceMismatchKind.Timer, comparison);
    }

    private static void ComparePorts(EnemyTracePortsState? reference, SimulationPortsState? simulation, ComparisonFrame comparison)
    {
        if (reference == null && simulation == null)
            return;

        if (reference == null || simulation == null)
        {
            AddMismatch(
                comparison,
                TraceMismatchKind.Port,
                "ports",
                reference == null ? "null" : "present",
                simulation == null ? "null" : "present",
                "Port presence differs.");
            return;
        }

        CompareInt(reference.in0_9000, simulation.In0_9000, "in0_9000", TraceMismatchKind.Port, comparison);
        CompareInt(reference.in1_9001, simulation.In1_9001, "in1_9001", TraceMismatchKind.Port, comparison);
        CompareInt(reference.dsw0_9002, simulation.Dsw0_9002, "dsw0_9002", TraceMismatchKind.Port, comparison);
        CompareInt(reference.dsw1_9003, simulation.Dsw1_9003, "dsw1_9003", TraceMismatchKind.Port, comparison);
    }

    private static void CompareActor(
        EnemyTraceActor reference,
        SimulationActorState simulation,
        TraceMismatchKind kind,
        ComparisonFrame comparison)
    {
        int? slot = kind == TraceMismatchKind.Enemy ? reference.slot : null;

        CompareInt(reference.x, simulation.X, "x", kind, comparison, slot);
        CompareInt(reference.y, simulation.Y, "y", kind, comparison, slot);
        CompareString(reference.dir, simulation.Direction, "dir", kind, comparison, slot);
        CompareBool(reference.active, simulation.Active, "active", kind, comparison, slot);
    }

    private static void CompareInt(
        int expected,
        int actual,
        string field,
        TraceMismatchKind kind,
        ComparisonFrame comparison,
        int? slot = null,
        int? gateId = null)
    {
        if (expected == actual)
            return;

        AddMismatch(
            comparison,
            kind,
            field,
            expected.ToString("X2"),
            actual.ToString("X2"),
            $"{kind} {field} differs.",
            slot: slot,
            gateId: gateId);
    }

    private static void CompareString(
        string? expected,
        string? actual,
        string field,
        TraceMismatchKind kind,
        ComparisonFrame comparison,
        int? slot = null,
        int? gateId = null)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            return;

        AddMismatch(
            comparison,
            kind,
            field,
            expected ?? string.Empty,
            actual ?? string.Empty,
            $"{kind} {field} differs.",
            slot: slot,
            gateId: gateId);
    }

    private static void CompareBool(
        bool expected,
        bool actual,
        string field,
        TraceMismatchKind kind,
        ComparisonFrame comparison,
        int? slot = null)
    {
        if (expected == actual)
            return;

        AddMismatch(
            comparison,
            kind,
            field,
            expected.ToString(),
            actual.ToString(),
            $"{kind} {field} differs.",
            slot: slot);
    }

    private static void CompareIntList(
        IReadOnlyList<int> expected,
        IReadOnlyList<int> actual,
        string field,
        TraceMismatchKind kind,
        ComparisonFrame comparison)
    {
        if (expected.Count != actual.Count)
        {
            AddMismatch(
                comparison,
                kind,
                field,
                FormatIntList(expected),
                FormatIntList(actual),
                $"{kind} {field} count differs.");
            return;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i] == actual[i])
                continue;

            AddMismatch(
                comparison,
                kind,
                $"{field}[{i}]",
                expected[i].ToString("X2"),
                actual[i].ToString("X2"),
                $"{kind} {field}[{i}] differs.");
        }
    }

    private static string FormatIntList(IReadOnlyList<int> values)
    {
        var formatted = new List<string>(values.Count);
        foreach (int value in values)
            formatted.Add(value.ToString("X2"));

        return string.Join(",", formatted);
    }

    private static SimulationActorState? FindSimulationEnemy(SimulationFrame frame, int slot)
    {
        foreach (SimulationActorState enemy in frame.Enemies)
        {
            if (enemy.Slot == slot)
                return enemy;
        }

        return null;
    }

    private static SimulationGateState? FindSimulationGate(SimulationFrame frame, int gateId)
    {
        foreach (SimulationGateState gate in frame.Gates)
        {
            if (gate.GateId == gateId)
                return gate;
        }

        return null;
    }

    private static void AddMismatch(
        ComparisonFrame comparison,
        TraceMismatchKind kind,
        string field,
        string expected,
        string actual,
        string description,
        int? slot = null,
        int? gateId = null)
    {
        comparison.Mismatches.Add(new TraceMismatch
        {
            Kind = kind,
            FrameIndex = comparison.FrameIndex,
            Tick = comparison.ReferenceTick,
            Slot = slot,
            GateId = gateId,
            Field = field,
            Expected = expected,
            Actual = actual,
            Description = description
        });
    }
}
