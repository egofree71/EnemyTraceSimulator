# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The goal is to compare two synchronized timelines:

- a **reference timeline** exported from MAME with a Lua script;
- a **candidate timeline** produced later by the C# / Godot enemy simulation.

The current version focuses on the MAME trace side: launching MAME from Godot, generating a trace through Lua, loading the generated JSONL trace, and visualizing the maze, rotating gates, player, and enemies in a diagnostic board.

This repository is deliberately separate from the main Lady Bug remake project. It is a validation tool, not the game itself.

## Current status

Current package version: **v0.6.44**

Implemented now:

- standalone Godot 4.6.2 .NET project;
- main scene: `scenes/tools/EnemyTraceSimulator.tscn`;
- compact toolbar with the main workflow buttons;
- settings button and dialog for editing `config/mame_trace_settings.json`;
- configurable MAME launch through `config/mame_trace_settings.json`;
- C# MAME process launcher;
- Lua runtime configuration generation;
- MAME Lua trace script integration;
- MAME save-state loading through MAME command-line options;
- JSONL trace loading through `MameTraceLoader`;
- trace model classes extracted under `scripts/tools/trace/`;
- diagnostic trace blocks parsed into dedicated classes: `EnemyTraceEnemyWorkState`, `EnemyTraceTimersState`, and `EnemyTracePortsState`;
- optional raw memory trace blocks parsed into `EnemyTraceRawMemoryState`;
- centralized MAME-to-Godot actor Y conversion through `MameTraceCoordinates`;
- debug rendering of the logical 11 x 11 maze from `data/maze.json`;
- rendering of rotating gates from the loaded trace;
- rendering of the player from `assets/sprites/player/ladybug_spritesheet.png
assets/sprites/enemies/enemy_level1.png` when available;
- nearest-neighbor player sprite rendering at a fixed 32 x 32 debug size;
- fallback debug rendering when the player sprite is missing;
- optional player debug markers for raw MAME position and turn target;
- runtime player sprite visual offset tuning with keyboard shortcuts;
- active enemy rendering from the loaded trace using the level-1 spritesheet when available;
- inactive enemy slots hidden by default, with an optional diagnostic toggle;
- MAME-to-Godot actor Y mirroring with `0xDD - mameY`;
- playback controls: restart, pause/resume, step one tick;
- direct tick jump field in the toolbar;
- current-frame diagnostic dump window opened from the toolbar;
- trace navigation helper window opened from the toolbar;
- native Godot subwindows enabled for diagnostic windows;
- two synchronized boards: left for future C# simulation, right for MAME trace;
- bottom console area for runtime messages;
- `.gitignore` rules for generated traces, logs, runtime files, and local MAME state files.

Not implemented yet:

- real C# enemy simulation output;
- frame-by-frame comparison between simulation and MAME;
- mismatch highlighting;
- first-mismatch navigation;
- exact final actor coordinate mapping;
- original gate sprite rendering and level 2+ enemy sprite selection;
- integration with the full Lady Bug game codebase.

## Requirements

- Godot Engine .NET 4.6.2
- .NET SDK compatible with Godot .NET projects
- MAME with Lua support
- A valid local Lady Bug MAME setup

This repository does **not** include Lady Bug ROM files or MAME binaries.

The sprite renderers expect these optional files if visual actor rendering is desired:

```text
assets/sprites/player/ladybug_spritesheet.png
assets/sprites/enemies/enemy_level1.png
```

These files can be copied from the main Lady Bug remake repository if needed. If a file is missing, the board falls back to simple debug markers.

## Running the tool

1. Open this repository folder in Godot Engine .NET.
2. Build the C# project from Godot or with:

```powershell
dotnet build
```

3. Run the main scene:

```text
scenes/tools/EnemyTraceSimulator.tscn
```

4. Edit the local MAME configuration.

You can click the **⚙** button in the simulator toolbar to edit the settings directly from Godot.

The settings are saved to:

```text
config/mame_trace_settings.json
```

