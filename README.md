# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The repository is separate from the main Lady Bug remake project. Its purpose is to compare a MAME reference trace with a C# / Godot simulation so the arcade enemy behavior can be reimplemented progressively, safely, and without fitting rules to individual logs.

## Current status

Current checkpoint: **v0.6.93**

Latest validated commit:

```text
Add source-first 4315 current-kept shadow model
```

The main comparison pipeline is still conservative and reference-assisted, but the source-first diagnostics now explain the current one-enemy `rejectedMask` transition window with zero diagnostic mismatches. v0.6.93 adds a narrow source-first shadow check for the exact-PC validated `0x4315 preferred rejected, current kept` path.

Latest validated standard trace result:

```text
comparedFrames=501
mismatches=0

preferred[] shadow model checks=496, matches=496, mismatches=0
rejectedMask shadow model checks=496, matches=496, mismatches=0
fallback helper shadow model checks=496, matches=496, mismatches=0

Lady Bug decision diagnostics v0.6.92 transition model:
rejectedMaskMatchesModeled=496
rejectedMaskDiffersFromModeled=0
releaseActivationTransitions=1
releaseActivationModeledFromExactPc=1
preferredRejectedCurrentKept=2

Lady Bug source-first 4315 shadow v0.6.93:
checks=2
matches=2
mismatches=0
releaseChecks=1
normalCurrentKeptChecks=1
```

The formerly unresolved first-active-enemy transition is now modeled as:

```text
3061_4315_RELEASE_PREFERRED_REJECTED_CURRENT_KEPT
```

Meaning:

```text
0x3061 gives the release-shaped start state raw=82, x=58, y=86.
The first exact-PC Enemy_UpdateOne cycle starts at temp=08:58,86.
preferred=02 is rejected at 0x4315, so 0x61C1 |= 02.
current direction 08 remains valid and is kept.
0x4224/0x43BA move one pixel to 08:58,87.
0x43D4 commits the temp state.
```

v0.6.93 then runs the reconstructed `LadyBugEnemyDecisionModel.TryPreferredDirection()` path against the two exact-PC validated `0x4315 current kept` cases. This is still diagnostic-only. It does **not** yet mean the simulator independently releases enemies from the den or owns `rejectedMask` authoritatively.

## Implemented workflows

The project currently supports these complementary workflows:

1. **Standard JSONL trace workflow**
   - exports frame-by-frame state from MAME;
   - loads the trace in Godot;
   - visually replays MAME state;
   - compares MAME frames with a C# simulation adapter;
   - runs adapter-level shadow comparisons for `preferred[]`, `rejectedMask`, fallback helper / `0x61C2`, and enemy direction;
   - runs source-first decision and release transition diagnostics in parallel.

2. **Exact-PC preferred[] diagnostic workflow**
   - uses MAME debugger breakpoints;
   - captures exact writes to `EnemyWork.preferred[]`;
   - writes raw `LBPREF` events to `tools/mame/lua/error.log`;
   - automatically generates a readable analysis report in `traces/mame`.

3. **Exact-PC EnemyWork decision diagnostic workflow**
   - uses MAME debugger breakpoints around the enemy decision code;
   - captures reset, rejection, fallback, local-door, forced-reversal, release, and commit events;
   - writes raw `LBEW` events to `tools/mame/lua/error.log`;
   - automatically generates a cycle-level `Enemy_UpdateOne` decision report in `traces/mame`.

4. **Source-first enemy decision diagnostics**
   - adds C# transcription scaffolding for selected Z80 blocks from `LadyBug_enemy_management_extract.txt`;
   - runs transition-oriented decision diagnostics in parallel with the current reference-direction comparison;
   - keeps the comparison non-invasive: the main adapter should still produce zero mismatches;
   - reports where the source-based model explains or fails to explain MAME scratch state;
   - includes a narrow v0.6.93 shadow check for `0x4315 preferred rejected, current kept`.

5. **Source-first enemy release diagnostics**
   - models the release initialization shape around `0x05AE` / `0x3061`;
   - checks whether the first active enemy transition matches the source release shape;
   - now uses exact-PC evidence to model the first activation transition as a normal `0x4315 preferred rejected, current kept` cycle, with the correct release start state.

## Methodology rule

New enemy logic must be implemented **source-first** from:

```text
LadyBug_enemy_management_extract.txt
```

MAME logs and traces are validation tools only. They must not be used to add case-by-case simulation rules unless the behavior is justified by the disassembled Z80 code.

Current important findings:

```text
0x61C1 = EnemyRejectedDirMask
0x61C2 = fallback step counter/helper, not a normal direction mask
```

The v0.6.92 release activation handling follows this rule: it does not hide the first-active-enemy mismatch. It replaces an invalid frame-to-frame assumption with the exact-PC start state observed for the first `Enemy_UpdateOne` cycle.

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

## Enemy direction mapping

Enemy movement direction encoding from `LadyBug_enemy_management_extract.txt`:

```text
01 = left
02 = up
04 = right
08 = down
```

