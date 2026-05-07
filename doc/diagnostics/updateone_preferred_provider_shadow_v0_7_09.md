# v0.7.09 — Enemy_UpdateOne preferred-provider shadow

This diagnostic moves the preferred-direction bridge one step closer to the real
source-first enemy simulation.

## Goal

v0.7.08 proved that the value consumed by `Enemy_UpdateOne` as `preferred[slot]`
can be supplied by the replay/classifier provider on the current static-player,
single-enemy sequence.

v0.7.09 now uses that provider inside the full source-first `Enemy_UpdateOne`
shadow instead of reading the selected `preferred[slot]` directly from the MAME
`EnemyWork.preferred[]` tuple.

## What is still not autonomous

This is still a bridge, not a 100% autonomous enemy AI:

- the adapter visible comparison frames still keep `preferred[]` reference-synced;
- the replay provider still classifies the standard trace end-of-frame tuple;
- the standard JSONL trace does not yet carry an exact-PC `LD A,R` tape from
  `0x2EA5`;
- forced reversal carry-set / `0x4347` has not yet been observed in this static
  player sequence.

## Expected result

With the standard full-memory trace:

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
```

Run:

```text
Compare > Lady Bug reference-direction step
```

Expected summary excerpt:

```text
Comparison: comparedFrames=501, mismatches=0

Lady Bug source-first Enemy_UpdateOne shadow v0.7.09:
sourceUpdateCandidates=496
checks=496
matches=496
mismatches=0
preferredProviderChecks=496
preferredProviderMatchesReference=496
preferredProviderDiffersFromReference=0
preferredProviderSkips=0
```

## Interpretation

If the above passes, the full `Enemy_UpdateOne` shadow no longer depends on the
selected `preferred[slot]` being read directly from MAME. It consumes the
provider output instead and still reproduces the same `tempDir/tempX/tempY`,
`rejectedMask`, and fallback helper on the validated sequence.

The next milestone remains injecting a true exact-PC random tape into the
standard JSONL replay path, so the random branch can be replayed from captured
`0x2EA5 LD A,R` values instead of classified from the final tuple.
