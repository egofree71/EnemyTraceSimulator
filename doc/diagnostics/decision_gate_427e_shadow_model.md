# v0.7.00 — Source-first 0x427E decision-gate shadow model

## Purpose

This package turns the v0.6.99 exact-PC finding into a non-invasive C# shadow model.

v0.6.98 still used the simple pixel-center predicate:

```text
(x & 0x0F) == 0x08
(y & 0x0F) == 0x06
```

That was not source-faithful enough. The Z80 caller at `0x42DA..0x42E0` does not branch on pixel alignment directly. It calls `0x427E` and uses the carry flag:

```text
0x42DA  CALL 0x427E
0x42DD  JP NC,0x433A
0x42E0  CALL 0x42E6
```

So only carry set enters preferred-direction logic. Carry clear enters the outside-center path at `0x433A`.

## Added file

```text
scripts/tools/simulation/LadyBugEnemyDecisionGate427EModel.cs
```

It transcribes:

```text
0x427F..0x428B  pixel alignment
0x428D..0x429C  helper selection by direction bits
0x377A          X-axis helper using table 0x0DFA
0x36DA          Y-axis helper using table 0x0DE4
0x429F..0x42B3  bitfield iteration and compare
0x3703          CP B / CCF
0x42B6          carry-set return
0x42B8          carry-clear return
```

The model uses the ROM helper tables exactly as little-endian words:

```text
0x0DFA table: 05E5, 07FF, 07FF, 07FE, 03DF, 0575, 03DF, 07FE, 07FF, 07BB, 05E5
0x0DE4 table: 0777, 03DE, 05FD, 03DE, 03FE, 07AF, 05FD, 07DF, 07FF, 03DE, 07AF
```

## Modified shadow

```text
scripts/tools/simulation/LadyBugEnemyLocalDoor4130ShadowModel.cs
```

v0.7.00 still checks only the same pixel-aligned candidate cycles as v0.6.98, but before calling `TryPreferredDirection()` it now evaluates the full source gate:

```text
if 0x427E carry set:
    run 0x42E0 / 0x42E6 preferred-direction model
else:
    run 0x433A outside-center keep-or-reverse path
```

This is the expected fix for the v0.6.98 mismatch where the C# shadow treated `08:48,86` as a true decision center while MAME kept direction `08` and moved to `48,87`.

## Expected result

Run the standard full-memory JSONL trace:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Then run:

```text
Compare > Lady Bug reference-direction step
```

Expected main comparison:

```text
comparedFrames=501
mismatches=0
```

Expected shadow trend:

```text
Lady Bug source-first 0x4130 local-door shadow v0.7.00:
checks=31
mismatches=0
pixelAlignedCandidates=31
```

The exact `decisionGateCarrySet` / `decisionGateCarryClear` counts depend on the trace length, but for the current 501-frame full-memory trace we expect one formerly-problematic pixel-aligned cycle to go through `0x433A` instead of `0x42E0`.

## Non-goals

This package still does not make the enemy decision authoritative. The main adapter remains reference-synced for direction, `preferred[]`, `rejectedMask`, fallback helper, chase timers, and chase round-robin. This is still a shadow diagnostic.
