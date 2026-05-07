# v0.7.03 — Source-first Enemy_UpdateOne shadow with 0x4189 clear-path accounting

This diagnostic keeps the v0.7.01 one-enemy `Enemy_UpdateOne` shadow and makes the `0x4189` forced-reversal probe visible in the standard JSONL comparison summary.

It remains shadow-only. The authoritative comparison still uses the reference-synced enemy direction, `preferred[]`, `rejectedMask`, and `0x61C2` helper.

## Source-first flow modeled

For each active transition, the model still follows:

```text
0x43F0..0x4405  load current enemy slot state into temp
0x42CC          reset 0x61C1 rejected mask
0x42CF          reset 0x61C2 helper
0x42DA          call 0x427E decision gate
0x42DD          JP NC,0x433A when 0x427E returns carry clear
0x42E0..0x4337  preferred/current/fallback decision when carry set
0x433A..0x4356  outside-center keep-or-forced-reverse path when carry clear
0x4189..0x4223  door-local forced-reversal probe used inside the 0x433A path
0x43BA..0x43CB  one-pixel temp movement and 0x61C2 increment
```

## What is new in v0.7.03

v0.7.01 already called `LadyBugEnemyDecisionModel.CheckDoorForcedReversal()` in the outside-center path, but the diagnostic summary only exposed the final `forcedReversalApplied` count.

v0.7.03 adds explicit counters:

```text
forcedReversalChecks
forcedReversalClear
forcedReversalSet
forcedReversalTileReads
forcedReversalTiles
```

This makes the static-player sequence clearer: `0x4189` is exercised, but only through the clear path.

## Current expected result

Use the same standard full-memory JSONL trace:

```text
res://tools/mame/lua/ladybug_sequence_trace.lua
includeFullMemoryEachFrame=true
outputPrefix=ladybug_sequence_v8_fullmem
```

Load:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Run:

```text
Compare > Lady Bug reference-direction step
```

Expected main pipeline:

```text
comparedFrames=501
mismatches=0
```

Expected v0.7.03 shadow trend:

```text
Lady Bug source-first Enemy_UpdateOne shadow v0.7.03:
sourceUpdateCandidates=496
matches=496
mismatches=0
forcedReversalChecks > 0
forcedReversalClear == forcedReversalChecks
forcedReversalSet=0
forcedReversalApplied=0
```

## Important limitation

This does **not** validate forced reversal at 100%.

The exact-PC diagnostic v0.7.02 observed `0x4189` calls and verified that all tile probes in the current static-player window return clear, but it did not observe a real `0x4222` carry-set return or the subsequent `0x4347` direction reversal.

That is intentional for the current milestone. We are validating a constrained scenario first:

```text
- static player
- primarily one active enemy
- first enemy release / movement sequence
- no deliberate attempt to trigger a forced reversal
```

The carry-set branch of `0x4189` should be validated later with a targeted trace, probably after the single-enemy static-player release sequence is fully reproduced and visualized.
