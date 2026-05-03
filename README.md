# Enemy Trace Simulator

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool used to validate the enemy movement logic of the arcade game **Lady Bug**.

The goal is to compare two synchronized timelines:

- a **reference timeline** exported from MAME with a Lua script;
- a **candidate timeline** produced later by the C# / Godot enemy simulation.

The current version focuses on the MAME trace side: launching MAME from Godot, generating a trace through Lua, loading the generated JSONL trace, and visualizing the maze, rotating gates, player, and enemies in a debug board.

This repository is deliberately separate from the main Lady Bug remake project. It is a validation tool, not the game itself.

## Current status

Current package version: **v0.2.5**

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
- debug rendering of rotating gates from the loaded trace;
- debug rendering of player and active enemies from the loaded trace;
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
- use of the original Lady Bug visual assets;
- integration with the full Lady Bug game codebase.

## Requirements

- Godot Engine .NET 4.6.2
- .NET SDK compatible with Godot .NET projects
- MAME with Lua support
- A valid local Lady Bug MAME setup

This repository does **not** include Lady Bug ROM files, MAME binaries, or copyrighted arcade assets.

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

## Repository layout

```text
.
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
  "frame": 123,
  "player": {
    "raw": [130, 88, 86, 0, 0],
    "x": 88,
    "y": 86,
    "dir": "08",
    "active": true
  },
  "enemies": [
    {
      "slot": 0,
      "raw": [130, 88, 134, 12, 4],
      "x": 88,
      "y": 134,
      "dir": "08",
      "active": true
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

The current renderer is diagnostic only.

It draws:

- static maze walls in purple;
- rotating gates in green;
- the player as a red debug marker;
- active enemies as blue debug markers;
- a direction vector for each actor when available.

Rotating gates are rendered as two-cell segments centered on their logical pivot:

- horizontal gates occupy two logical cells horizontally;
- vertical gates occupy two logical cells vertically.

Actor coordinate mapping is still provisional. The current renderer applies a temporary vertical offset for actors because the MAME arcade coordinate space includes a larger logical area than the 11 x 11 diagnostic maze currently displayed.

## Current limitations

The left board is labelled **Simulation C# / Godot**, but it currently displays the loaded MAME trace frame just like the right board. This is intentional for now: the C# simulation side has not been connected yet.

The actor positions are close enough for visual inspection, but the coordinate mapping is not considered final. Gates are currently more reliable than actor placement.

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