At minimum, adapt these values to your local machine:

```json
{
  "mameExecutable": "C:/Path/To/MAME/mame.exe",
  "romPath": "C:/Path/To/MAME/roms",
  "stateDirectory": "C:/Path/To/MAME/sta",
  "stateSubdir": "ladybug",
  "saveState": "test1"
}
```

The expected save-state path for the example above is:

```text
C:/Path/To/MAME/sta/ladybug/test1.sta
```

5. In the simulator window, use:

```text
Lancer MAME/Lua
```

to start MAME and generate a trace.

6. Then use:

```text
Charger trace
```

to load the generated trace.

7. Use the playback controls:

```text
⚙    edit MAME trace settings
↺    restart from the first frame
▶    resume playback
❚❚   pause playback
▶|   advance one tick
Tick jump to the requested trace tick
```

The tick field is synchronized with playback. If the requested tick is not present in the trace, the viewer shows the nearest available frame and writes a message to the console.

The **Dump** button opens a separate diagnostic window for the current frame. It includes metadata, player state, enemy slots, gates, enemy work RAM, timers, ports, and raw-memory block sizes. The main console remains a concise activity log.

The **Find** button opens a separate navigation helper window. It can jump to the first active enemy, the first enemy direction change, the first active frame for a selected slot, the first direction change for a selected slot, and the first gate change. When jumping to the first gate change, the console reports which gates changed orientation.

The same window also includes a condition search area with **Find condition** and **Find next**. Current supported conditions include `enemyWork rejectedMask != 0`, `enemyWork fallbackMask != 0`, `enemyWork tempDir == value`, enemy direction checks, slot direction checks, and player direction checks.

The **Compare** button opens a comparison test window. For now, it can run two checks: an identity comparison, where the MAME trace is compared against a simulation generated from the same trace and should produce zero mismatches; and an injected mismatch test, where the first active enemy has its X coordinate altered by one pixel so the mismatch reporting path can be validated.

The comparison runner now checks actors, gates, frame metadata, `enemyWork`, timers, and ports. This gives the future simulation adapter a broader target than just enemy coordinates.

The temporary comparison sources now implement `IEnemySimulationAdapter`. This keeps the **Compare** window independent from any specific simulation source.

`LadyBugEnemySimulationAdapter` now exists as the first skeleton for the future real C# simulation. It builds a typed `LadyBugSimulationInitialState` from the first MAME trace frame, including player, enemy slots, gates, enemyWork, timers, and ports.

After v0.6.5, the adapter no longer directly returns an identity simulation. It builds a `LadyBugSimulationState` and generates frames from that state. No real enemy movement is applied yet, so this adapter is expected to diverge once the MAME trace changes enemy, gate, timer, or enemyWork state.

After v0.6.7, `LadyBugSimulationState.AdvanceOneTick()` exists. It initially synchronized external inputs from the MAME reference trace, specifically the player state and input ports.

After v0.6.10, `AdvanceOneTick()` also synchronizes gates and timers from the MAME reference trace. This makes the current Lady Bug adapter focus its expected divergence on enemies and enemyWork.

After v0.6.14, active enemies are advanced by one pixel using the direction observed in the MAME trace. This is not yet the real enemy decision logic, but it validates the low-level coordinate movement convention. The expected first divergence now moves from enemy position to `enemyWork` or later decision-state fields.

After v0.6.17, the comparison window includes an **Ignore EnemyWork mismatches** option. This makes it possible to validate movement and environment alignment while `enemyWork` is still pending. In the current reference trace, enabling this filter leaves no remaining mismatches, which confirms that the reference-direction stepping matches MAME for the compared fields.

The status line is displayed below the two board views, not inside the toolbar. This keeps the toolbar stable even after a large trace is loaded.

## Important Godot .NET rebuild note

After replacing C# files, Godot may continue to run an older compiled assembly. If the UI does not reflect the latest patch, use:

```text
MSBuild > Rebuild
```

inside Godot, then relaunch the scene.

