# Current Implementation

**Project:** Enemy Trace Simulator  
**Current package version:** v0.2.5  
**Engine target:** Godot Engine .NET 4.6.2  
**Language:** C#  

## Purpose of this document

This document describes what is currently implemented in this repository.

It intentionally separates implemented behavior from planned behavior. Future systems are listed only in the final roadmap section.

## 1. Project goal

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool for validating the enemy movement logic of the Lady Bug arcade remake.

The intended final workflow is:

1. run Lady Bug in MAME;
2. load a known MAME save-state;
3. export a deterministic trace with a Lua script;
4. load that trace in Godot;
5. initialize the C# enemy simulation from the same state;
6. compare the C# simulation against the MAME trace frame by frame.

The current implementation covers steps 1 to 4 partially. It can launch MAME from Godot, use a Lua script to generate a trace, load the generated trace, and replay it visually. It does not yet run the real C# simulation or perform mismatch detection.

## 2. Current repository structure

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

## 3. Project entry point

The Godot project starts from:

```text
scenes/tools/EnemyTraceSimulator.tscn
```

The scene is intended to be launched directly from Godot while developing the tool.

## 4. Scene structure

### 4.1 EnemyTraceSimulator.tscn

The UI is authored directly in the `.tscn` scene instead of being fully created from C# at runtime. This is intentional: the window remains visible even if the C# assembly has not been rebuilt yet.

Current scene tree:

```text
EnemyTraceSimulator (Control)
└─ Root (MarginContainer)
   └─ MainLayout (VBoxContainer)
      ├─ Title (Label)
      ├─ Toolbar / PlaybackControls (HBoxContainer)
      │  ├─ LaunchMameLuaButton (Button)
      │  ├─ LoadTraceButton (Button)
      │  ├─ RunSimulationButton (Button)
      │  ├─ PauseResumeButton (Button)
      │  ├─ StepButton (Button)
      │  └─ StatusLabel (Label)
      ├─ BoardComparison (HBoxContainer)
      │  ├─ SimulationBoard (Control + EnemyTraceBoardView.cs)
      │  └─ MameTraceBoard (Control + EnemyTraceBoardView.cs)
      └─ Console (TextEdit)
```

The previous visible text fields for MAME config and trace path were removed in v0.2.5 to save vertical space. The workflow is now button-driven, with paths coming from configuration and generated trace metadata.

## 5. Current scripts

### 5.1 EnemyTraceSimulatorWindow.cs

Current role:

- root UI controller;
- binds scene nodes by path;
- connects button handlers;
- configures compact playback buttons;
- loads the default logical maze into both board views;
- launches MAME/Lua through `MameTraceLauncher`;
- loads JSON or JSONL trace files;
- parses frame, actor, and gate data;
- stores loaded frames in memory;
- manages playback state;
- advances playback at 60 Hz while running;
- supports manual single-frame stepping;
- writes messages to the bottom console and to Godot output.

Current playback constant:

```csharp
private const double PlaybackTickSeconds = 1.0 / 60.0;
```

Current playback buttons:

```text
↺    restart from the first frame
▶    resume playback
❚❚   pause playback
▶|   advance one tick
```

Important current limitation:

- the left board and the right board currently display the same loaded MAME trace frame;
- the left board is reserved for the future C# simulation output.

### 5.2 EnemyTraceBoardView.cs

Current role:

- draws one diagnostic board;
- loads `data/maze.json`;
- draws a background rectangle;
- draws a logical grid;
- draws static maze walls from wall bitmasks;
- draws rotating gates from the trace;
- draws the player if present and active;
- draws active enemies;
- draws a short direction vector for each actor.

Current maze assumptions:

```text
logical maze width  = 11
logical maze height = 11
arcade cell size    = 16 pixels
```

Current wall bit meanings:

```text
Up    = 1
Down  = 2
Left  = 4
Right = 8
```

Gate rendering rules:

```text
Horizontal gate = two-cell green horizontal segment centered on the gate pivot
Vertical gate   = two-cell green vertical segment centered on the gate pivot
```

Actor rendering currently interprets MAME positions as arcade-pixel coordinates. A temporary vertical offset is applied because the arcade coordinate space used by the runtime is larger than the 11 x 11 diagnostic maze currently displayed. This mapping is not final.

### 5.3 MameTraceSettings.cs

Current role:

