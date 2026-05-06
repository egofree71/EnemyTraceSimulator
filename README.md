# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The repository is separate from the main Lady Bug remake project. Its purpose is to compare a MAME reference trace with a C# / Godot simulation so that the arcade enemy behavior can be reimplemented progressively and safely.

## Current status

Current checkpoint: **v0.6.79**

The project currently supports two complementary workflows:

1. **Standard JSONL trace workflow**
   - exports frame-by-frame state from MAME;
   - loads the trace in Godot;
   - visually replays MAME state;
   - compares MAME frames with the C# simulation adapter;
   - runs the adapter-level `preferred[]` shadow comparison.

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
- corrected player sprite direction display using the observed `0x6198` mapping;
- current-frame dump window;
- trace navigation helper window;
- comparison window;
- identity and injected-mismatch comparison modes;
- Lady Bug comparison adapter skeleton;
- one-active-enemy validation path;
- JSONL diagnostics for `EnemyWork.preferred[]`;
- exact-PC diagnostics for `EnemyWork.preferred[]`;
- automatic analysis of MAME `error.log` preferred[] events;
- standalone `LadyBugMonsterPreferenceSystem` model validated against exact-PC logs;
- preferred[] shadow replay diagnostic validating the modeled write sequence against MAME snapshots;
- preferred[] shadow compare integrated into the Lady Bug adapter while keeping MAME reference-sync active;
- preferred[] rotate-branch shadow recognition generalized and validated for all four player directions;
- den-exit candidate diagnostics for enemy activation traces;
- temporary reference-sync bridges for `rejectedMask` and `fallbackMask` scratch fields.

## Current validation checkpoint

The current validated path is intentionally conservative.

The C# adapter can compare against one-active-enemy MAME traces, but some systems are still synchronized from the MAME reference while they are being reverse-engineered:

- enemy movement direction;
- authoritative `EnemyWork.preferred[]`;
- `rejectedMask`;
- `fallbackMask`;
- chase timers;
- chase round-robin state.

The current reverse-engineering focus remains:

```text
EnemyWork.preferred[] = 0x61C4..0x61C7
```

The exact-PC diagnostic confirmed that `preferred[]` is produced by a base generator and then may be overridden by chase/BFS logic.

The standard JSONL adapter now computes a preferred[] shadow model in parallel. This shadow model does not yet replace the reference-synced `preferred[]`, but it currently matches the loaded one-enemy traces, including den-exit traces and rotate-branch cases for all four player directions.

## Player direction mapping

The player direction byte used by the preferred[] rotate branch is stored at:

```text
0x6198 = PLAYER_DIR_CURRENT
```

Runtime checks in MAME confirmed:

```text
01 = left
02 = down
04 = right
08 = up
```

This mapping is also used by the debug-board player sprite rendering.

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

6. Load the generated trace and compare:

```text
Compare > Run Lady Bug reference-direction step
```

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
scripts/tools/EnemyTraceBoardView.cs
scripts/tools/MameTraceLauncher.cs
scripts/tools/simulation/LadyBugEnemySimulationAdapter.cs
scripts/tools/simulation/LadyBugMonsterPreferenceSystem.cs
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
- `rejectedMask` and `fallbackMask` are still reference-synced scratch fields.
- Chase timers and round-robin state are still reference-synced.
- BFS direction is still observed / inferred from MAME in the shadow paths; full BFS pathfinding is not yet implemented.
- Sprite rendering is diagnostic and not intended to be final gameplay rendering.

## Roadmap

Near-term:

1. test the adapter preferred[] shadow model on more one-enemy traces and den-exit timings;
2. start isolating the real `rejectedMask` / fallback generator;
3. start replacing the reference-synced `preferred[]` only after the shadow path is robust on more traces;
4. implement chase timer and round-robin behavior;
5. implement full BFS/chase direction selection.

Later:

- implement independent enemy direction decisions;
- expand validation to multi-enemy traces;
- add stable regression traces or test fixtures.