A normal **Build** is often enough, but **Rebuild** is the safest option when the visible version/debug text does not change.

## Repository layout

```text
.
├─ assets/
│  └─ sprites/
│     └─ player/
│        ├─ player/
│        │  └─ ladybug_spritesheet.png   optional local asset
│        └─ enemies/
│           └─ enemy_level1.png          optional local asset
├─ config/
│  └─ mame_trace_settings.json
├─ data/
│  └─ maze.json
├─ doc/
│  └─ current_implementation.md
├─ scenes/
│  └─ tools/
│     └─ EnemyTraceSimulator.tscn
├─ scripts/
│  └─ tools/
│     ├─ EnemyTraceBoardView.cs
│     ├─ EnemyTraceSimulatorWindow.cs
│     ├─ MameTraceLauncher.cs
│     ├─ MameTraceSettings.cs
│     ├─ trace/
│     │  ├─ EnemyTraceActor.cs
│     │  ├─ EnemyTraceFrame.cs
│     │  ├─ EnemyTraceGateState.cs
│     │  ├─ EnemyTraceEnemyWorkState.cs
│     │  ├─ EnemyTraceTimersState.cs
│     │  ├─ EnemyTracePortsState.cs
│     │  ├─ EnemyTraceRawMemoryState.cs
│     │  ├─ MameTraceCoordinates.cs
│     │  └─ MameTraceLoader.cs
│     ├─ comparison/
│     │  ├─ SimulationFrame.cs
│     │  ├─ SimulationActorState.cs
│     │  ├─ SimulationGateState.cs
│     │  ├─ SimulationEnemyWorkState.cs
│     │  ├─ SimulationTimersState.cs
│     │  ├─ SimulationPortsState.cs
│     │  ├─ ComparisonFrame.cs
│     │  ├─ TraceMismatch.cs
│     │  ├─ TraceComparisonResult.cs
│     │  ├─ TraceComparisonOptions.cs
│     │  ├─ TraceSimulationStub.cs
│     │  └─ TraceComparisonRunner.cs
│     └─ simulation/
│        ├─ IEnemySimulationAdapter.cs
│        ├─ SimulationAdapterResult.cs
│        ├─ IdentityTraceSimulationAdapter.cs
│        ├─ InjectedMismatchSimulationAdapter.cs
│        ├─ LadyBugSimulationInitialState.cs
│        ├─ LadyBugSimulationState.cs
│        └─ LadyBugEnemySimulationAdapter.cs
├─ tools/
│  └─ mame/
│     ├─ lua/
│     │  └─ ladybug_sequence_trace.lua
│     └─ states/
│        └─ ladybug/
│           └─ .gitkeep
├─ traces/
│  └─ mame/
│     └─ .gitkeep
├─ EnemyTraceSimulator.csproj
├─ project.godot
├─ .gitignore
└─ README.md
```

## Window behavior

The project uses native subwindows for diagnostic windows:

```ini
[display]
window/subwindows/embed_subwindows=false
```

This allows the frame dump window to move outside the main simulator window on the desktop.

## Configuration

The simulator no longer exposes MAME and trace paths directly as visible text fields in the main UI. Runtime paths are centralized in:

```text
config/mame_trace_settings.json
```

The C# launcher reads this file, resolves `res://` paths to project-local paths, generates a Lua runtime configuration, and starts MAME with the configured script and save-state parameters.

The toolbar **⚙** button opens a settings window that reads and writes this JSON file. Generated files such as traces, logs, runtime Lua configs, and `.sta` files are ignored by Git.

## Trace format

The current MAME trace format is JSON Lines (`.jsonl`): one JSON object per frame.

A typical frame contains:

