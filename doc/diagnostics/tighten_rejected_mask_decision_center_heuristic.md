# Tighten rejectedMask decision-center heuristic

`v0.6.91b` replaces the discarded `v0.6.91` reference-clear bridge with a
stricter non-reference heuristic.

## Why

The second one-enemy trace showed that the previous standard JSONL heuristic was
too broad.

The old model assumed:

```text
decision center
preferred[0] != current direction
=> preferred[0] was rejected
=> rejectedMask |= preferred[0]
```

The exact-PC EnemyWork diagnostic disproved that assumption.

For the `test2` exact-PC run:

```text
4315_REJECT_OR_CANDIDATE: 8
4331_REJECT_OR_TEMPDIR: 7
4241_FALLBACK_ENTRY: 7
```

So there are only 8 real rejected-candidate cycles, and only one
"preferred rejected, current direction kept" cycle.

The standard JSONL shadow model was inventing two extra
`DECISION_CENTER_REJECT_PREFERRED` cases that do not correspond to exact-PC
writes.

## Change

When the enemy is at a decision center and keeps the current direction:

```text
previousTempDir == currentTempDir
preferred[0] != currentTempDir
```

the model no longer assumes that `preferred[0]` was rejected.

It now classifies the case as:

```text
DECISION_CENTER_KEEP_CURRENT_NO_REJECT_WRITE
```

and leaves the shadow `rejectedMask` clear:

```text
rejectedMaskShadow = 00
```

## Important

This is not the discarded `v0.6.91` bridge.

`v0.6.91` used the reference `rejectedMask` to decide when to clear the shadow.
This patch does not do that.

Instead, it tightens the local heuristic: `preferred[0]` alone is no longer
treated as proof that 4315 wrote to `61C1`.

## Expected validation

On `ladybug_sequence_v8_test2_trace.jsonl`, the expected result is:

```text
rejectedMask shadow model mismatches=0
enemy direction shadow model mismatches=0
comparison globale mismatches=0
```

This should also be re-tested against the previous validated one-enemy trace,
because that older trace had one or two genuine
"preferred rejected, current direction kept" cases.
