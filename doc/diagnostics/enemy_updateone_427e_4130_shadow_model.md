# v0.7.01 — Source-first Enemy_UpdateOne shadow

This diagnostic broadens the v0.7.00 `0x427E / 0x4130` shadow from decision-center candidates to every active one-enemy `Enemy_UpdateOne` transition in the standard full-memory JSONL trace.

It remains shadow-only. The authoritative comparison still uses the reference-synced enemy direction, `preferred[]`, `rejectedMask`, and `0x61C2` helper.

## Source-first flow modeled

For each active transition, the model follows the source structure instead of reconstructing from the already-committed current frame:

```text
0x43F0..0x4405  load current enemy slot state into temp
0x42CC          reset 0x61C1 rejected mask
0x42CF          reset 0x61C2 helper
0x42DA          call 0x427E decision gate
0x42DD          JP NC,0x433A when 0x427E returns carry clear
0x42E0..0x4337  preferred/current/fallback decision when carry set
0x433A..0x4356  outside-center keep-or-forced-reverse path when carry clear
0x43BA..0x43CB  one-pixel temp movement and 0x61C2 increment
```

The release activation transition still uses the validated `0x3061` start state:

```text
raw=82, x=58, y=86, dir=08
```

## Why this exists

v0.7.00 validated the source-first `0x427E` gate on the 31 decision candidates:

```text
checks=31
matches=31
mismatches=0
decisionGateCarrySet=29
decisionGateCarryClear=2
fallbackSelected=8
```

v0.7.01 checks the same source-first path over all active one-enemy transitions, including the many outside-center `0x433A` plain movement cycles that were previously skipped.

## Trace requirement

Use the standard JSONL trace, not the exact-PC script:

```text
res://tools/mame/lua/ladybug_sequence_trace.lua
```

and enable full memory per frame:

```json
"includeFullMemoryEachFrame": true
```

The diagnostic needs `rawMemory.vramD000_D3FF` so `0x3C0A` tile probes can read the same VRAM bytes as MAME.

## Expected test

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

Expected new summary shape:

```text
Lady Bug source-first Enemy_UpdateOne shadow v0.7.01:
sourceUpdateCandidates=496
matches=496
mismatches=0
```

If mismatches appear, they should be treated as source-first evidence to investigate. Do not add tick-specific filters.
