using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// v0.9.0c diagnostic model.
///
/// Compares static direction availability between:
/// - MAME logical maze from rawMemory.logicalMaze6200_62AF;
/// - Godot static maze.json model.
///
/// Important scope rules:
/// - compare only when the enemy is at an arcade decision center;
/// - skip the den / lair exit zone;
/// - skip movement boundaries touched by a rotating gate pivot.
///
/// The last rule is the v0.9.0c correction. The 0x6200 logical maze captured in
/// the trace can already reflect runtime gate state, while maze.json is meant to
/// describe only the static maze. Gate-local probes are therefore dynamic-terrain
/// questions and must not be counted as static maze mismatches.
/// </summary>
public static class LadyBugStaticMazeCollisionComparisonModel
{
    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames == null || referenceFrames.Count == 0)
            return "Lady Bug static maze collision comparison v0.9.0c: no frames";

        LadyBugGodotStaticMazeOracle godotOracle;
        try
        {
            godotOracle = new LadyBugGodotStaticMazeOracle();
        }
        catch (Exception ex)
        {
            return "Lady Bug static maze collision comparison v0.9.0c: " +
                   $"could not initialize Godot static maze oracle: {ex.Message}";
        }

        var stats = new StaticMazeCollisionStats();

        for (int frameIndex = 0; frameIndex < referenceFrames.Count; frameIndex++)
        {
            EnemyTraceFrame frame = referenceFrames[frameIndex];

            if (frame.enemies == null || frame.enemies.Count == 0)
                continue;

            LadyBugMameLogicalMazeOracle mameOracle = new(frame);
            if (!mameOracle.HasMemory)
                stats.MissingMameMemoryFrames++;

            bool frameHadActiveEnemy = false;

            foreach (EnemyTraceActor enemy in frame.enemies)
            {
                if (!enemy.active || !enemy.HasKnownPosition)
                    continue;

                frameHadActiveEnemy = true;
                stats.ActiveEnemyStates++;

                if (!mameOracle.IsDecisionCenter(enemy.x, enemy.y))
                {
                    stats.OutsideDecisionCenterSkipped++;
                    continue;
                }

                stats.DecisionCenterEnemyStates++;

                foreach (int direction in LadyBugDirectionBits.AllDirections)
                {
                    stats.Probes++;

                    EnemyCollisionProbeResult mame = mameOracle.Probe(enemy.x, enemy.y, direction);
                    EnemyCollisionProbeResult godot = godotOracle.Probe(enemy.x, enemy.y, direction);

                    if (mame.BlockKind == "missing-memory")
                    {
                        stats.MissingMameMemoryProbes++;
                        continue;
                    }

                    if (mame.BlockKind == "lair-exit-zone" || godot.BlockKind == "lair-exit-zone")
                    {
                        stats.LairZoneSkipped++;
                        continue;
                    }

                    if (IsGateAffectedProbe(frame, mame.CellX, mame.CellY, direction, out string gateReason))
                    {
                        stats.GateAffectedSkipped++;

                        if (stats.FirstGateAffectedSkipped == null)
                        {
                            stats.FirstGateAffectedSkipped = BuildGateAffectedText(
                                frameIndex,
                                frame,
                                enemy,
                                direction,
                                mame,
                                godot,
                                gateReason);
                        }

                        continue;
                    }

                    stats.ComparableProbes++;

                    if (mame.Allowed == godot.Allowed)
                    {
                        stats.Matches++;
                        continue;
                    }

                    stats.Mismatches++;

                    if (stats.FirstMismatch == null)
                    {
                        stats.FirstMismatch = BuildMismatchText(
                            frameIndex,
                            frame,
                            enemy,
                            direction,
                            mame,
                            godot);
                    }
                }
            }

            if (frameHadActiveEnemy)
                stats.ActiveEnemyFrames++;
        }

        return stats.BuildSummary(referenceFrames.Count);
    }

    private static bool IsGateAffectedProbe(
        EnemyTraceFrame frame,
        int cellX,
        int cellY,
        int direction,
        out string reason)
    {
        reason = "none";

        if (frame.gates == null || frame.gates.Count == 0)
            return false;

        int dir = direction & 0x0F;

        if (dir == LadyBugDirectionBits.Left || dir == LadyBugDirectionBits.Right)
        {
            int boundaryX = dir == LadyBugDirectionBits.Left ? cellX : cellX + 1;

            if (TryFindGateAtPivot(frame, boundaryX, cellY, out EnemyTraceGateState? gateTop))
            {
                reason = FormatGateReason(gateTop!, boundaryX, cellY, "vertical-boundary-top");
                return true;
            }

            if (TryFindGateAtPivot(frame, boundaryX, cellY + 1, out EnemyTraceGateState? gateBottom))
            {
                reason = FormatGateReason(gateBottom!, boundaryX, cellY + 1, "vertical-boundary-bottom");
                return true;
            }

            return false;
        }

        if (dir == LadyBugDirectionBits.Up || dir == LadyBugDirectionBits.Down)
        {
            int boundaryY = dir == LadyBugDirectionBits.Up ? cellY : cellY + 1;

            if (TryFindGateAtPivot(frame, cellX, boundaryY, out EnemyTraceGateState? gateLeft))
            {
                reason = FormatGateReason(gateLeft!, cellX, boundaryY, "horizontal-boundary-left");
                return true;
            }

            if (TryFindGateAtPivot(frame, cellX + 1, boundaryY, out EnemyTraceGateState? gateRight))
            {
                reason = FormatGateReason(gateRight!, cellX + 1, boundaryY, "horizontal-boundary-right");
                return true;
            }
        }

        return false;
    }

    private static bool TryFindGateAtPivot(
        EnemyTraceFrame frame,
        int pivotX,
        int pivotY,
        out EnemyTraceGateState? gate)
    {
        gate = null;

        if (frame.gates == null)
            return false;

        foreach (EnemyTraceGateState candidate in frame.gates)
        {
            if (candidate.pivot_x == pivotX && candidate.pivot_y == pivotY)
            {
                gate = candidate;
                return true;
            }
        }

        return false;
    }

    private static string FormatGateReason(
        EnemyTraceGateState gate,
        int pivotX,
        int pivotY,
        string boundaryKind)
    {
        return $"gateId={gate.gate_id} pivot=({pivotX},{pivotY}) " +
               $"orientation={gate.orientation ?? "unknown"} boundary={boundaryKind}";
    }

    private static string BuildGateAffectedText(
        int frameIndex,
        EnemyTraceFrame frame,
        EnemyTraceActor enemy,
        int direction,
        EnemyCollisionProbeResult mame,
        EnemyCollisionProbeResult godot,
        string gateReason)
    {
        var builder = new StringBuilder();

        builder.Append("firstGateAffectedSkipped: ");
        builder.Append($"frameIndex={frameIndex} tick={frame.frame} enemySlot={enemy.slot} ");
        builder.Append($"posMame=({enemy.x:X2},{enemy.y:X2}) ");
        builder.Append($"godotY={MameTraceCoordinates.MameToGodotArcadeY(enemy.y) & 0xFF:X2} ");
        builder.Append($"dir={LadyBugDirectionBits.ToLabel(direction)} ");
        builder.Append($"cell=({mame.CellX},{mame.CellY}) ");
        builder.Append($"gate=[{gateReason}] ");
        builder.Append($"MAME={(mame.Allowed ? "allowed" : "blocked")}({mame.BlockKind}) ");
        builder.Append($"Godot={(godot.Allowed ? "allowed" : "blocked")}({godot.BlockKind}) ");
        builder.Append($"mameDetails=[{mame.Details}] ");
        builder.Append($"godotDetails=[{godot.Details}]");

        return builder.ToString();
    }

    private static string BuildMismatchText(
        int frameIndex,
        EnemyTraceFrame frame,
        EnemyTraceActor enemy,
        int direction,
        EnemyCollisionProbeResult mame,
        EnemyCollisionProbeResult godot)
    {
        var builder = new StringBuilder();

        builder.Append("firstMismatch: ");
        builder.Append($"frameIndex={frameIndex} tick={frame.frame} enemySlot={enemy.slot} ");
        builder.Append($"posMame=({enemy.x:X2},{enemy.y:X2}) ");
        builder.Append($"godotY={MameTraceCoordinates.MameToGodotArcadeY(enemy.y) & 0xFF:X2} ");
        builder.Append($"dir={LadyBugDirectionBits.ToLabel(direction)} ");
        builder.Append($"MAME={(mame.Allowed ? "allowed" : "blocked")}({mame.BlockKind}) ");
        builder.Append($"Godot={(godot.Allowed ? "allowed" : "blocked")}({godot.BlockKind}) ");
        builder.Append($"mameCell=({mame.CellX},{mame.CellY}) ");
        builder.Append($"godotCell=({godot.CellX},{godot.CellY}) ");
        builder.Append($"mameDetails=[{mame.Details}] ");
        builder.Append($"godotDetails=[{godot.Details}]");

        return builder.ToString();
    }

    private sealed class StaticMazeCollisionStats
    {
        public int ActiveEnemyFrames;
        public int ActiveEnemyStates;
        public int DecisionCenterEnemyStates;
        public int OutsideDecisionCenterSkipped;
        public int Probes;
        public int ComparableProbes;
        public int Matches;
        public int Mismatches;
        public int LairZoneSkipped;
        public int GateAffectedSkipped;
        public int MissingMameMemoryFrames;
        public int MissingMameMemoryProbes;
        public string? FirstMismatch;
        public string? FirstGateAffectedSkipped;

        public string BuildSummary(int totalFrames)
        {
            var builder = new StringBuilder();

            builder.Append("Lady Bug static maze collision comparison v0.9.0c: ");
            builder.Append($"frames={totalFrames}, ");
            builder.Append($"activeEnemyFrames={ActiveEnemyFrames}, ");
            builder.Append($"activeEnemyStates={ActiveEnemyStates}, ");
            builder.Append($"decisionCenterEnemyStates={DecisionCenterEnemyStates}, ");
            builder.Append($"outsideDecisionCenterSkipped={OutsideDecisionCenterSkipped}, ");
            builder.Append($"probes={Probes}, ");
            builder.Append($"comparableProbes={ComparableProbes}, ");
            builder.Append($"matches={Matches}, ");
            builder.Append($"mismatches={Mismatches}, ");
            builder.Append($"lairZoneSkipped={LairZoneSkipped}, ");
            builder.Append($"gateAffectedSkipped={GateAffectedSkipped}, ");
            builder.Append($"missingMameMemoryFrames={MissingMameMemoryFrames}, ");
            builder.Append($"missingMameMemoryProbes={MissingMameMemoryProbes}, ");

            builder.Append(FirstMismatch ?? "firstMismatch: none");

            if (FirstGateAffectedSkipped != null)
            {
                builder.Append(", ");
                builder.Append(FirstGateAffectedSkipped);
            }

            return builder.ToString();
        }
    }
}
