# Den-exit transient EnemyWork bridge

`v0.6.76` turns the den-exit detector from `v0.6.75` into a temporary comparison bridge.

Problem observed
----------------

A trace saved just before enemy release produced:

```text
preferred[] shadow model checks=496
matches=496
mismatches=0
```

but the global comparison still reported many `EnemyWork` mismatches, starting with:

```text
tick=5
field=rejectedMask
expected=04
actual=00
```

The timeline showed that:

- enemy position matched;
- enemy direction matched;
- preferred[] matched;
- the remaining mismatch was the den-exit transient `rejectedMask` and the fallback pair/mask.

Current interpretation
----------------------

The first active frame is a special enemy-release / den-exit sequence, not normal free-roaming decision logic.

The enemy is forced upward and the arcade writes special-looking scratch state:

```text
tempDir=08
tempX=58
tempY=87
rejectedMask=04
fallbackMask=01
```

Patch behavior
--------------

This patch keeps the current simulation behavior but adds two temporary bridges:

1. If a detected den-exit candidate has a transient `rejectedMask`, sync it from MAME for that special frame.
2. Sync `fallbackMask` from MAME while the fallback generator is still not implemented.

This matches the adapter description, which already says that the fallback pair is temporarily reference-synced.

Expected result
---------------

On the den-exit trace, the comparison should no longer be polluted by hundreds of fallback/rejected-mask mismatches.

The summary should include:

```text
preferred[] shadow model checks=496, matches=496, mismatches=0
enemy activations=1
den-exit candidates=1
den-exit transient rejectedMask syncs=1
reference fallbackMask syncs=...
```

Ideally the global comparison should return to:

```text
mismatches=0
```

or at least leave only genuinely new mismatches.
