# EnemyWork exact-PC diagnostics

`v0.6.80` adds a targeted MAME debugger diagnostic for the next reconstruction step:

```text
preferred[] -> rejectedMask -> fallbackMask -> final direction
```

The current adapter already reference-syncs `rejectedMask` and `fallbackMask`. This diagnostic does not replace that bridge yet. It captures exact-PC events around the arcade code paths that appear to produce or consume those scratch fields.

## Source basis

The focused reverse-engineering extract identifies these important RAM fields:

```text
61BD = EnemyTemp_Dir
61BE = EnemyTemp_X
61BF = EnemyTemp_Y
61C1 = EnemyRejectedDirMask
61C2 = EnemyFallback_WorkMask / fallback helper mask
61C4..61C7 = preferred[]
```

It also records validated runtime breakpoints:

```text
4187 = local door/tile rejection
4241 = generic fallback
4347 = forced reversal
477D = BFS/chase preferred[] override
```

This patch focuses on `4187`, `4241`, and `4347`, while also sampling the entry points `3911` and `4130` for context.

## New files

```text
tools/mame/lua/ladybug_enemywork_pc_trace.lua
scripts/tools/simulation/LadyBugEnemyWorkPcLogAnalyzer.cs
```

The launcher is updated so selecting `ladybug_enemywork_pc_trace.lua` automatically enables:

```text
-debug
-log
-debugscript ladybug_preferred_pc_debug_startup.cmd
```

## Breakpoints

```text
3911_LOGICAL_MAZE_VALIDATE
4130_LOCAL_DOOR_CHECK_ENTRY
4187_LOCAL_DOOR_REJECT
4241_FALLBACK
4347_FORCED_REVERSAL
```

`3911` and `4130` may be noisy. The important first signals are expected around:

```text
4187_LOCAL_DOOR_REJECT
4241_FALLBACK
4347_FORCED_REVERSAL
```

## Output

Raw primary output:

```text
tools/mame/lua/error.log
```

Generated report:

```text
traces/mame/<outputPrefix>_enemywork_pc_analysis.txt
```

The report counts:

```text
hits by source
hits by rejectedMask
hits by fallbackMask
rejectedMask changes
fallbackMask changes
first events
```

## Suggested settings

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_enemywork_pc_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_enemywork_pcdiag",
  "framesAfterTick0": 500
}
```

This is not a JSONL trace. Do not load it with **Charger trace**. The useful file is the generated `_enemywork_pc_analysis.txt` report.
