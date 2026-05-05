# Enemy activation / den-exit diagnostics

`v0.6.75` adds a diagnostic-only detector for frames where a reference enemy slot becomes active.

Why this exists
---------------

A save-state taken just before an enemy leaves the den can produce a special-looking sequence:

- the enemy becomes active while still constrained by the den corridor;
- direction is forced upward (`08`);
- the shared `EnemyWork` scratch tuple may contain a non-zero rejected mask;
- fallback/rejection values can differ from the normal free-roaming decision path.

This is useful reverse-engineering data, but it should not be interpreted as a normal movement-decision mismatch.

What the patch does
-------------------

`LadyBugSimulationState` now tracks:

```text
enemy activations=...
den-exit candidates=...
first den-exit candidate: ...
```

The first den-exit candidate includes:

```text
tick=...
mameFrame=...
pc=...
r=...
slot=...
raw=...
enemyXY=(..,..)
enemyDir=...
tempDir=...
tempX=...
tempY=...
rejectedMask=...
preferred=[..]
activeEnemies=...
```

Current heuristic
-----------------

A frame is classified as a den-exit candidate when:

```text
- an enemy slot just became active;
- EnemyWork tempDir/tempX/tempY matches that enemy;
- the direction is 08;
- EnemyWork rejectedMask is non-zero.
```

This is deliberately a diagnostic heuristic, not final arcade logic.

Simulation behavior
-------------------

This patch does not change the simulation result.

It only enriches the adapter summary. Authoritative `EnemyWork.preferred[]` is still reference-synced, and the preferred[] shadow model remains diagnostic-only.
