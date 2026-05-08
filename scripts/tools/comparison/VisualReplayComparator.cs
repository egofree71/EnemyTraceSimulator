using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Compares only the visual gameplay state: player, active enemy slots and gate
/// orientations. Internal EnemyWork bytes are intentionally ignored here so the
/// visual playback stops only on visible or gameplay-facing divergence.
/// </summary>
public static class VisualReplayComparator
{
    public static VisualReplayMismatch Compare(
        SimulationFrame? simulation,
        EnemyTraceFrame reference,
        int frameIndex)
    {
        if (simulation == null)
        {
            return Mismatch(
                frameIndex,
                reference.frame,
                "simulation-missing-frame",
                "no simulation frame exists for this reference tick");
        }

        if (simulation.Tick != reference.frame)
        {
            return Mismatch(
                frameIndex,
                reference.frame,
                "tick-mismatch",
                $"simulation tick={simulation.Tick}, reference tick={reference.frame}");
        }

        VisualReplayMismatch player = ComparePlayer(simulation.Player, reference.player, frameIndex, reference.frame);
        if (player.HasMismatch)
            return player;

        VisualReplayMismatch enemies = CompareEnemies(simulation.Enemies, reference.enemies, frameIndex, reference.frame);
        if (enemies.HasMismatch)
            return enemies;

        VisualReplayMismatch gates = CompareGates(simulation.Gates, reference.gates, frameIndex, reference.frame);
        if (gates.HasMismatch)
            return gates;

        return VisualReplayMismatch.None;
    }

    public static string BuildSummary(IReadOnlyList<SimulationFrame> simulationFrames, IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        int compared = Math.Min(simulationFrames.Count, referenceFrames.Count);
        for (int i = 0; i < compared; i++)
        {
            VisualReplayMismatch mismatch = Compare(simulationFrames[i], referenceFrames[i], i);
            if (mismatch.HasMismatch)
                return $"visual replay comparison: comparedFrames={i + 1}, firstMismatch={mismatch}";
        }

        if (simulationFrames.Count != referenceFrames.Count)
        {
            return "visual replay comparison: comparedFrames=" + compared +
                   ", frameCountMismatch simulation=" + simulationFrames.Count +
                   " reference=" + referenceFrames.Count;
        }

        return "visual replay comparison: comparedFrames=" + compared + ", mismatches=0";
    }

    private static VisualReplayMismatch ComparePlayer(
        SimulationActorState? simulation,
        EnemyTraceActor? reference,
        int frameIndex,
        int tick)
    {
        if (simulation == null && reference == null)
            return VisualReplayMismatch.None;

        if (simulation == null || reference == null)
        {
            return Mismatch(
                frameIndex,
                tick,
                "player-presence",
                $"simulation={(simulation == null ? "missing" : "present")}, reference={(reference == null ? "missing" : "present")}");
        }

        if (simulation.X != reference.x || simulation.Y != reference.y)
        {
            return Mismatch(
                frameIndex,
                tick,
                "player-position",
                $"simulation=({Hex(simulation.X)},{Hex(simulation.Y)}) reference=({Hex(reference.x)},{Hex(reference.y)})");
        }

        if (NormalizeDir(simulation.Direction) != NormalizeDir(reference.dir))
        {
            return Mismatch(
                frameIndex,
                tick,
                "player-direction",
                $"simulation={FormatDir(simulation.Direction)} reference={FormatDir(reference.dir)}");
        }

        return VisualReplayMismatch.None;
    }

    private static VisualReplayMismatch CompareEnemies(
        IReadOnlyList<SimulationActorState> simulationEnemies,
        IReadOnlyList<EnemyTraceActor>? referenceEnemies,
        int frameIndex,
        int tick)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            SimulationActorState? simulation = FindSimulationEnemy(simulationEnemies, slot);
            EnemyTraceActor? reference = FindReferenceEnemy(referenceEnemies, slot);

            bool simulationActive = simulation?.Active == true;
            bool referenceActive = reference?.active == true;

            if (!simulationActive && !referenceActive)
                continue;

