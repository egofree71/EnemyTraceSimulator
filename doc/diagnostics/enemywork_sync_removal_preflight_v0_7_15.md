# v0.7.15 — EnemyWork sync-removal preflight

## Goal

v0.7.15 does **not** remove another reference-sync bridge yet.

It adds an explicit preflight summary that answers a narrower question:

```text
Is the full source-first Enemy_UpdateOne shadow, now fed by the exact-PC aligned
preferred[] provider, clean enough to try removing the rejectedMask / 0x61C2
reference-sync in a later shadow branch?
```

## Why this is the right next step

v0.7.14 proved that the full `Enemy_UpdateOne` shadow can consume `preferred[slot]`
from the imported exact-PC tape window instead of the standard-trace tuple classifier.

The full shadow comparison already checks the modeled values against MAME for:

```text
- rejectedMask / 0x61C1
- fallback helper / 0x61C2
- tempDir / 0x61BD
- tempX / 0x61BE
- tempY / 0x61BF
```

So v0.7.15 makes that readiness explicit before changing `LadyBugSimulationState`.

## New summary

The comparison output now includes:

```text
Lady Bug EnemyWork sync-removal preflight v0.7.15
```

Expected result for the current one-enemy static-player trace:

```text
fullShadowClean=true
preferredInputClean=true
canTryRejectedFallbackUnsyncedShadow=true
```

## Important limitation

This remains shadow-only.

The visible comparison pipeline still syncs `rejectedMask`, `0x61C2`, `preferred[]`,
chase timers, chase round-robin, and enemy direction from MAME.

Also, the current trace validates the `0x4189` forced-reversal clear path only:

```text
forcedReversalSet=0
```

Therefore, the next implementation step should still be a controlled shadow branch,
not an authoritative simulation switch.
