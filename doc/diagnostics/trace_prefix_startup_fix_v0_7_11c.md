# v0.7.11c - Trace prefix startup fix

## Purpose

After the v0.7.10 experimental cleanup, the simulator could again try to load the old hard-coded trace path:

```text
res://traces/mame/ladybug_sequence_v8_trace.jsonl
```

instead of the configured trace generated with the current prefix:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

## Fix

Adds a small partial class:

```text
scripts/tools/EnemyTraceSimulatorWindow.TracePathSettings.cs
```

It reads:

```text
config/mame_trace_settings.json
```

on startup and initializes `_currentTracePath` from:

```text
OutputDirectory + OutputPrefix + "_trace.jsonl"
```

## Scope

This is UI path selection only. It does not change:

- the trace parser
- the MAME launcher
- the simulation adapter
- Enemy_UpdateOne shadow logic
- preferred[] exact-PC tape import logic
