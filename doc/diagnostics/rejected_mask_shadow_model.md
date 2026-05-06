# rejectedMask shadow model

`v0.6.84` adds an adapter-level shadow diagnostic for `0x61C1 / EnemyRejectedDirMask`.

This follows the same pattern as the preferred[] shadow model:

```text
compute a local candidate
compare it with the MAME reference value
keep the authoritative comparison value reference-synced
print a summary in the comparison result
```

## What is still reference-synced

The adapter still syncs the authoritative `RejectedMask` field from MAME after the
shadow check. This patch should not introduce normal comparison mismatches.

## New diagnostic fields

`SimulationEnemyWorkState` now has:

```text
RejectedMaskShadow
RejectedMaskShadowSource
```

These are diagnostic-only and are not intended to drive movement yet.

## Current source classes

The shadow model classifies each standard JSONL frame into source buckets such as:

```text
PLAIN_STEP
DECISION_CENTER_PREFERRED_ACCEPTED
DECISION_CENTER_REVERSE_IGNORED
DECISION_CENTER_REJECT_PREFERRED
DECISION_CENTER_REJECT_PREFERRED_AND_PREVIOUS
DECISION_CENTER_NO_PREFERRED0
SAFETY_PREVIOUS_02_TO_08
NO_REFERENCE_ENEMYWORK
```

## Expected comparison summary

After running:

```text
Compare > Run Lady Bug reference-direction step
```

the result summary should contain a section like:

```text
rejectedMask shadow model checks=...
matches=...
mismatches=...
sources: ...
```

If there is a mismatch, the first mismatch is reported with:

```text
tick
mameFrame
pc
r
activeEnemies
tempDir/tempX/tempY
source
reference rejectedMask
shadow rejectedMask
preferred[]
```

## Current limitation

This is not a full `61C1` implementation yet. It still uses reference `preferred[0]`
as the attempted candidate because preferred[] is still reference-synced in the
standard JSONL adapter.

The goal of this patch is measurement: determine how far the current rejectedMask
heuristic goes on the already validated one-enemy standard traces before replacing
MAME sync.