```json
{
  "schema": "ladybug.sequenceTrace.v7",
  "tick": 0,
  "phase": "post_load_tick0",
  "mameFrame": 5,
  "player": {
	"raw": "82",
	"x": "78",
	"y": "8B",
	"sprite": "00",
	"attr": "00",
	"turnTargetX": "78",
	"turnTargetY": "86",
	"currentDir": "08"
  },
  "enemies": [
	{
	  "slot": 0,
	  "raw": "82",
	  "x": "58",
	  "y": "86",
	  "currentDir": "08"
	}
  ],
  "gates": [
	{
	  "gate_id": 0,
	  "pivot": { "x": 3, "y": 2 },
	  "currentOrientation": "Horizontal"
	}
  ]
}
```

The parser is now isolated in `scripts/tools/trace/MameTraceLoader.cs`. It is intentionally tolerant and supports several field names used during the trace experiments, such as `pivot`, `gatePivot`, `orientation`, and `currentOrientation`.

## Debug board rendering

The current renderer is diagnostic, but less noisy by default than the early prototype.

It draws:

- static maze walls in purple;
- rotating gates in green;
- the player with the Lady Bug player sprite when available;
- active level-1 enemies with `enemy_level1.png` when available;
- active enemies as blue debug markers when the spritesheet is missing;
- inactive enemy slots only when explicitly enabled for diagnostics.

Rotating gates are rendered as two-cell segments centered on their logical pivot:

- horizontal gates occupy two logical cells horizontally;
- vertical gates occupy two logical cells vertically.

The player sprite is rendered with nearest-neighbor filtering at a fixed 32 x 32 debug size. Its visual offset is currently:

```text
(0, -7) arcade pixels
```

These offsets only affect sprite drawing. They do **not** change the raw MAME gameplay coordinates.

Useful debug shortcuts:

```text
Ctrl + D       toggle player debug markers
Ctrl + arrows  adjust player sprite visual offset
Ctrl + Home    reset player sprite visual offset to (0, 2)
Ctrl + E       toggle inactive enemy slots
```

When enabled, the player debug markers mean:

```text
cyan P    raw player x/y read from MAME
yellow T  player turn target, when present in the trace
```

Actor coordinate mapping is still provisional. The current renderer applies a temporary vertical offset for actors because the MAME arcade coordinate space includes a larger logical area than the 11 x 11 diagnostic maze currently displayed.

## Current limitations

The left board is labelled **Simulation C# / Godot**, but it currently displays the loaded MAME trace frame just like the right board. This is intentional for now: the C# simulation side has not been connected yet.

The actor positions are now converted with the MAME Y mirror and are good enough for visual inspection, but the coordinate mapping is still not considered final. Gates are currently more reliable than actor placement.

The tool does not yet compare states. It only loads, displays, and replays the trace.

## Roadmap

The early roadmap has been adjusted after the v0.2.x work. The simulator already has a usable UI shell, MAME/Lua launch, trace loading, gate rendering, player and level-1 enemy rendering, settings editing, and tick navigation.

The next steps should now focus less on UI polish and more on making the trace pipeline clean enough to compare against a real C# simulation.

### v0.3: trace model cleanup and loader extraction

Status after v0.3.4: trace model extraction, diagnostic DTOs, and optional raw memory block parsing done.

Implemented:

- `EnemyTraceFrame`, `EnemyTraceActor`, and `EnemyTraceGateState` moved into separate files;
- dedicated `MameTraceLoader` added;
- current JSONL trace loading kept unchanged from the UI point of view;
- MAME-to-Godot actor Y conversion centralized in `MameTraceCoordinates`;
- `EnemyTraceSimulatorWindow.cs` no longer owns JSON / JSONL parsing or trace DTO definitions;
- `enemyWork`, `timers`, and `ports` are now parsed into dedicated classes;
- optional raw memory blocks are parsed into `EnemyTraceRawMemoryState`;
- the first-frame enemy scan now logs compact enemy-work diagnostics, including temporary direction, rejection mask, fallback mask, preferred directions, and chase timers;
- trace loading logs a compact raw-memory block summary when those blocks are present.

Remaining v0.3 cleanup:

- add explicit DTOs for top-level metadata if the trace schema grows further;
- document which trace fields are source-of-truth and which are only visual/debug fields;
- add small loader-focused tests or sample trace checks when the project structure is ready for tests.

