# v0.7.02 — 0x4189 forced-reversal exact-PC diagnostic

This diagnostic extends `tools/mame/lua/ladybug_enemywork_pc_trace.lua` with source-first breakpoints around the forced-reversal path:

```text
0x433A outside-center path
0x4342 CALL Enemy_CheckDoorForcedReversal / 0x4189
0x4345 JR NC skip reversal
0x4347 reverse EnemyTemp_Dir
0x4356 write reversed direction back to 0x61BD
```

The goal is not to implement new gameplay logic yet. The goal is to capture the exact tile probes used by `0x4189` so the existing C# scaffold `CheckDoorForcedReversal()` can later be validated against MAME without adding trace-specific rules.

## Added tile-read breakpoints

The breakpoints are placed immediately after each `LD A,(HL)`, at the following `CP` instruction, so register `A` contains the tile value that the arcade code is about to compare.

```text
419E  down first probe   reject 4A / 45
41AF  down second probe  reject 41 / 46
41C2  left first probe   reject 45 / 46
41D2  left second probe  reject 4A / 41
41E5  up first probe     reject 49 / 43
41F5  up second probe    reject 41 / 46
4208  right first probe  reject 44 / 47
4218  right second probe reject 4A / 41
```

Additional control-flow breakpoints:

```text
4189_FORCED_REVERSAL_CHECK_ENTRY
4220_FORCED_REVERSAL_RET_CLEAR
4222_FORCED_REVERSAL_RET_SET
4342_CALL_FORCED_REVERSAL_CHECK
4345_AFTER_FORCED_REVERSAL_CHECK
4347_FORCED_REVERSAL
4356_FORCED_REVERSAL_DIR_WRITE
```

## Expected report section

The analyzer now emits:

```text
0x4189 forced-reversal tile-probe summary
```

A trace may legitimately show zero forced-reversal tile probes if the sequence does not reach that path. In that case, this package is still useful: it prepares the diagnostic for a future trace where `0x4347` or `0x4222` is actually reached.

## Source-first rule

The expected clear/force result is derived from the tile constants in the Z80 code, not from observed MAME outcomes. MAME logs are used only to confirm whether the source transcription matches the arcade behavior.
