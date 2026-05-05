# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The repository is separate from the main Lady Bug remake project. Its purpose is to compare a MAME reference trace with a C# / Godot simulation so that the arcade enemy behavior can be reimplemented progressively and safely.

## Current status

Current checkpoint: **v0.6.69**

The project currently supports two complementary workflows:

1. **Standard JSONL trace workflow**
   - exports frame-by-frame state from MAME;
   - loads the trace in Godot;
   - visually replays MAME state;
   - compares MAME frames with the C# simulation adapter.

2. **Exact-PC preferred[] diagnostic workflow**
   - uses MAME debugger breakpoints;
   - captures exact writes to `EnemyWork.preferred[]`;
   - writes raw `LBPREF` events to `tools/mame/lua/error.log`;
   - automatically generates a readable analysis report in `traces/mame`.

The standard JSONL trace remains the main comparison pipeline. The exact-PC workflow is a reverse-engineering aid used for precise CPU-level diagnostics.

## Implemented

- Godot 4.6.2 .NET tool project;
- visual trace viewer with two boards: simulation and MAME reference;
- compact toolbar with playback controls;
- MAME settings dialog;
- MAME launcher from inside Godot;
- Lua runtime configuration generation;
- MAME save-state loading;
- standard JSONL trace export and loading;
- rendering of maze, gates, player, and enemy slots;
- optional sprite rendering with fallback debug markers;
- current-frame dump window;
- trace navigation helper window;
- comparison window;
- identity and injected-mismatch comparison modes;
- Lady Bug comparison adapter skeleton;
- one-active-enemy validation path;
- JSONL diagnostics for `EnemyWork.preferred[]`;
- exact-PC diagnostics for `EnemyWork.preferred[]`;
- automatic analysis of MAME `error.log` preferred[] events.

## Current validation checkpoint

The current validated path is still intentionally conservative.

The C# adapter can compare against a one-active-enemy MAME trace, but some systems are still synchronized from the MAME reference while they are being reverse-engineered:

- enemy movement direction;
- `EnemyWork.preferred[]`;
- chase timers;
- chase round-robin state.

The current reverse-engineering focus is:

```text
EnemyWork.preferred[] = 0x61C4..0x61C7
```

The exact-PC diagnostic has confirmed that `preferred[]` is produced by a base generator and then may be overridden by chase/BFS logic. The detailed findings are documented in:

```text
doc/current_implementation.md
```

## Running the standard JSONL workflow

1. Open the project in Godot .NET.
2. Rebuild C# if needed:

```text
MSBuild > Rebuild
```

3. Run:

```text
scenes/tools/EnemyTraceSimulator.tscn
```

4. Configure MAME with the settings button:

```text
config/mame_trace_settings.json
```

Typical standard settings:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_sequence_trace.lua",
  "outputPrefix": "ladybug_sequence_v8",
  "framesAfterTick0": 600
}
```

5. Click:

```text
Lancer MAME/Lua
```

6. Load the generated trace and compare.

## Running the exact-PC preferred[] diagnostic

Use this mode only when investigating `EnemyWork.preferred[]`.

Typical diagnostic settings:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_preferred_pc_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_pcdiag",
  "framesAfterTick0": 500
}
```

When this script is selected, the launcher automatically enables MAME debugger/log mode.

Raw diagnostic output:

```text
tools/mame/lua/error.log
```

Generated report:

```text
traces/mame/ladybug_sequence_v8_pcdiag_preferred_pc_analysis.txt
```

## Repository layout

```text
.
├─ assets/
├─ config/
├─ data/
├─ doc/
├─ scenes/
├─ scripts/
│  └─ tools/
│     ├─ trace/
│     ├─ comparison/
│     └─ simulation/
├─ tools/
│  └─ mame/
│     └─ lua/
└─ traces/
```

Important files:

```text
scenes/tools/EnemyTraceSimulator.tscn
scripts/tools/EnemyTraceSimulatorWindow.cs
scripts/tools/MameTraceLauncher.cs
scripts/tools/simulation/LadyBugEnemySimulationAdapter.cs
scripts/tools/simulation/LadyBugPreferredPcLogAnalyzer.cs
tools/mame/lua/ladybug_sequence_trace.lua
tools/mame/lua/ladybug_preferred_pc_trace.lua
doc/current_implementation.md
```

## Generated files

Generated traces, logs, runtime Lua files, and MAME save-states should normally not be committed.

Typical generated files:

```text
tools/mame/lua/error.log
tools/mame/lua/ladybug_sequence_runtime_config.lua
tools/mame/lua/ladybug_preferred_pc_debug_startup.cmd
traces/mame/*.json
traces/mame/*.jsonl
traces/mame/*.txt
tools/mame/states/**/*.sta
```

## Current limitations

- The simulation is not yet a full independent arcade enemy AI.
- The official validation target is still one active enemy.
- Multi-enemy validation is planned but not yet stable.
- `EnemyWork.preferred[]` is still reference-synced in the comparison adapter.
- Chase timers and round-robin state are still reference-synced.
- Sprite rendering is diagnostic and not intended to be final gameplay rendering.

## Roadmap

Near-term:

1. implement a C# `MonsterPreferenceSystem`;
2. reproduce the base `preferred[]` generator;
3. apply the chase/BFS override separately;
4. compare against the standard JSONL trace;
5. remove the temporary MAME reference-sync for `preferred[]`.

Later:

- implement chase timer and round-robin behavior;
- implement independent enemy direction decisions;
- expand validation to multi-enemy traces;
- add stable regression traces or test fixtures.
