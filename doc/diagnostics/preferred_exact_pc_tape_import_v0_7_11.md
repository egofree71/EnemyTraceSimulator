# Lady Bug preferred[] exact-PC tape import v0.7.11

## Purpose

v0.7.10 tried to embed exact-PC preferred[] events directly into the standard JSONL trace by launching `ladybug_sequence_trace.lua` with the MAME debugger enabled. That did not work reliably: the capture stalled after tick 0 and produced only one JSONL frame.

v0.7.11 returns to the stable workflow:

1. Generate the standard JSONL trace without the debugger.
2. Generate the exact-PC EnemyWork diagnostic separately with `ladybug_enemywork_pc_trace.lua`.
3. Load the standard JSONL trace and run Compare.
4. The compare summary imports the newest `*_enemywork_pc_hits.log` companion file and validates the preferred[] generator tape offline.

This avoids mixing the frame-by-frame standard tracer and debugger breakpoints in the same MAME run.

## What is imported

The importer searches for the newest file matching:

```text
res://traces/mame/*_enemywork_pc_hits.log
```

It parses `LBEW|...` lines emitted by the exact-PC diagnostic and extracts the preferred[] generator sources:

```text
2E5C  preferred[] generator entry
2E8C  rotate branch
2E97  rotate write
2E9E  random branch
2EA5  true LD A,R value
2EC7  random write
2ECB  call BFS/chase override
46D8  BFS/chase override entry
477D  BFS/chase preferred[] write
```

The model validates:

```text
- base rotate writes against LadyBugMonsterPreferenceSystem.GenerateRotateBranch(...)
- random writes against the captured 2EA5 LD A,R values
- BFS target validity for IY=61C4..61C7
```

## Expected result when the companion log exists

After loading the normal JSONL trace and running Compare, the summary should include something like:

```text
Lady Bug preferred[] exact-PC tape import v0.7.11:
fileFound=true
preferredEvents > 0
entries2E5C > 0
randomRValues2EA5 > 0
baseWriteMismatches=0
randomPairMismatches=0
bfsOverrideInvalidTargets=0
```

## Expected result when the companion log is missing

If no exact-PC companion log exists yet, the compare still works and the summary says:

```text
fileFound=false
```

In that case, generate the diagnostic separately with:

```text
Lua script path:
res://tools/mame/lua/ladybug_enemywork_pc_trace.lua

Trace output prefix:
ladybug_sequence_v8_enemywork_pcdiag

Frames after tick 0:
600
```

Then reload the standard trace and run Compare again.

## Important limitation

v0.7.11 is still an import/validation bridge. It does not yet time-align every exact-PC preferred[] call back into the standard JSONL replay timeline. Therefore, it does not yet make the simulation fully autonomous.

The next step is to use the imported tape to replace the tuple classifier used by the preferred provider.
