# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The repository is separate from the main Lady Bug remake project. Its purpose is to compare a MAME reference trace with a C# / Godot simulation so that the arcade enemy behavior can be reimplemented progressively and safely.

## Current status

Current checkpoint: **v0.6.85**

The project currently supports three complementary workflows:

1. **Standard JSONL trace workflow**
   - exports frame-by-frame state from MAME;
   - loads the trace in Godot;
   - visually replays MAME state;
   - compares MAME frames with the C# simulation adapter;
   - runs adapter-level shadow comparisons for `preferred[]`, `rejectedMask`, and fallback helper state.

2. **Exact-PC preferred[] diagnostic workflow**
   - uses MAME debugger breakpoints;
   - captures exact writes to `EnemyWork.preferred[]`;
   - writes raw `LBPREF` events to `tools/mame/lua/error.log`;
   - automatically generates a readable analysis report in `traces/mame`.

3. **Exact-PC EnemyWork decision diagnostic workflow**
   - uses MAME debugger breakpoints around the enemy decision code;
   - captures reset, rejection, fallback, local-door, and forced-reversal events;
   - writes raw `LBEW` events to `tools/mame/lua/error.log`;
   - automatically generates a cycle-level `Enemy_UpdateOne` decision report in `traces/mame`.

The standard JSONL trace remains the main comparison pipeline. The exact-PC workflows are reverse-engineering aids used for precise CPU-level diagnostics.

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
- exact-PC diagnostics for EnemyWork decision-cycle events;
- automatic analysis of MAME `error.log` preferred[] events;
- automatic analysis of MAME `error.log` EnemyWork decision events;
- standalone `LadyBugMonsterPreferenceSystem` model validated against exact-PC logs;
- preferred[] shadow replay diagnostic validating the modeled write sequence against MAME snapshots;
- preferred[] shadow compare integrated into the Lady Bug adapter while keeping MAME reference-sync active;
- rejectedMask shadow compare integrated into the Lady Bug adapter while keeping MAME reference-sync active;
- fallback helper shadow compare integrated into the Lady Bug adapter while keeping MAME reference-sync active;
- preferred[] rotate-branch shadow recognition generalized and validated for all four player directions;
- den-exit candidate diagnostics for enemy activation traces;
- Enemy_UpdateOne cycle classification for rejection/fallback decisions;
- rejectedMask shadow diagnostics validated on a one-enemy den-exit trace;
- fallback helper shadow diagnostics validated on a one-enemy den-exit trace;
- temporary reference-sync bridges for authoritative `rejectedMask` and `fallbackMask` / `0x61C2` helper fields.

## Current validation checkpoint

The current validated path is intentionally conservative.

The C# adapter can compare against one-active-enemy MAME traces, but some systems are still synchronized from the MAME reference while they are being reverse-engineered:

- enemy movement direction;
- authoritative `EnemyWork.preferred[]`;
- authoritative `rejectedMask`;
- authoritative fallback helper / legacy `fallbackMask`;
- chase timers;
- chase round-robin state.

The current reverse-engineering focus is the decision layer after preferred[] generation:

```text
preferred[] -> rejectedMask -> fallback helper -> final direction
```

Important methodology rule from this checkpoint onward:

```text
New enemy decision logic must be implemented source-first from
LadyBug_enemy_management_extract.txt.

MAME logs and traces are validation tools only. They must not be used to add
case-by-case simulation rules unless the behavior is justified by the
disassembled code.
```

Current EnemyWork findings:

```text
0x61C1 = EnemyRejectedDirMask
0x61C2 = fallback step counter/helper, not a normal direction mask
```

The latest standard JSONL comparison validates all three shadow models on the same one-enemy den-exit trace:

```text
comparedFrames=501
mismatches=0

preferred[] shadow model checks=496, matches=496, mismatches=0
rejectedMask shadow model checks=496, matches=496, mismatches=0
fallback helper shadow model checks=496, matches=496, mismatches=0
```

The fallback helper model is intentionally narrow at this checkpoint. It models the currently validated one-step-per-enemy-update behavior:

```text
fallback helper shadow sources:
ONE_STEP_PER_ENEMY_UPDATE=496
```

The exact-PC EnemyWork report still provides the deeper decision-cycle context:

```text
4315 without 4331/fallback:
  preferred candidate rejected, current temp direction kept

4315 -> 4331 -> 4241:
  preferred candidate rejected, current temp direction also rejected,
  fallback finder selects another direction
```

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

## Running the exact-PC EnemyWork diagnostic

Use this mode when investigating `rejectedMask`, fallback behavior, local-door rejection, or forced reversal.

Typical diagnostic settings:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_enemywork_pc_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_enemywork_pcdiag",
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
traces/mame/ladybug_sequence_v8_enemywork_pcdiag_enemywork_pc_analysis.txt
```

Important: this is not a JSONL trace. Do not load it with the standard trace loader. Read the generated analysis report instead.

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
scripts/tools/simulation/LadyBugEnemyWorkPcLogAnalyzer.cs
tools/mame/lua/ladybug_sequence_trace.lua
tools/mame/lua/ladybug_preferred_pc_trace.lua
tools/mame/lua/ladybug_enemywork_pc_trace.lua
doc/current_implementation.md
```

## Generated files

Generated traces, logs, runtime Lua files, and MAME save-states should normally not be committed.

Typical generated files:

```text
tools/mame/lua/error.log
tools/mame/lua/ladybug_sequence_runtime_config.lua
tools/mame/lua/ladybug_preferred_pc_debug_startup.cmd
tools/mame/lua/ladybug_enemywork_pc_debug_startup.cmd
traces/mame/*.json
traces/mame/*.jsonl
traces/mame/*.txt
tools/mame/states/**/*.sta
```

## Current limitations

- The simulation is not yet a full independent arcade enemy AI.
- The official validation target is still one active enemy.
- Multi-enemy validation is planned but not yet stable.
- `EnemyWork.preferred[]` is still authoritative reference-synced in the comparison adapter.
- authoritative `rejectedMask` is still reference-synced, even though it now has a passing shadow model on the latest one-enemy trace.
- authoritative fallback helper / legacy `fallbackMask` is still reference-synced, even though it now has a passing narrow shadow model on the latest one-enemy trace.
- Chase timers and round-robin state are still reference-synced.
- BFS direction is still observed / inferred from MAME in the shadow paths; full BFS pathfinding is not yet implemented.
- Sprite rendering is diagnostic and not intended to be final gameplay rendering.

## Roadmap

Near-term:

1. read and map the Z80 enemy-decision blocks from `LadyBug_enemy_management_extract.txt`;
2. implement source-named C# functions for the arcade blocks:

```text
0x42BA  Enemy_UpdateOne
0x42E6  Try preferred direction
0x3911  Logical maze validation
0x4130  Local / door validation
0x4241  Fallback direction selection
0x4347  Forced reversal
```

3. validate each C# block against exact-PC MAME logs;
4. avoid trace-specific branches or log-fitting rules;
5. document and cleanly rename the `fallbackMask` concept where appropriate;
6. keep authoritative comparison reference-synced until the source-based decision model is validated;
7. implement chase timer, round-robin behavior, and full BFS/chase direction selection.

Later:

- implement independent enemy direction decisions;
- expand validation to multi-enemy traces;
- add stable regression traces or test fixtures.
