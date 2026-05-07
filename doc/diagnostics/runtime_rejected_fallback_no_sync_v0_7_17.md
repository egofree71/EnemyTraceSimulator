# v0.7.17 — runtime no-sync for rejectedMask / fallback helper

This is the first controlled runtime experiment after the v0.7.15 / v0.7.16 preflight checks.

## Goal

Stop overwriting these two EnemyWork fields with MAME at the end of `LadyBugSimulationState.UpdateEnemyWorkTempMovementFields()`:

```text
0x61C1 = rejectedMask
0x61C2 = fallback helper / legacy FallbackMask DTO field
```

The visible simulation remains reference-assisted for other fields:

```text
- enemy direction is still reference-driven
- preferred[] is still synced in SimulationState
- chase timers and round-robin remain synced
- player / gates / ports / timers remain external reference inputs
```

## Why this is now safe to try

v0.7.14 validated the full `Enemy_UpdateOne` shadow with the exact-PC aligned preferred[] provider:

```text
checks=496
matches=496
mismatches=0
preferredProviderMode=exact-PC-aligned
preferredProviderMatchesReference=496
```

v0.7.15 and v0.7.16 confirmed that the same shadow already reconstructs:

```text
rejectedMask / 0x61C1
fallback helper / 0x61C2
tempDir / tempX / tempY
```

for the current 496 active updates.

## Patch behavior

`RejectedMask` is already assigned from the modeled shadow candidate before the old sync call. v0.7.17 keeps that value.

`FallbackMask` now receives the modeled `fallbackHelperShadow` value before diagnostics continue.

The old sync calls are replaced by no-sync keep/check methods:

```text
KeepModeledRejectedMaskState(...)
KeepModeledFallbackState(...)
```

These methods count whether the modeled runtime value still matches the reference trace, but they do not overwrite it.

## Expected result

After applying the patch, reload:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Then run:

```text
Compare > Lady Bug reference-direction step
```

Expected:

```text
Comparison: comparedFrames=501, mismatches=0

preferred[] shadow model ...
runtime modeled rejectedMask keeps=496
runtime modeled rejectedMask differs=0
runtime modeled fallback helper keeps=496
runtime modeled fallback helper differs=0
```

## Limits

This is not a full enemy AI yet.

The current static-player trace still has:

```text
forcedReversalSet=0
```

So the `0x4189` carry-set path into `0x4347` is not validated by this patch.
