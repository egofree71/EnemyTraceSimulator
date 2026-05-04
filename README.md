# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The goal is to compare two synchronized timelines:

- a **reference timeline** exported from MAME with a Lua script;
- a **candidate timeline** produced later by the C# / Godot enemy simulation.

The current version focuses on the MAME trace side: launching MAME from Godot, generating a trace through Lua, loading the generated JSONL trace, and visualizing the maze, rotating gates, player, and enemies in a diagnostic board.

This repository is deliberately separate from the main Lady Bug remake project. It is a validation tool, not the game itself.

## Current status

Current package version: **v0.2.25**

Implemented now:

- standalone Godot 4.6.2 .NET project;
- main scene: `scenes/tools/EnemyTraceSimulator.tscn`;
- compact toolbar with the main workflow buttons;
- configurable MAME launch through `config/mame_trace_settings.json`;
- C# MAME process launcher;
- Lua runtime configuration generation;
- MAME Lua trace script integration;
- MAME save-state loading through MAME command-line options;
- JSONL trace loading;
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

4. Edit the local MAME configuration file:

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

7. Use the playback buttons:

```text
↺    restart from the first frame
▶    resume playback
❚❚   pause playback
▶|   advance one tick
```

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
│     └─ MameTraceSettings.cs
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

## Configuration

The simulator no longer exposes MAME and trace paths directly as visible text fields in the main UI. Runtime paths are centralized in:

```text
config/mame_trace_settings.json
```

The C# launcher reads this file, resolves `res://` paths to project-local paths, generates a Lua runtime configuration, and starts MAME with the configured script and save-state parameters.

Generated files such as traces, logs, runtime Lua configs, and `.sta` files are ignored by Git.

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

The parser is intentionally tolerant and supports several field names used during the trace experiments, such as `pivot`, `gatePivot`, `orientation`, and `currentOrientation`.

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

### v0.3: trace cleanup and model extraction

- Move trace model classes out of `EnemyTraceSimulatorWindow.cs`.
- Add a dedicated `MameTraceLoader`.
- Add explicit trace DTOs for frames, actors, gates, RAM helpers, and metadata.
- Make actor coordinate mapping explicit and documented.

### v0.4: comparison infrastructure

- Add simulation frame types.
- Add mismatch types.
- Add a comparison runner.
- Report first mismatch in the console.
- Keep the left board trace-driven until the comparison pipeline is stable.

### v0.5: C# simulation adapter

- Reuse or port the enemy movement classes from the Lady Bug remake.
- Initialize the simulation from the loaded MAME trace.
- Advance one tick at a time.
- Compare simulated enemy positions and directions with MAME.

### v0.6+

- Add mismatch highlighting in the boards.
- Add frame scrubber.
- Add jump-to-first-mismatch.
- Add exportable comparison reports.
- Add focused scenarios for fallback, door-local rejection, forced reversal, and chase/BFS behavior.

## Design principle

The simulator should remain strict, boring, and explicit.

It is not trying to look like the final game. It should help answer one question:

> At which exact frame does the C# enemy simulation diverge from the arcade reference?

That means the tool should favor integer arcade-pixel positions, raw MAME values, deterministic playback, and precise mismatch reporting over nice visuals.