This differs from the player RAM direction mapping above. Keep the distinction explicit.

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

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_sequence_trace.lua",
  "outputPrefix": "ladybug_sequence_v8",
  "framesAfterTick0": 500,
  "includeLogicalMazeEachFrame": true
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

Expected current result on the validated one-enemy trace:

```text
mismatches=0
```

Then inspect the adapter summary for shadow-model, transition-model, and release diagnostics.

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

Raw diagnostic output:

```text
tools/mame/lua/error.log
```

Generated report:

```text
traces/mame/ladybug_sequence_v8_pcdiag_preferred_pc_analysis.txt
```

## Running the exact-PC EnemyWork diagnostic

Use this mode when investigating `rejectedMask`, fallback behavior, local-door rejection, forced reversal, release, or commit sequencing.

Typical diagnostic settings:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_enemywork_pc_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_enemywork_pcdiag",
  "framesAfterTick0": 500
}
```

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
│  ├─ current_implementation.md
│  ├─ patches/
│  └─ diagnostics/
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
scripts/tools/simulation/LadyBugSimulationState.cs
scripts/tools/simulation/LadyBugMonsterPreferenceSystem.cs
scripts/tools/simulation/LadyBugEnemyDecisionModel.cs
scripts/tools/simulation/LadyBugStaticMazeRomTable.cs
scripts/tools/simulation/LadyBugEnemyDecisionTraceDiagnosticAdapter.cs
scripts/tools/simulation/LadyBugEnemyReleaseModel.cs
scripts/tools/simulation/LadyBugEnemySourceFirst4315ShadowModel.cs
scripts/tools/simulation/LadyBugPreferredPcLogAnalyzer.cs
scripts/tools/simulation/LadyBugEnemyWorkPcLogAnalyzer.cs
tools/mame/lua/ladybug_sequence_trace.lua
tools/mame/lua/ladybug_preferred_pc_trace.lua
tools/mame/lua/ladybug_enemywork_pc_trace.lua
doc/current_implementation.md
```

## Source-first decision and release model coverage

```text
0x05AE  scan enemy slots and trigger release initialization
0x3061  initialize released enemy slot to raw=82, x=58, y=86
0x3911  logical maze validation
0x4130  local / door validation scaffold
0x4189  forced reversal probe scaffold
0x4224  one-pixel temp movement
0x4241  fallback direction scan
0x42E6  try preferred direction
0x4315  rejectedMask |= rejected preferred candidate; current direction may still be kept
0x4331  rejectedMask |= current temp direction before fallback
0x4347  reverse direction
0x43BA  apply temp movement step and increment 0x61C2 helper
0x43D4  commit temp state
0x43F0  load current state to temp
0x4471  alternate release/den path calling 0x3061, observed in exact-PC logs
```

Important limitations:

```text
The release handling is still diagnostic only. It models the first activation transition
in the decision diagnostic, but it does not yet drive slot activation in the simulator.

The v0.6.93 0x4315 shadow model validates the current-kept path only. It does not
remove the authoritative rejectedMask reference-sync bridge.

0x4130 is not yet fully authoritative because the diagnostic still lacks a faithful
transcription of the 0x3C0A tile lookup over VRAM.
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
- Authoritative `rejectedMask` is still reference-synced, although the adapter-level shadow, transition diagnostics, and source-first `0x4315 current kept` shadow now match the current one-enemy trace.
- Authoritative fallback helper / legacy `fallbackMask` is still reference-synced.
- Authoritative enemy direction is still reference-synced, although direction shadow diagnostics currently match the one-enemy trace.
- The release / den-exit path is modeled diagnostically for the first activation transition, but enemy release is not yet independently simulated.
- Chase timers and round-robin state are still reference-synced.
- BFS direction is still observed / inferred from MAME in the shadow paths; full BFS pathfinding is not yet implemented.
- `0x4130` local-door validation still needs faithful `0x3C0A` tile lookup over VRAM.
- Sprite rendering is diagnostic and not intended to be final gameplay rendering.

## Roadmap

Near-term:

1. transcribe the release/den path more fully instead of only modeling the first activation transition;
2. implement source-first release slot activation in the simulation state;
3. validate release sequencing against exact-PC logs around `0x3061`, `0x4315`, `0x43BA`, `0x43D4`, and `0x4471`;
4. reconstruct the `0x3C0A` tile lookup so `0x4130` can be validated for real;
5. replace permissive local-door diagnostic logic with source-faithful tile probing;
6. validate `0x4241` fallback direction selection against exact-PC fallback cycles;
7. validate `0x4347` forced reversal with a focused trace;
8. keep authoritative comparison reference-synced until the source-based decision and release models are validated;
9. once stable, switch authoritative `rejectedMask`, fallback helper, release activation, and enemy direction away from MAME reference-sync.

Later:

- implement chase activation and timer behavior;
- implement round-robin behavior independently;
- implement full BFS/chase direction selection;
- expand validation to multiple active enemies;
- add stable regression traces or test fixtures.

## Commit rhythm

Commit after each stable checkpoint that:

```text
- compiles;
- keeps the expected comparison result;
- has updated README.md and doc/current_implementation.md.
```

This makes it easy to revert to the last known-good state before trying the next source-first reconstruction step.
