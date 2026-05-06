# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The repository is separate from the main Lady Bug remake project. Its purpose is to compare a MAME reference trace with a C# / Godot simulation so that the arcade enemy behavior can be reimplemented progressively, safely, and without fitting rules to individual logs.

## Current status

Current checkpoint: **v0.6.90**

Latest validated commit:

```text
Add enemy release source diagnostics
```

The project currently supports five complementary workflows:

1. **Standard JSONL trace workflow**
   - exports frame-by-frame state from MAME;
   - loads the trace in Godot;
   - visually replays MAME state;
   - compares MAME frames with a C# simulation adapter;
   - runs adapter-level shadow comparisons for `preferred[]`, `rejectedMask`, fallback helper / `0x61C2`, and enemy direction.

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

4. **Source-first enemy decision diagnostics**
   - adds a C# transcription scaffold for selected Z80 blocks from `LadyBug_enemy_management_extract.txt`;
   - runs transition-oriented decision diagnostics in parallel with the current reference-direction comparison;
   - keeps the comparison non-invasive: the main adapter should still produce zero mismatches;
   - reports where the source-based model explains or fails to explain MAME scratch state.

5. **Source-first enemy release diagnostics**
   - models the release initialization path around `0x05AE` / `0x3061`;
   - checks whether the first active enemy transition matches the source release shape;
   - confirms the observed first active enemy looks like `0x3061` initialization plus one movement/update step;
   - does **not** hide or absorb the remaining transition diagnostic mismatch.

The standard JSONL trace remains the main comparison pipeline. The exact-PC workflows and source-first diagnostics are reverse-engineering aids used for precise CPU-level validation.

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
- enemy direction shadow diagnostics integrated into the Lady Bug adapter;
- preferred[] rotate-branch shadow recognition generalized and validated for all four player directions;
- den-exit candidate diagnostics for enemy activation traces;
- Enemy_UpdateOne cycle classification for rejection/fallback decisions;
- source-first decision model scaffold for the enemy Z80 decision path;
- transition-oriented source-first rejectedMask diagnostics;
- source-first release diagnostics for the first active enemy / den-exit case;
- temporary reference-sync bridges for authoritative `rejectedMask`, fallback helper / `0x61C2`, preferred[], chase timers, chase round-robin, and enemy direction.

## Current validation checkpoint

The current validated path is intentionally conservative.

The C# adapter can compare against one-active-enemy MAME traces, but several systems are still synchronized from the MAME reference while they are being reverse-engineered:

- enemy movement direction;
- authoritative `EnemyWork.preferred[]`;
- authoritative `rejectedMask`;
- authoritative fallback helper / legacy `fallbackMask`;
- chase timers;
- chase round-robin state.

The current reverse-engineering focus is the decision/release layer after preferred[] generation:

```text
preferred[] -> rejectedMask -> fallback helper -> final direction -> release / den-exit handling
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

Latest standard JSONL comparison on the validated one-enemy trace:

```text
comparedFrames=501
mismatches=0

preferred[] shadow model checks=496, matches=496, mismatches=0
rejectedMask shadow model checks=496, matches=496, mismatches=0
fallback helper shadow model checks=496, matches=496, mismatches=0
```

Transition decision diagnostic:

```text
Lady Bug decision diagnostics v0.6.90 transition model
rejectedMaskMatchesModeled=495
rejectedMaskDiffersFromModeled=1
```

Known remaining transition-diagnostic mismatch:

```text
tick=5
mameFrame=10
slot=0
prevTmp=02:78,8E
currTmp=08:58,87
preferred=02
source=PLAIN_STEP_OUTSIDE_CENTER
referenceRejected=02
modeledRejected=00
```

Interpretation:

```text
This is not a normal plain movement transition. It corresponds to the first active
enemy / den-exit release window, where previous EnemyWork does not represent a
normal continuous update of the same active enemy slot.
```

Release source diagnostic:

```text
activationTransitions=1
sourceReleaseLikeTransitions=1
sourceReleaseAfterStepTransitions=1
expected3061=82:58,86
observed slotRawXY=82:58,87
work=08:58,87
rejected=02
matchesRaw=True
matchesX=True
matchesY3061=False
matchesYAfterStep=True
matchesWorkAfterStep=True
```

Interpretation:

```text
The first active enemy is consistent with the source release initialization shape
from 0x3061, followed by a movement/update step. This is currently diagnostic only;
it is not yet a simulated release implementation.
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