- represents the JSON configuration stored in `config/mame_trace_settings.json`;
- stores paths and launch parameters for MAME;
- stores trace output parameters;
- supports project-local `res://` paths where appropriate.

Typical settings:

```json
{
  "mameExecutable": "C:/Path/To/MAME/mame.exe",
  "game": "ladybug",
  "romPath": "C:/Path/To/MAME/roms",
  "stateDirectory": "C:/Path/To/MAME/sta",
  "stateSubdir": "ladybug",
  "saveState": "test1",
  "luaScriptPath": "res://tools/mame/lua/ladybug_sequence_trace.lua",
  "outputDirectory": "res://traces/mame",
  "outputPrefix": "ladybug_sequence_v8",
  "framesAfterTick0": 600
}
```

### 5.4 MameTraceLauncher.cs

Current role:

- loads `config/mame_trace_settings.json`;
- resolves project paths;
- creates a runtime Lua configuration file for the MAME script;
- builds the MAME command-line arguments;
- starts MAME as an external process;
- waits for MAME to finish;
- returns messages and the expected generated trace path to the UI.

The previous `.bat` workflow has been replaced by this C# launcher and the JSON configuration file.

## 6. Current data and configuration files

### 6.1 config/mame_trace_settings.json

This file centralizes local MAME settings.

It is currently the main place to configure:

- MAME executable path;
- MAME game/driver name;
- ROM path;
- state directory;
- state subdirectory;
- save-state name;
- Lua script path;
- trace output directory;
- trace output prefix;
- number of frames to export.

The save-state name should be stored without `.sta`.

Example:

```text
stateDirectory = C:/Path/To/MAME/sta
stateSubdir    = ladybug
saveState      = test1
```

Expected actual state file:

```text
C:/Path/To/MAME/sta/ladybug/test1.sta
```

### 6.2 data/maze.json

The current maze file contains:

```text
width  = 11
height = 11
cells  = flat array of 121 wall masks
```

It is used only for drawing the static logical maze in the diagnostic board views.

### 6.3 tools/mame/lua/ladybug_sequence_trace.lua

This is the current MAME Lua trace script.

Current responsibilities:

- cooperate with the runtime Lua configuration generated by C#;
- capture the initial post-load state;
- export frame data to JSONL;
- export player state;
- export enemy slot state;
- export rotating gate state;
- use the loaded MAME save-state as the start of the sequence.

The script is still part of the trace-generation pipeline, not a simulation or comparison component.

### 6.4 traces/mame/

This directory is used for generated traces.

Trace outputs are intentionally ignored by Git. The directory can be kept in the repository with `.gitkeep`.

### 6.5 tools/mame/states/

This directory exists as an optional project-side location for MAME state files.

Local `.sta` files are ignored by Git. In practice, the current setup can also point directly to the normal MAME `sta` directory through `config/mame_trace_settings.json`.

## 7. Current trace model classes

The trace model classes are currently defined at the bottom of `EnemyTraceSimulatorWindow.cs`.

Current classes include:

```text
EnemyTraceFrame
EnemyTraceActor
EnemyTraceGateState
```

Depending on the locally applied patches, compatibility classes for older sample JSON traces may also exist.

This is acceptable for the current prototype, but these classes should be moved into separate files soon.

## 8. Current behavior

### 8.1 Startup

On `_Ready()`:

1. the UI nodes are bound;
2. playback buttons are configured;
3. button events are connected;
4. both boards load `res://data/maze.json`;
5. startup messages are written to the console.

### 8.2 Launching MAME/Lua

When **Lancer MAME/Lua** is pressed:

1. the simulator reads `res://config/mame_trace_settings.json`;
2. the launcher resolves paths;
3. a runtime Lua configuration is generated;
4. MAME is started with the configured game, ROM path, state directory, save-state, and Lua script;
5. the UI waits for MAME to terminate;
6. launcher messages are written to the console;
7. the expected generated trace path is remembered for loading.

### 8.3 Loading a trace

When **Charger trace** is pressed:

1. the simulator loads the most recent generated trace path when available;
2. otherwise it falls back to the configured default trace path;
3. JSONL is parsed line by line;
4. frame data is stored in memory;
5. frame 0 is displayed on both boards;
6. the status label is updated;
7. the console reports the frame count and gate count.

### 8.4 Playback

When **↺** is pressed:

- playback restarts from the first frame;
- the state becomes running.

When **▶ / ❚❚** is pressed:

