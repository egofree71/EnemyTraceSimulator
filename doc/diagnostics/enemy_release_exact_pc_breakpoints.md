# Enemy release exact-PC breakpoints — v0.6.91

This package extends the existing `ladybug_enemywork_pc_trace.lua` diagnostic with release / den-exit breakpoints.

It intentionally keeps the same Lua script filename:

```text
tools/mame/lua/ladybug_enemywork_pc_trace.lua
```

That means the existing launcher path still auto-enables MAME debugger/log mode and still generates the usual:

```text
traces/mame/<outputPrefix>_enemywork_pc_analysis.txt
tools/mame/lua/error.log
```

## Added release breakpoints

```text
05AE_RELEASE_HELPER_ENTRY
05B6_RELEASE_SLOT_TEST
05C3_RELEASE_CALL_3061
05CC_RELEASE_RAW81_WRITE
3061_RELEASE_INIT_ENTRY
3070_RELEASE_INIT_RAW82
3074_RELEASE_INIT_X58
3078_RELEASE_INIT_Y86
3080_RELEASE_INIT_SPRITE
4471_ALT_RELEASE_CALL_3061
43D4_COMMIT_TEMP_STATE
```

These are diagnostic-only. They do not change the simulator, movement, `rejectedMask`, or any reference-sync bridge.

## What to check in the generated report

Look in:

```text
traces/mame/<outputPrefix>_enemywork_pc_analysis.txt
```

and in raw form:

```text
tools/mame/lua/error.log
```

The key question is the exact ordering around the first enemy activation:

```text
05AE / 05C3 / 3061 / 3070 / 3074 / 3078
then possibly 05CC or 4471 context
then 4241 / 4315 / 4331 / 43D4
```

The goal is not to classify away the JSONL tick-5 mismatch. The goal is to prove the exact Z80 order that produces:

```text
source init candidate: raw=82 x=58 y=86
observed JSONL state: raw=82 x=58 y=87
enemyWork: dir=08 x=58 y=87 rejectedMask=02
```

Only after that should the simulator get a real source-based release implementation.
