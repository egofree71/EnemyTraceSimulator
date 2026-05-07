# v0.6.98 - Source-first 0x4130 local-door shadow model

## Purpose

This checkpoint keeps the v0.6.97 `0x4130` tile reader, but fixes the start-state reconstruction for normal decision-center cycles.

v0.6.97 proved that the per-frame VRAM reader can call the existing source-first `LadyBugEnemyDecisionModel.TryPreferredDirection()` with real tile values. However, the shadow started normal cycles by undoing the already-committed current frame. That is not source-first enough for fallback cycles.

## Source-first correction

The arcade flow does not start a normal `Enemy_UpdateOne` cycle from the final committed `EnemyWork` state.

The source flow is:

```text
0x42BD  CALL Enemy_LoadCurrentStateToTemp
0x42C0  BC = 0003
0x42C3  HL = 61BA
0x42C6  DE = 61BD
0x42C9  LDIR   ; copy 61BA..61BC -> 61BD..61BF
0x42CC  EnemyRejectedDirMask = 0
0x42CF  61C2 fallback helper = 0
0x42D2  load temp dir/x/y for decision
```

`Enemy_LoadCurrentStateToTemp` at `0x43F0..0x4405` derives:

```text
TempDir = slot raw high nibble
TempX   = slot + 1
TempY   = slot + 2
```

Therefore, for a standard JSONL transition `previousFrame -> currentFrame`, a normal shadow cycle must start from the **previous frame's enemy slot**, not from the current frame's post-commit `EnemyWork`.

## What changed

`LadyBugEnemyLocalDoor4130ShadowModel` now:

1. selects the same enemy slot as before from `currentFrame`;
2. reads that slot's state from `previousFrame`;
3. builds the scratch state from the previous slot's raw direction and X/Y;
4. checks the decision-center predicate on that source-loaded scratch;
5. runs the existing source-first `TryPreferredDirection()`;
6. applies the one-pixel movement step;
7. compares the resulting scratch state to `currentFrame.enemyWork`.

The release activation path remains special only because exact-PC already showed that the first active frame corresponds to a source release state from `0x3061` plus one update step.

## What did not change

The model is still shadow-only.

It does not replace:

- authoritative enemy direction;
- authoritative `rejectedMask`;
- authoritative `fallbackMask` / `0x61C2`;
- the main comparison path.

It also does not add tick-specific or mismatch-specific rules.

## Expected test

Use the standard JSONL trace with full memory enabled:

```text
luaScriptPath = res://tools/mame/lua/ladybug_sequence_trace.lua
outputPrefix = ladybug_sequence_v8_fullmem
includeFullMemoryEachFrame = true
```

Then load:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Run:

```text
Compare > Lady Bug reference-direction step
```

Expected main result:

```text
Comparison [Lady Bug reference-direction step]: comparedFrames=501, mismatches=0
```

The important summary is:

```text
Lady Bug source-first 0x4130 local-door shadow v0.6.98:
...
checks=...
matches=...
mismatches=...
fallbackSelected=...
```

If mismatches remain, they should be interpreted as source-model gaps, not as reasons to add frame-specific rules.

## Why this is safer than v0.6.97

v0.6.97 saw two shadow mismatches. The first one looked like this:

```text
tick=309
source=4130_DECISION_CENTER:4315_PREFERRED_REJECTED_CURRENT_KEPT
ref=03:01:08:48,67
model=01:01:08:48,67
```

The bad part was not the tile read. The bad part was the start-state reconstruction: using the final current direction made a fallback cycle look like a current-kept cycle.

v0.6.98 fixes that by following the source load path `0x43F0..0x4405`.
