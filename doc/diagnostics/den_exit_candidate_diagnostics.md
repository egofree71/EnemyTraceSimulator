# Den-exit candidate diagnostics

`v0.6.75` adds a diagnostic-only detector for enemy activation / den-exit-like frames.

Why this exists
---------------

A trace that starts just before an enemy leaves the den can produce early
`EnemyWork` mismatches in fields such as:

```text
rejectedMask
fallbackMask
```

while the preferred[] shadow model is still perfectly valid.

Example observed trace:

```text
preferred[] shadow model checks=496
matches=496
mismatches=0

First mismatch:
tick=5
field=rejectedMask
expected=04
actual=00
```

The enemy appears at the den exit, is surrounded by walls on three sides, and is
forced upward. This is likely not normal free-roaming enemy decision logic.

What the patch does
-------------------

The adapter now tracks reference enemy slots that become active.

If a newly active enemy also matches this shape:

```text
enemy dir = 08
EnemyWork tempDir/tempX/tempY matches the newly active enemy
EnemyWork rejectedMask != 0
```

the adapter summary reports it as a possible den-exit candidate.

Expected new summary fragment
-----------------------------

On the den-exit trace, the adapter summary should now include something like:

```text
enemy activations=1
den-exit candidates=1
first den-exit candidate: tick=5 ...
```

This does not change simulation behavior.

Important
---------

The preferred[] shadow model remains the important result for this stage. If
`preferred[] shadow model ... mismatches=0`, then the preferred generator still
passes even if `rejectedMask` / `fallbackMask` mismatches appear during this
special sequence.