### v0.4: trace inspection and diagnostic state

Status after v0.4.8: current-frame dump window, trace navigation helpers, gate-change diagnostics, and condition-based search implemented.

Implemented:

- toolbar **Dump** button;
- separate diagnostic window for the current frame;
- dump of metadata, player state, enemy slots, gates, enemy work RAM, timers, ports, and raw-memory block sizes;
- compact main console: activity and summary messages stay in the console, large dumps move to the dump window;
- native subwindows enabled so diagnostic windows can move outside the main simulator window;
- compact status label to avoid toolbar overflow;
- toolbar **Find** button;
- separate trace navigation helper window;
- helpers for first active enemy, first direction change, first active frame for a selected slot, first direction change for a selected slot, and first gate change;
- gate-change diagnostics in the Dump window, comparing the selected frame with the previous frame;
- console summary of the gates that changed when using **Find → First gate change**;
- condition-based search in the **Find** window;
- initial comparison data model under `scripts/tools/comparison/`;
- toolbar **Compare** button opening a comparison test window;
- temporary identity simulation source;
- injected mismatch test source for validating mismatch detection;
- comparison support for frame metadata, enemyWork, timers, and ports;
- simulation adapter interface under `scripts/tools/simulation/`;
- temporary comparison sources converted into simulation adapters;
- first `LadyBugEnemySimulationAdapter` skeleton;
- typed `LadyBugSimulationInitialState` built from the first MAME trace frame;
- frozen `LadyBugSimulationState` used to generate simulation frames independently from the MAME trace;
- first `AdvanceOneTick()` hook in `LadyBugSimulationState`;
- player and input ports synchronized from the MAME reference trace during adapter playback;
- gates and timers synchronized from the MAME reference trace during adapter playback;
- adapter summary/version log updated to reflect the current sync behavior;
- active enemies advanced by one pixel using the direction observed in the MAME trace;
- comparison summary renamed to `Lady Bug reference-direction step`;
- status line moved below the two boards to avoid toolbar overflow.

Remaining v0.4 work:

- show which exact gate changed at the selected tick;
- add a more generic condition-based search, if it becomes useful;
- keep this diagnostic layer read-only: it should inspect the MAME trace, not simulate anything yet.

### v0.5: comparison data model

Status after v0.5.6: initial comparison model, identity comparison, injected mismatch test, and diagnostic state comparison implemented.

Implemented:

- `SimulationFrame`, `SimulationActorState`, and `SimulationGateState`;
- `ComparisonFrame`, `TraceMismatch`, and `TraceComparisonResult`;
- `TraceComparisonRunner`;
- temporary `TraceSimulationStub` identity source;
- toolbar **Compare** button opening a comparison test window;
- identity comparison test;
- injected mismatch test that alters the first active enemy X coordinate;
- comparison of frame metadata, `enemyWork`, timers, and ports;
- richer mismatch categories: `Metadata`, `EnemyWork`, `Timer`, and `Port`;
- first mismatch report in the console;
- automatic jump to the first mismatch frame;
- status line moved below the boards to avoid toolbar overflow.

Remaining v0.5 work:

- keep the left board trace-driven until the comparison model is stable;
- then move to v0.6: C# enemy simulation adapter.

### v0.6: C# enemy simulation adapter

Status after v0.6.44: reference-direction enemy stepping, partial `EnemyWork` reconstruction, chase-state synchronization, and preferred-only comparison filtering added.

Implemented:

- `IEnemySimulationAdapter` interface;
- `SimulationAdapterResult`;
- `IdentityTraceSimulationAdapter`;
- `InjectedMismatchSimulationAdapter`;
- `LadyBugSimulationInitialState`;
- `LadyBugSimulationState`;
- `LadyBugEnemySimulationAdapter` skeleton;
- **Compare** window now runs adapters instead of calling the temporary stub directly;
- **Compare** window includes **Run Lady Bug adapter skeleton**;
- Lady Bug adapter now generates frames from its own simulation state instead of directly mirroring MAME;
- `LadyBugSimulationState.AdvanceOneTick()` exists;
- the tick hook currently syncs player, input ports, gates, and timers from MAME as reference state;
- active enemies move one pixel using the MAME direction;
- the comparison source summary now reports `Lady Bug reference-direction step`;
- the current expected first mismatch is now typically in `EnemyWork`, not in basic enemy position;
- `EnemyWork` mismatches can be ignored temporarily through the comparison window;
- when this filter removes all mismatches, the console reports that no remaining mismatch exists after applying filters.

Remaining v0.6 work:

- implement the real `EnemyWork.preferred[]` generator, beginning with the arcade base-preference path around `0x2E5C`;
- replace temporary reference-synchronized chase timer / round-robin state with the real chase/BFS subsystem;
- replace the reference direction with real enemy direction decision logic;
- reuse or port the enemy movement classes from the Lady Bug remake;
- create a standalone simulation adapter independent of the normal game scene;
- initialize gates, maze, player position, enemies, timers, chase state, and enemy work state from the MAME trace;
- advance one simulation tick at a time;
- compare simulated enemy positions and directions against MAME;
- report the first divergent tick.

### v0.7: mismatch visualization

Goal: make divergences visible immediately.

Planned work:

- highlight mismatching enemies or gates in the two boards;
- add a jump-to-first-mismatch button;
- add previous/next mismatch navigation;
- add exportable comparison reports;
- add focused scenarios for center decisions, fallback, door-local rejection, forced reversal, and chase/BFS behavior.

### Later

Possible later improvements:

- original gate sprite rendering;
- level 2+ enemy sprite selection;
- file/directory picker buttons in the settings dialog;
- trace schema versioning and validation;
- frame scrubber or timeline view;
- side-by-side MAME screenshot reference support, if useful.

## Design principle

The simulator should remain strict, boring, and explicit.

It is not trying to look like the final game. It should help answer one question:

> At which exact frame does the C# enemy simulation diverge from the arcade reference?

That means the tool should favor integer arcade-pixel positions, raw MAME values, deterministic playback, and precise mismatch reporting over nice visuals.


## v0.6.44 checkpoint: reference-driven enemy work alignment

After the reference-direction step work, the Lady Bug adapter can now validate the current 601-frame trace with **no remaining mismatch** when only `EnemyWork.preferred[]` mismatches are filtered.

Current behavior of `LadyBugEnemySimulationAdapter`:

- synchronizes reference player, ports, gates, and global timers;
- advances active enemies one arcade pixel per tick using the MAME reference direction;
- derives `EnemyWork.tempDir`, `EnemyWork.tempX`, and `EnemyWork.tempY` from the simulated active enemy;
- derives `EnemyWork.rejectedMask` from decision-center candidates, using the MAME reference `preferred[0]` while `preferred[]` itself remains unsimulated;
- keeps `EnemyWork.fallbackMask` aligned for the validated trace;
- synchronizes `EnemyWork.chaseTimers[]` and `EnemyWork.chaseRoundRobin` from MAME as a temporary reference state;
- supports comparison filters for either all `EnemyWork` mismatches or only `EnemyWork.preferred[]` mismatches;
- logs a compact mismatch timeline around the first mismatch to diagnose enemy state transitions.

Important validation result:

```text
Ignore only EnemyWork preferred[] mismatches
Comparison result: no remaining mismatch after applying filters.
```

This means the current adapter matches MAME for enemy position, enemy direction, `tempDir`, `tempX`, `tempY`, `rejectedMask`, `fallbackMask`, chase timer state when synchronized, gates, global timers, player, and ports. The remaining unsolved block is `EnemyWork.preferred[]`.

Next work:

- replace the temporary MAME reference `preferred[0]` dependency with a real reproduction of the arcade preferred-direction generator;
- implement the base preferred-direction path around `0x2E5C`;
- later replace reference-synchronized chase timer / round-robin state with the real chase/BFS subsystem.
