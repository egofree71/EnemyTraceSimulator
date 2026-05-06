# Load trace path from MAME settings

`v0.6.88` fixes the `Charger trace` default target after restarting Godot.

## Problem

The simulator stored this hard-coded default trace path:

```text
res://traces/mame/ladybug_sequence_v8_trace.jsonl
```

When MAME/Lua was launched from the same UI session, the launcher updated the current trace path correctly.

But after restarting the simulator and clicking `Charger trace` directly, the UI loaded the hard-coded default trace again, even if the current settings used another prefix such as:

```text
outputPrefix = ladybug_sequence_v8_test2
```

## Fix

A new partial class initializes `_currentTracePath` from:

```text
config/mame_trace_settings.json
```

before the UI is ready.

It builds the expected standard JSONL trace path as:

```text
{outputDirectory}/{outputPrefix}_trace.jsonl
```

Example:

```text
outputDirectory = res://traces/mame
outputPrefix    = ladybug_sequence_v8_test2

=> res://traces/mame/ladybug_sequence_v8_test2_trace.jsonl
```

## Important

This patch does not change trace generation.

It only changes what `Charger trace` tries to load after restarting Godot.

The fix is intentionally implemented as a separate partial class to avoid replacing the large `EnemyTraceSimulatorWindow.cs` file.
