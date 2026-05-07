# v0.7.16 — EnemyWork rejected/fallback unsynced shadow readiness

## Goal

v0.7.16 adds a non-invasive readiness diagnostic before removing the MAME sync bridge for:

```text
0x61C1 = EnemyRejectedDirMask / rejectedMask
0x61C2 = fallback helper / legacy fallbackMask field
```

The visible simulation is still unchanged. The comparison timeline remains reference-assisted.

## Why this exists

v0.7.14 proved that the full source-first `Enemy_UpdateOne` shadow can consume `preferred[slot]` from the imported exact-PC preferred[] tape. v0.7.15 then added a preflight summary showing that the full shadow is clean enough to try removing the rejected/fallback sync bridge.

v0.7.16 adds one more explicit checkpoint:

```text
Does the full Enemy_UpdateOne shadow already produce the same rejectedMask and 0x61C2 outcomes that SimulationState currently overwrites from MAME?
```

## Expected result

For the current static-player, one-active-enemy trace:

```text
Lady Bug EnemyWork rejected/fallback unsynced shadow v0.7.16:
modeledScratchChecks=496
modeledScratchMatches=496
modeledScratchMismatches=0
rejectedFallbackUnsyncedShadowClean=true
canTryRuntimeNoSyncPatch=true
```

## Important limit

The current trace still has:

```text
forcedReversalSet=0
```

So this does not validate the forced-reversal carry-set branch toward `0x4347`.

## Next step

The next patch can try an actual runtime no-sync experiment where `SimulationState` keeps modeled `rejectedMask` / `0x61C2` instead of immediately calling the MAME reference-sync bridge. That should still remain validation-gated until it passes with `mismatches=0`.
