using System;
using System.Collections.Generic;

/// <summary>
/// Compares a MAME reference trace against a simulation-produced frame sequence.
/// v0.5 intentionally keeps this strict and simple.
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

            if (comparisonFrame.HasMismatch)
                result.Frames.Add(comparisonFrame);
        }

        return result;
    }

    private static void CompareFrameHeader(EnemyTraceFrame reference, SimulationFrame simulation, ComparisonFrame comparison)
    {
        if (reference.frame != simulation.Tick)
        {
            AddMismatch(
                comparison,
                TraceMismatchKind.Frame,
                "tick",
                reference.frame.ToString(),
                simulation.Tick.ToString(),
                "Frame tick differs.");
        }
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

            if (!string.Equals(referenceGate.orientation, simulationGate.Orientation, StringComparison.OrdinalIgnoreCase))
            {
                AddMismatch(
                    comparison,
                    TraceMismatchKind.Gate,
                    "orientation",
                    referenceGate.orientation ?? string.Empty,
                    simulationGate.Orientation ?? string.Empty,
                    $"Gate {referenceGate.gate_id} orientation differs.",
                    gateId: referenceGate.gate_id);
            }

            if (referenceGate.pivot_x != simulationGate.PivotX)
            {
                AddMismatch(
                    comparison,
                    TraceMismatchKind.Gate,
                    "pivot_x",
                    referenceGate.pivot_x.ToString(),
                    simulationGate.PivotX.ToString(),
                    $"Gate {referenceGate.gate_id} pivot X differs.",
                    gateId: referenceGate.gate_id);
            }

            if (referenceGate.pivot_y != simulationGate.PivotY)
            {
                AddMismatch(
                    comparison,
                    TraceMismatchKind.Gate,
                    "pivot_y",
                    referenceGate.pivot_y.ToString(),
                    simulationGate.PivotY.ToString(),
                    $"Gate {referenceGate.gate_id} pivot Y differs.",
                    gateId: referenceGate.gate_id);
            }
        }
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
        int? slot = null)
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
            slot: slot);
    }

    private static void CompareString(
        string? expected,
        string? actual,
        string field,
        TraceMismatchKind kind,
        ComparisonFrame comparison,
        int? slot = null)
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
            slot: slot);
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
