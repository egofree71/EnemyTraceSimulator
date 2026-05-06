# EnemyWork decision-cycle classification

`v0.6.83` improves only the EnemyWork exact-PC analysis report.

It does not change the Lua trace script and does not add new MAME breakpoints.

## Why

`v0.6.82` groups exact-PC events into `Enemy_UpdateOne` cycles. The latest logs show
that the next useful view is a decision-level classification of those cycles.

The report now separates:

```text
plain step / no rejected candidate
preferred rejected, current direction kept
preferred/current rejected, fallback entered
forced reversal outside decision center
other / mixed
```

## Interpretation

The key observed pattern is:

```text
4315 without 4331/fallback
```

means:

```text
a preferred candidate was rejected,
but the current temp direction was still valid,
so the enemy kept moving in the current direction.
```

Whereas:

```text
4315 -> 4331 -> 4241
```

means:

```text
the preferred candidate was rejected,
then the current temp direction was also rejected,
then the fallback finder selected another direction.
```

## Expected sections

The generated analysis report should now include:

```text
Decision-cycle classification
Rejected preferred but current direction kept
Fallback outcomes
Decision interpretation hints
```

## Current trace expectation

For the latest one-enemy trace, the expected high-level classification should be close to:

```text
cycles: 677
preferred rejected, current direction kept: 2
preferred/current rejected, fallback entered: 8
forced reversal outside decision center: 0
```

The exact plain-step count depends on total captured cycles.

## Important limitation

`nextDir` is inferred from the next cycle's starting temp direction. It is useful for
cycle-level analysis, but it is not a separate exact-PC commit breakpoint.