- playback resumes or pauses;
- the button text changes according to the current state.

When **▶|** is pressed:

- playback pauses;
- one frame is advanced manually.

## 9. Implemented now

- Standalone Godot project skeleton.
- Main scene with compact simulator UI.
- Scene-authored controls.
- C# node binding.
- C# button handling.
- Console output area.
- JSON configuration for MAME paths and trace parameters.
- C# external MAME process launch.
- Runtime Lua configuration generation.
- MAME Lua trace script integration.
- Save-state-based trace start through MAME configuration.
- JSONL trace loading.
- Frame playback at 60 Hz.
- Manual tick/frame stepping.
- Logical maze rendering.
- Rotating gate debug rendering.
- Player and enemy debug rendering.
- `.gitignore` rules for generated and local-only files.

## 10. Not implemented yet

- Real C# simulation-side enemy movement.
- Reuse of the current Lady Bug remake enemy classes.
- Frame-by-frame comparison.
- Mismatch report.
- Navigation to first mismatch.
- Highlighting mismatches in the board views.
- Export of comparison logs.
- Exact final actor coordinate mapping.
- Original sprite rendering for players, enemies, and gates.
- UI for editing MAME settings directly inside Godot.
- Deterministic handling and replay of random arcade decisions on the C# side.

## 11. Current known limitations

### 11.1 Left and right boards are not different yet

The left board is named `Simulation C# / Godot`, but it currently displays the same frame as the MAME trace board.

This is a temporary visualization placeholder.

### 11.2 Actor coordinate mapping is provisional

The rotating gates are now aligned with the debug maze, but actor placement still needs validation.

The current implementation applies a temporary vertical offset for actors. This was added because MAME runtime positions are expressed in the arcade coordinate space, while the current debug view displays only an 11 x 11 logical maze area.

This should be replaced by an explicit, verified coordinate conversion.

### 11.3 Trace schema is still evolving

The JSONL trace schema is practical enough for the current viewer, but it is not final.

The parser is intentionally tolerant, but the project should eventually define one canonical trace schema.

### 11.4 Trace model classes need extraction

The trace DTOs currently live in `EnemyTraceSimulatorWindow.cs`.

They should move into separate files before the comparison logic grows.

### 11.5 No collectibles

Collectibles are intentionally absent from the simulator view. The validation target is enemy movement, not scoring or item collection.

## 12. Git ignore policy

The repository should keep source files, configuration templates, scripts, and documentation.

It should not keep generated or local machine-specific files such as:

```text
logs/
*.log
traces/**/*.json
traces/**/*.jsonl
traces/**/*.txt
traces/**/*.csv
tools/mame/states/**/*.sta
tools/mame/states/**/*.bak
runtime Lua config files
```

The `.gitkeep` files under trace/state directories are kept so the folder structure exists after cloning.

If generated files have already been committed, remove them from Git tracking without deleting them locally:

```powershell
git rm --cached -r traces
git rm --cached -r tools/mame/states
git add .gitignore
```

## 13. Planned next steps

### v0.3: trace model cleanup

- Move trace model classes into separate files.
- Add `MameTraceLoader`.
- Add explicit DTOs for trace frames, actors, gates, and metadata.
- Define one canonical JSONL trace schema.
- Make the actor coordinate conversion explicit and testable.

### v0.4: comparison architecture

- Add `SimulationFrame` / `ComparisonFrame` types.
- Add mismatch types.
- Add `TraceComparisonRunner`.
- Add a first textual mismatch report.
- Keep the simulation side simple until the comparison plumbing is stable.

### v0.5: C# simulation adapter

- Create a simulation adapter independent of the normal game scene.
- Reuse or port the existing enemy movement classes from the Lady Bug remake.
- Initialize the simulation from the trace initial state.
- Advance one tick at a time.
- Compare each simulated frame to the MAME frame.

### v0.6: diagnostic improvements

- Highlight mismatching actors or gates.
- Add a frame scrubber.
- Add first-mismatch jump.
- Add exportable mismatch reports.
- Add focused test scenarios for center decisions, fallback, door-local rejection, forced reversal, and chase/BFS overrides.

## 14. Design principle

The simulator should not try to look like the final game.

It should be a strict diagnostic tool:

- integer arcade-pixel positions;
- explicit frame numbers;
- raw MAME state visible when needed;
- deterministic playback;
- minimal visuals;
- precise mismatch reporting.