```text
config/mame_trace_settings.json
```

Typical standard settings:

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
scripts/tools/simulation/LadyBugPreferredPcLogAnalyzer.cs
scripts/tools/simulation/LadyBugEnemyWorkPcLogAnalyzer.cs
tools/mame/lua/ladybug_sequence_trace.lua
tools/mame/lua/ladybug_preferred_pc_trace.lua
tools/mame/lua/ladybug_enemywork_pc_trace.lua
doc/current_implementation.md
```

## Source-first decision and release model files

```text
scripts/tools/simulation/LadyBugEnemyDecisionModel.cs
scripts/tools/simulation/LadyBugStaticMazeRomTable.cs
scripts/tools/simulation/LadyBugEnemyDecisionTraceDiagnosticAdapter.cs
scripts/tools/simulation/LadyBugEnemyReleaseModel.cs
```

Current source-first coverage:

```text
0x05AE  scan enemy slots and trigger release initialization
0x3061  initialize released enemy slot to raw=82, x=58, y=86
0x427E  decision-center pixel predicate
0x3911  logical maze validation
0x4130  local / door validation scaffold
0x4189  forced reversal probe scaffold
0x4224  one-pixel temp movement
0x4241  fallback direction scan
0x42E6  try preferred direction
0x4347  reverse direction
0x43BA  apply temp movement step and increment 0x61C2 helper
0x43D4  commit temp state
0x43F0  load current state to temp
```

Important limitation:

```text
The release model is diagnostic only. It identifies source-shaped activation
transitions, but it does not yet drive slot activation or simulate the full den-exit
sequence.

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
- authoritative `rejectedMask` is still reference-synced, even though the adapter-level shadow model matches the current one-enemy trace.
- the transition-oriented decision diagnostic still has one known mismatch at the first den-exit activation.
- authoritative fallback helper / legacy `fallbackMask` is still reference-synced.
- authoritative enemy direction is still reference-synced, although direction shadow diagnostics currently match the one-enemy trace.
- the release / den-exit path is identified diagnostically but not yet simulated.
- Chase timers and round-robin state are still reference-synced.
- BFS direction is still observed / inferred from MAME in the shadow paths; full BFS pathfinding is not yet implemented.
- `0x4130` local-door validation still needs faithful `0x3C0A` tile lookup over VRAM.
- Sprite rendering is diagnostic and not intended to be final gameplay rendering.

## Roadmap

Near-term:

1. add an exact-PC release diagnostic with breakpoints around `0x05AE`, `0x3061`, `0x4471`, `0x4241`, `0x43BA`, and `0x43D4`;
2. verify the real release order: slot initialization, scratch state, fallback, movement step, and final commit;
3. implement release / den-exit as a source-first simulation path instead of a diagnostic classification;
4. validate whether the `tick=5` transition mismatch is naturally explained by the release path;
5. reconstruct the `0x3C0A` tile lookup so `0x4130` can be validated for real;
6. replace permissive local-door diagnostic logic with source-faithful tile probing;
7. validate `0x4241` fallback direction selection against exact-PC fallback cycles;
8. validate `0x4347` forced reversal with a focused trace;
9. keep authoritative comparison reference-synced until the source-based decision and release models are validated;
10. once stable, switch authoritative `rejectedMask`, fallback helper, release activation, and enemy direction away from MAME reference-sync.

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
