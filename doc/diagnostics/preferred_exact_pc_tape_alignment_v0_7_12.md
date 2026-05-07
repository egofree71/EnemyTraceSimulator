# v0.7.12 — preferred[] exact-PC tape alignment diagnostic

This diagnostic keeps the stable two-capture workflow introduced in v0.7.11:

1. Generate the standard JSONL trace with `ladybug_sequence_trace.lua` and MAME debugger disabled.
2. Generate the exact-PC EnemyWork diagnostic with `ladybug_enemywork_pc_trace.lua`.
3. Load the standard trace and run the normal comparison.

v0.7.11 proved that the exact-PC preferred[] tape can be imported from `tools/mame/lua/error.log` and that the source-first preferred[] generator model is internally consistent.

v0.7.12 adds a cautious sequence-alignment diagnostic:

- Reconstruct the active-frame preferred[] provider sequence from the standard trace.
- Reconstruct complete 0x2E5C preferred[] generator calls from the exact-PC tape.
- Search for the best contiguous tuple window in the exact-PC call sequence.
- Report tuple matches/mismatches and source-label matches/mismatches.

This is not yet a frame-accurate timing alignment. Raw `error.log` LBEW lines do not carry the standard JSONL tick number. A perfect tuple window means that the external exact-PC tape contains a sequence compatible with the active standard trace window, but it does not yet prove the exact tick of every 0x2E5C call.

Expected test setup:

```text
Standard trace:
Lua script path: res://tools/mame/lua/ladybug_sequence_trace.lua
Trace output prefix: ladybug_sequence_v8_fullmem
Frames after tick 0: 500
Include full memory each frame: true
Include logical maze each frame: true
Enable MAME debugger: false

Exact-PC diagnostic:
Lua script path: res://tools/mame/lua/ladybug_enemywork_pc_trace.lua
Trace output prefix: ladybug_sequence_v8_enemywork_pcdiag
Frames after tick 0: 600
```

Then load:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

and run:

```text
Compare > Lady Bug reference-direction step
```

New summary block:

```text
Lady Bug preferred[] exact-PC tape alignment v0.7.12:
...
bestWindowTupleMatches=...
bestWindowTupleMismatches=...
...
```

Interpretation:

- `bestWindowTupleMismatches=0` would mean the standard active-frame sequence is represented by a contiguous sequence of modeled exact-PC calls.
- Non-zero mismatches do not automatically invalidate the previous v0.7.11 result; they mean we need a stronger timing key before replacing the standard replay/classifier provider.
- This diagnostic is still shadow-only and must not be used as an authoritative preferred[] provider yet.
