# v0.7.11b — preferred[] exact-PC tape import fallback

v0.7.11 imported the separate exact-PC preferred[] tape from `*_enemywork_pc_hits.log`.
The latest test showed that this file can contain only a short Lua header when the
exact-PC diagnostic is watchdog-killed, while the actual `LBEW|...` debugger output
is still available in `tools/mame/lua/error.log`.

v0.7.11b keeps the stable two-capture workflow:

1. Generate the standard JSONL trace with `ladybug_sequence_trace.lua`, debugger off.
2. Generate the exact-PC diagnostic with `ladybug_enemywork_pc_trace.lua`.
3. Reload the standard JSONL trace and run Compare.

The importer now searches:

1. `res://tools/mame/lua/error.log`
2. `res://traces/mame/*_enemywork_pc_hits.log`
3. `res://tools/mame/lua/*_enemywork_pc_hits.log`

It chooses the newest usable file with `LBEW|` markers, preferring the real MAME
`error.log` output when the Lua-drained hits file has only headers.

Expected result after running the exact-PC diagnostic:

```text
Lady Bug preferred[] exact-PC tape import v0.7.11b:
fileFound=true
importedMarkerCount > 0
importedEvents > 0
preferredEvents > 0
entries2E5C > 0
randomRValues2EA5 > 0
baseWriteMismatches=0
randomPairMismatches=0
bfsOverrideInvalidTargets=0
```

This still does not time-align each exact-PC call back into the standard replay
timeline. It only proves that the external exact-PC tape is available and internally
source-consistent.
