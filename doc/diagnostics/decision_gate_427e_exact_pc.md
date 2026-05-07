# v0.6.99 — 0x427E decision-gate exact-PC diagnostic

This package adds exact-PC instrumentation around the complete `0x427E` decision gate.

The purpose is source-first validation, not a heuristic fix for the remaining v0.6.98 mismatch.
The C# shadow currently knows the simple alignment predicate:

```text
(x & 0x0F) == 0x08
(y & 0x0F) == 0x06
```

But the source block continues after those two checks. Once alignment passes, `0x427E` restores the saved direction from the alternate AF register, chooses a helper path based on the direction bits, calls the `0x377A` or `0x36DA` geometry helper, loops through `0x429F`, calls `0x3703`, and finally returns either carry set at `0x42B6` or carry clear at `0x42B8`.

The caller uses that carry result immediately:

```text
42DA CALL 427E
42DD JP NC,433A
42E0 CALL 42E6
```

So the next source-first correction must be based on the real carry result of `0x427E`, not on a special-case tick or on the final frame state.

## Added breakpoints

```text
42D2_LOAD_TEMP_BEFORE_DECISION_GATE
42DA_CALL_427E_DECISION_GATE
427E_DECISION_GATE_ENTRY
428D_DECISION_GATE_ALIGNMENT_PASSED
4292_DECISION_GATE_HORIZONTAL_HELPER
429A_DECISION_GATE_VERTICAL_HELPER
42AA_DECISION_GATE_HELPER_COMPARE
42B6_DECISION_GATE_RET_CARRY_SET
42B8_DECISION_GATE_RET_CARRY_CLEAR
42DD_AFTER_427E_BRANCH_POINT
42E0_ENTER_PREFERRED_DECISION
433A_ENTER_OUTSIDE_CENTER_PATH
```

## How to test

Use the exact-PC diagnostic Lua script:

```text
res://tools/mame/lua/ladybug_enemywork_pc_trace.lua
```

Suggested settings:

```text
outputPrefix = ladybug_sequence_v8_enemywork_pcdiag
framesAfterTick0 = 600
```

After MAME exits, inspect:

```text
traces/mame/ladybug_sequence_v8_enemywork_pcdiag_enemywork_pc_analysis.txt
tools/mame/lua/error.log
```

The report should show the new labels in `Hits by source` and `First events`.

## What to verify

The interesting mismatch from v0.6.98 was around `tick=373`, where the shadow treated `start=08:48,86` as a preferred-decision center and accepted preferred `02`, while MAME kept direction `08`.

Do **not** fix this by adding a tick-specific rule. The expected workflow is:

1. inspect whether the corresponding exact-PC cycle reaches `42E0_ENTER_PREFERRED_DECISION` or `433A_ENTER_OUTSIDE_CENTER_PATH`;
2. inspect whether `0x427E` returned via `42B6` or `42B8`;
3. only then transcribe the missing source behavior into C#.

## Status

Diagnostic only. No simulation logic is changed.
