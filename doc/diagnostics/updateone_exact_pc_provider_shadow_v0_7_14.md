# v0.7.14 — Enemy_UpdateOne exact-PC preferred-provider shadow

## Purpose

v0.7.14 moves the exact-PC preferred-direction tape one step closer to the full enemy update model.

The previous checkpoints established the following chain:

```text
v0.7.11b  import preferred[] exact-PC tape from tools/mame/lua/error.log
v0.7.12   align the imported 0x2E5C call sequence with the standard active-frame trace
v0.7.13   validate selected preferred[slot] from that aligned tape
```

v0.7.14 now feeds the **full source-first Enemy_UpdateOne shadow** with that aligned exact-PC provider.

This replaces the v0.7.09 decision input:

```text
standard-trace tuple classifier
```

with:

```text
imported exact-PC 0x2E5C tape window aligned to the standard trace
```

The visible simulation remains reference-assisted. This is still a shadow diagnostic.

## Files

```text
scripts/tools/simulation/LadyBugPreferredExactPcAlignedProvider.cs
scripts/tools/simulation/LadyBugEnemyLocalDoor4130ShadowModel.cs
scripts/tools/simulation/LadyBugEnemySimulationAdapter.cs
```

## Stable workflow

Keep the standard JSONL trace debugger-free:

```text
Lua script path:
res://tools/mame/lua/ladybug_sequence_trace.lua

Trace output prefix:
ladybug_sequence_v8_fullmem

Frames after tick 0:
500

Include full memory each frame:
true

Include logical maze each frame:
true

Enable MAME debugger:
false
```

Generate the exact-PC companion diagnostic separately:

```text
Lua script path:
res://tools/mame/lua/ladybug_enemywork_pc_trace.lua

Trace output prefix:
ladybug_sequence_v8_enemywork_pcdiag

Frames after tick 0:
600
```

Then load:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

and run:

```text
Compare > Lady Bug reference-direction step
```

## Expected result

The main comparison should remain green:

```text
Comparison: comparedFrames=501, mismatches=0
```

The full Enemy_UpdateOne shadow should now report v0.7.14 and the exact-PC provider fields:

```text
Lady Bug source-first Enemy_UpdateOne shadow v0.7.14:
preferredProviderMode=exact-PC-aligned
preferredExactProviderUsable=true
preferredExactProviderBestWindowTupleMismatches=0
preferredProviderChecks=496
preferredProviderMatchesReference=496
preferredProviderDiffersFromReference=0
checks=496
matches=496
mismatches=0
```

## Interpretation

If this passes, the full source-first Enemy_UpdateOne shadow no longer uses the standard-trace tuple classifier as its preferred-direction decision input. It consumes `preferred[slot]` from the imported exact-PC tape window.

That is still not a fully autonomous simulation:

```text
- the visible enemy movement still follows the reference direction;
- the exact-PC tape is a replay artifact, not an independent Z80 R emulator;
- exact per-frame PC timing is still inferred by tuple-window alignment;
- the forced-reversal carry-set path 0x4222 -> 0x4347 is still not validated;
- multi-enemy validation is still outside the current stable target.
```

## Why this is useful

This is an important bridge-removal step.  The shadow decision model still validates against MAME state, but its `preferred[slot]` input is now supplied by a source-first exact-PC replay of the generator path:

```text
0x2E5C entry
0x2E97 rotate writes
0x2EA5 LD A,R values
0x2EC7 random writes
0x477D BFS/chase overwrites
```

So the remaining reference assistance is more explicit and localized.