            if (simulationActive != referenceActive)
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "enemy-active-state",
                    $"slot={slot} simulationActive={simulationActive} referenceActive={referenceActive}{FormatLairContext(simulation, reference)}");
            }

            if (simulation == null || reference == null)
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "enemy-presence",
                    $"slot={slot} simulation={(simulation == null ? "missing" : "present")} reference={(reference == null ? "missing" : "present")}{FormatLairContext(simulation, reference)}");
            }

            if (simulation.X != reference.x || simulation.Y != reference.y)
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "enemy-position",
                    $"slot={slot} simulation=({Hex(simulation.X)},{Hex(simulation.Y)}) reference=({Hex(reference.x)},{Hex(reference.y)}){FormatLairContext(simulation, reference)}");
            }

            if (NormalizeDir(simulation.Direction) != NormalizeDir(reference.dir))
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "enemy-direction",
                    $"slot={slot} simulation={FormatDir(simulation.Direction)} reference={FormatDir(reference.dir)}{FormatLairContext(simulation, reference)}");
            }
        }

        return VisualReplayMismatch.None;
    }

    private static VisualReplayMismatch CompareGates(
        IReadOnlyList<SimulationGateState> simulationGates,
        IReadOnlyList<EnemyTraceGateState>? referenceGates,
        int frameIndex,
        int tick)
    {
        int referenceCount = referenceGates?.Count ?? 0;
        if (simulationGates.Count != referenceCount)
        {
            return Mismatch(
                frameIndex,
                tick,
                "gate-count",
                $"simulation={simulationGates.Count} reference={referenceCount}");
        }

        if (referenceGates == null)
            return VisualReplayMismatch.None;

        foreach (EnemyTraceGateState reference in referenceGates)
        {
            SimulationGateState? simulation = FindSimulationGate(simulationGates, reference.gate_id);
            if (simulation == null)
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "gate-missing",
                    $"gateId={reference.gate_id} missing in simulation");
            }

            if (!SameText(simulation.Orientation, reference.orientation))
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "gate-orientation",
                    $"gateId={reference.gate_id} simulation={simulation.Orientation ?? "--"} reference={reference.orientation ?? "--"}");
            }

            if (simulation.PivotX != reference.pivot_x || simulation.PivotY != reference.pivot_y)
            {
                return Mismatch(
                    frameIndex,
                    tick,
                    "gate-pivot",
                    $"gateId={reference.gate_id} simulation=({simulation.PivotX},{simulation.PivotY}) reference=({reference.pivot_x},{reference.pivot_y})");
            }
        }

        return VisualReplayMismatch.None;
    }

    private static SimulationActorState? FindSimulationEnemy(IReadOnlyList<SimulationActorState> enemies, int slot)
    {
        foreach (SimulationActorState enemy in enemies)
        {
            if (enemy.Slot == slot)
                return enemy;
        }

        return null;
    }

    private static EnemyTraceActor? FindReferenceEnemy(IReadOnlyList<EnemyTraceActor>? enemies, int slot)
    {
        if (enemies == null)
            return null;

        foreach (EnemyTraceActor enemy in enemies)
        {
            if (enemy.slot == slot)
                return enemy;
        }

        return null;
    }

    private static SimulationGateState? FindSimulationGate(IReadOnlyList<SimulationGateState> gates, int gateId)
    {
        foreach (SimulationGateState gate in gates)
        {
            if (gate.GateId == gateId)
                return gate;
        }

        return null;
    }

    private static VisualReplayMismatch Mismatch(int frameIndex, int tick, string category, string details)
    {
        return new VisualReplayMismatch
        {
            HasMismatch = true,
            FrameIndex = frameIndex,
            Tick = tick,
            Category = category,
            Details = details
        };
    }

    private static bool SameText(string? a, string? b)
    {
        return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return -1;

        string value = dir.Trim().ToLowerInvariant();
        // Visual trace convention used by the debug board and MAME trace logs:
        // 01 = left, 02 = down, 04 = right, 08 = up.
        return value switch
        {
            "left" => 0x01,
            "down" => 0x02,
            "right" => 0x04,
            "up" => 0x08,
            _ => TryParseHexByte(value, out int parsed) ? parsed & 0x0F : -1
        };
    }

    private static bool TryParseHexByte(string value, out int parsed)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        return int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out parsed);
    }

    private static string FormatDir(string? dir)
    {
        int normalized = NormalizeDir(dir);
        return normalized < 0 ? (dir ?? "--") : normalized.ToString("X2");
    }

    private static string Hex(int value)
    {
        return value < 0 ? "--" : (value & 0xFF).ToString("X2");
    }

    private static string FormatLairContext(SimulationActorState? simulation, EnemyTraceActor? reference)
    {
        bool inLair = false;
        if (simulation != null)
            inLair |= IsLairReleaseZone(simulation.X, simulation.Y);
        if (reference != null)
            inLair |= IsLairReleaseZone(reference.x, reference.y);

        return inLair ? " [lair-release-zone]" : string.Empty;
    }

    /// <summary>
    /// Conservative raw-MAME-coordinate zone around the central enemy release lane.
    /// This is not a skip rule: it only labels mismatches so the special den-exit
    /// sequence is visible in the log.
    /// </summary>
    private static bool IsLairReleaseZone(int x, int y)
    {
        return x >= 0x48 && x <= 0x68 && y >= 0x80 && y <= 0x98;
    }
}
