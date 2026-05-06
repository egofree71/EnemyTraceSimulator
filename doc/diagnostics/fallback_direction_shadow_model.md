# Fallback direction shadow model

`v0.6.87` improves the enemy final-direction shadow model.

It removes the previous diagnostic source:

```text
FALLBACK_REFERENCE_DIRECTION_BRIDGE
```

for the currently validated one-enemy trace.

## Arcade basis

The fallback routine at `0x4241` scans direction bits in this order:

```text
01, 02, 04, 08
```

It skips directions already present in `0x61C1` / `EnemyRejectedDirMask`, then runs the maze and local-door checks.

## Current model

The shadow model now derives fallback direction candidates from:

```text
previousTempDir
rejectedMaskShadow
```

Current covered sources:

```text
FALLBACK_SCAN_MASK_04_SELECT_01
FALLBACK_SCAN_MASK_01_SELECT_02
FALLBACK_SCAN_MASK_03_SKIP_04_SELECT_08
```

The last source is still a narrow local-block shape: the standard JSONL trace does not expose each failed fallback probe, so the model records the observed case where mask `03` rejects left+down and the fallback scan skips right before selecting up.

## Important limitation

This is still a shadow model. Enemy movement still uses MAME's reference direction.

The next step is to validate this on more one-enemy traces.
