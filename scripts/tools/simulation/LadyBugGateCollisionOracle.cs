using System;

/// <summary>
/// v0.9.1 rotating-gate collision oracle.
///
/// This is intentionally small and diagnostic-oriented. It answers the same
/// functional question as the static maze oracle, but only for the dynamic gate
/// part: does a rotating gate currently block movement across the logical-cell
/// boundary being crossed?
///
/// Gate geometry convention used by the simulator board:
/// - gate.pivot_x / gate.pivot_y is a logical maze intersection;
/// - a Vertical gate blocks horizontal movement across the vertical boundary at
///   x = pivot_x for the two segments y = pivot_y - 1 and y = pivot_y;
/// - a Horizontal gate blocks vertical movement across the horizontal boundary at
///   y = pivot_y for the two segments x = pivot_x - 1 and x = pivot_x.
///
/// This matches the visual debug renderer where a vertical gate is drawn through
/// the pivot and spans two logical cells, and a horizontal gate spans two logical
/// cells around the pivot.
/// </summary>
public sealed class LadyBugGateCollisionOracle
{
    public GateCollisionProbeResult Probe(EnemyTraceFrame frame, int cellX, int cellY, int direction)
    {
        int dir = direction & 0x0F;

        if (dir != LadyBugDirectionBits.Left &&
            dir != LadyBugDirectionBits.Up &&
            dir != LadyBugDirectionBits.Right &&
            dir != LadyBugDirectionBits.Down)
        {
            return new GateCollisionProbeResult
            {
                Blocks = false,
                TouchesGate = false,
                BlockKind = "invalid-direction",
                Details = $"dir={direction:X2}"
            };
        }

        if (frame.gates == null || frame.gates.Count == 0)
        {
            return new GateCollisionProbeResult
            {
                Blocks = false,
                TouchesGate = false,
                BlockKind = "none",
                Details = "no gates in frame"
            };
        }

        if (dir == LadyBugDirectionBits.Left || dir == LadyBugDirectionBits.Right)
        {
            int boundaryX = dir == LadyBugDirectionBits.Left ? cellX : cellX + 1;
            int segmentY = cellY;
            return ProbeVerticalBoundary(frame, boundaryX, segmentY, dir);
        }

        int boundaryY = dir == LadyBugDirectionBits.Up ? cellY : cellY + 1;
        int segmentX = cellX;
        return ProbeHorizontalBoundary(frame, segmentX, boundaryY, dir);
    }

    private static GateCollisionProbeResult ProbeVerticalBoundary(
        EnemyTraceFrame frame,
        int boundaryX,
        int segmentY,
        int direction)
    {
        GateCollisionProbeResult? firstTouchingNonBlocking = null;

        foreach (EnemyTraceGateState gate in frame.gates!)
        {
            if (gate.pivot_x != boundaryX)
                continue;

            bool spansSegment = segmentY == gate.pivot_y - 1 || segmentY == gate.pivot_y;
            if (!spansSegment)
                continue;

            bool isVertical = IsVertical(gate.orientation);
            bool isHorizontal = IsHorizontal(gate.orientation);

            var result = new GateCollisionProbeResult
            {
                Blocks = isVertical,
                TouchesGate = true,
                BlockKind = isVertical ? "rotating-gate" : "none",
                GateId = gate.gate_id,
                PivotX = gate.pivot_x,
                PivotY = gate.pivot_y,
                Orientation = gate.orientation ?? "unknown",
                BoundaryKind = "vertical-boundary",
                Details =
                    $"dir={LadyBugDirectionBits.ToLabel(direction)} boundaryX={boundaryX} segmentY={segmentY} " +
                    $"gateId={gate.gate_id} pivot=({gate.pivot_x},{gate.pivot_y}) " +
                    $"orientation={gate.orientation ?? "unknown"} " +
                    (isVertical
                        ? "vertical gate blocks horizontal movement"
                        : isHorizontal
                            ? "horizontal gate touches nearby pivot but does not block horizontal movement"
                            : "unknown gate orientation; treated as non-blocking")
            };

            if (isVertical)
                return result;

            firstTouchingNonBlocking ??= result;
        }

        return firstTouchingNonBlocking ?? new GateCollisionProbeResult
        {
            Blocks = false,
            TouchesGate = false,
            BlockKind = "none",
            BoundaryKind = "vertical-boundary",
            Details = $"no gate spans boundaryX={boundaryX} segmentY={segmentY}"
        };
    }

    private static GateCollisionProbeResult ProbeHorizontalBoundary(
        EnemyTraceFrame frame,
        int segmentX,
        int boundaryY,
        int direction)
    {
        GateCollisionProbeResult? firstTouchingNonBlocking = null;

        foreach (EnemyTraceGateState gate in frame.gates!)
        {
            if (gate.pivot_y != boundaryY)
                continue;

            bool spansSegment = segmentX == gate.pivot_x - 1 || segmentX == gate.pivot_x;
            if (!spansSegment)
                continue;

            bool isHorizontal = IsHorizontal(gate.orientation);
            bool isVertical = IsVertical(gate.orientation);

            var result = new GateCollisionProbeResult
            {
                Blocks = isHorizontal,
                TouchesGate = true,
                BlockKind = isHorizontal ? "rotating-gate" : "none",
                GateId = gate.gate_id,
                PivotX = gate.pivot_x,
                PivotY = gate.pivot_y,
                Orientation = gate.orientation ?? "unknown",
                BoundaryKind = "horizontal-boundary",
                Details =
                    $"dir={LadyBugDirectionBits.ToLabel(direction)} segmentX={segmentX} boundaryY={boundaryY} " +
                    $"gateId={gate.gate_id} pivot=({gate.pivot_x},{gate.pivot_y}) " +
                    $"orientation={gate.orientation ?? "unknown"} " +
                    (isHorizontal
                        ? "horizontal gate blocks vertical movement"
                        : isVertical
                            ? "vertical gate touches nearby pivot but does not block vertical movement"
                            : "unknown gate orientation; treated as non-blocking")
            };

            if (isHorizontal)
                return result;

            firstTouchingNonBlocking ??= result;
        }

        return firstTouchingNonBlocking ?? new GateCollisionProbeResult
        {
            Blocks = false,
            TouchesGate = false,
            BlockKind = "none",
            BoundaryKind = "horizontal-boundary",
            Details = $"no gate spans segmentX={segmentX} boundaryY={boundaryY}"
        };
    }

    private static bool IsVertical(string? orientation)
    {
        return string.Equals(orientation, "Vertical", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHorizontal(string? orientation)
    {
        return string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GateCollisionProbeResult
{
    public bool Blocks { get; init; }
    public bool TouchesGate { get; init; }
    public string BlockKind { get; init; } = "none";
    public int GateId { get; init; } = -1;
    public int PivotX { get; init; } = -1;
    public int PivotY { get; init; } = -1;
    public string Orientation { get; init; } = "none";
    public string BoundaryKind { get; init; } = "none";
    public string Details { get; init; } = string.Empty;
}
