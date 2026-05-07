# v0.8.0 — Runtime preferred[] exact-PC no-sync

## Goal

v0.8.0 removes the runtime MAME-copy bridge for `EnemyWork.preferred[]` / `0x61C4..0x61C7` on the current validated one-enemy static-player trace.

Until v0.7.17, `LadyBugSimulationState` still copied `preferred[]` from the standard JSONL trace even though the source-first `Enemy_UpdateOne` shadow could already consume the imported exact-PC preferred tape.

This checkpoint feeds runtime `EnemyWork.Preferred` from:

```text
separate exact-PC EnemyWork diagnostic
-> tools/mame/lua/error.log
-> LadyBugPreferredExactPcAlignedProvider
-> active-frame aligned tuple window
-> LadyBugSimulationState.EnemyWork.Preferred
```

The normal JSONL trace remains debugger-free.

## Scope

This is a bigger milestone step than the v0.7.x micro-checkpoints, but it is still scoped:

```text
Still reference-assisted:
- enemy direction
- player / ports
- gates / timers
- chase timers
- chase round-robin
- release / den-exit slot activation

No longer immediately copied from MAME in runtime state for the validated trace:
- rejectedMask / 0x61C1 since v0.7.17
- fallback helper / 0x61C2 since v0.7.17
- preferred[] / 0x61C4..0x61C7 in v0.8.0 when provider is usable
```

A fallback to the old MAME preferred[] copy remains only if the exact-PC aligned provider is unavailable. In the expected validation path, fallback counters must stay at zero.

## Expected validation

Load:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Run:

```text
Compare > Lady Bug reference-direction step
```

Expected result:

```text
Comparison: comparedFrames=501, mismatches=0
```

Expected v0.8.0 runtime summary:

```text
preferred[] runtime exact-PC no-sync v0.8.0:
providerConfigured=true
providerUsable=true
bestWindowTupleMismatches=0
updates=496
matchesReference=496
differsFromReference=0
providerNotConfigured=0
providerUnavailable=0
missingFrame=0
missingReferencePreferred=0
fallbackToReference=0
clean=true
```

## Important limitation

The provider is still a replay provider backed by the exact `LD A,R` values captured in the companion exact-PC diagnostic. It is not yet a general autonomous emulation of the Z80 `R` register stream.

The current trace still has:

```text
forcedReversalSet=0
```

so the `0x4189` carry-set path into `0x4347` remains unvalidated.
