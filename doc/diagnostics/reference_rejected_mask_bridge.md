# Reference rejectedMask bridge

`v0.6.77` extends the temporary EnemyWork bridge.

Why
---

The den-exit trace showed that `v0.6.76` reduced the comparison from hundreds of
mismatches to only a few remaining `rejectedMask` mismatches:

```text
comparedFrames=501
mismatches=5
First mismatch:
tick=37
field=rejectedMask
expected=00
actual=04
```

At the same time, the important systems were already matching:

```text
preferred[] shadow model checks=496
matches=496
mismatches=0
```

and enemy position/direction matched in the mismatch timeline.

Interpretation
--------------

`rejectedMask` is still a scratch field from the wall-rejection / fallback
decision pipeline. The current adapter only has a partial model of that logic.
Keeping a partial rejectedMask model active creates misleading mismatches while
the preferred[] and movement validation are already clean.

Patch behavior
--------------

Until the real rejection/fallback generator is implemented, the adapter now
reference-syncs `rejectedMask` from MAME, like it already does for:

- fallbackMask;
- preferred[];
- chaseTimers;
- chaseRoundRobin.

Expected result
---------------

On the den-exit trace, the comparison should now be clean or leave only genuinely
new mismatches.

The adapter summary should include:

```text
reference rejectedMask syncs=...
reference fallbackMask syncs=...
preferred[] shadow model checks=496, matches=496, mismatches=0
```

This is still a temporary bridge, not the final enemy decision logic.
