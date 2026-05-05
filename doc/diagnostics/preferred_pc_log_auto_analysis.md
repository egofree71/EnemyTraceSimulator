# preferred[] PC log auto-analysis

`v0.6.66` integrates `LadyBugPreferredPcLogAnalyzer` into the MAME launcher workflow.

When the selected Lua script is:

```text
res://tools/mame/lua/ladybug_preferred_pc_trace.lua
```

the launcher already enables:

```text
-debug
-log
-debugscript tools/mame/lua/ladybug_preferred_pc_debug_startup.cmd
```

After MAME exits or is killed by the watchdog, the launcher now reads:

```text
tools/mame/lua/error.log
```

and writes an analyzed report to:

```text
traces/mame/<outputPrefix>_preferred_pc_analysis.txt
```

For example:

```text
traces/mame/ladybug_sequence_v8_pcdiag_preferred_pc_analysis.txt
```

This does not replace the normal JSONL trace pipeline. It only automates the exact-PC preferred[] diagnostic report.
