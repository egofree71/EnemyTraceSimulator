# Current Implementation

**Project:** Enemy Trace Simulator  
**Current package version:** v0.5.6  
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
│     └─ trace/
│        ├─ EnemyTraceActor.cs
│        ├─ EnemyTraceFrame.cs
│        ├─ EnemyTraceGateState.cs
│        ├─ EnemyTraceEnemyWorkState.cs
│        ├─ EnemyTraceTimersState.cs
│        ├─ EnemyTracePortsState.cs
│        ├─ EnemyTraceRawMemoryState.cs
│        ├─ MameTraceCoordinates.cs
│        └─ MameTraceLoader.cs
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

## 3. Window behavior

The project currently uses native subwindows for diagnostic windows:

```ini
[display]
window/subwindows/embed_subwindows=false
```

This is required so the frame dump window is not trapped inside the main simulator viewport and can be moved freely on the desktop.

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
      │  ├─ SettingsButton (Button)
      │  ├─ LaunchMameLuaButton (Button)
      │  ├─ LoadTraceButton (Button)
      │  ├─ RunSimulationButton (Button)
      │  ├─ PauseResumeButton (Button)
      │  ├─ StepButton (Button)
      │  ├─ TickLabel (Label)
      │  ├─ TickSpinBox (SpinBox)
      │  ├─ DumpFrameButton (Button)
      │  ├─ FindFrameButton (Button)
      │  └─ CompareButton (Button)
      ├─ BoardComparison (HBoxContainer)
      ├─ StatusLabel (Label)
      ├─ BoardComparison (HBoxContainer)
      │  ├─ SimulationBoard (Control + EnemyTraceBoardView.cs)
      │  └─ MameTraceBoard (Control + EnemyTraceBoardView.cs)
      └─ Console (TextEdit)
```

The previous visible text fields for MAME config and trace path were removed in v0.2.5 to save vertical space. The workflow is now button-driven, with paths coming from configuration and generated trace metadata. The toolbar now includes a **⚙** settings button that opens a modal editor for `config/mame_trace_settings.json`.

## 5. Current scripts

### 5.1 EnemyTraceSimulatorWindow.cs

Current role:

- root UI controller;
- binds scene nodes by path;
- opens and saves the MAME trace settings dialog;
- connects button handlers;
- configures compact playback buttons;
- loads the default logical maze into both board views;
- launches MAME/Lua through `MameTraceLauncher`;
- loads JSON or JSONL trace files through `MameTraceLoader`;
- stores loaded frames in memory;
- manages playback state;
- advances playback at 60 Hz while running;
- supports manual single-frame stepping;
- supports runtime player debug controls;
- supports inactive enemy slot diagnostic toggling;
- supports direct tick navigation through the toolbar tick field;
- opens a separate current-frame diagnostic dump window;
- opens a separate trace navigation helper window;
- reports gate orientation changes between the selected frame and the previous frame;
- supports condition-based trace search from the Find window;
- runs an initial comparison pipeline through the Compare button;
- opens a comparison test window with identity and injected-mismatch checks;
- compares frame metadata, enemyWork, timers, and ports;
- displays the status line below the two boards to avoid toolbar overflow;
- writes messages to the bottom console and to Godot output.

Current playback constant:

```csharp
private const double PlaybackTickSeconds = 1.0 / 60.0;
```

Current toolbar / playback buttons:

```text
⚙    edit MAME trace settings
↺    restart from the first frame
▶    resume playback
❚❚   pause playback
▶|   advance one tick
Tick jump to the requested trace tick
```

Current debug shortcuts:

```text
Ctrl + D       toggle player debug markers
Ctrl + arrows  adjust player sprite visual offset
Ctrl + Home    reset player sprite visual offset to (0, 2)
Ctrl + E       toggle inactive enemy slots
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
- uses `assets/sprites/player/ladybug_spritesheet.png` for the player when available;
- falls back to a simple player debug marker when the sprite is missing;
- renders the player sprite with nearest-neighbor filtering at a fixed 32 x 32 debug size;
- supports optional player debug markers for raw MAME position and turn target;
- draws active level-1 enemies using `enemy_level1.png` when available;
- falls back to simple blue debug markers when the enemy spritesheet is absent;
- hides inactive enemy slots by default;
- can show inactive enemy slots for diagnostics with `Ctrl + E`.

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

Player rendering rules:

```text
source sprite frame size = 64 x 64
debug display size       = 32 x 32
texture filtering        = nearest
default sprite offset    = (0, 2) arcade pixels
```

Level-1 enemy rendering rules:

```text
source sprite frame size = 64 x 64
debug display size       = 32 x 32
texture filtering        = nearest
default sprite offset    = (0, 1) arcade pixels
frames 0,1,2             = right/left animation
frames 3,4,5             = up/down animation
```

MAME-to-debug-board actor Y conversion:

```text
godotArcadeY = 0xDD - mameY
```

The player and enemy sprite offsets only change the visual sprite position. They do not alter the raw MAME coordinates used for trace validation.

When enabled with `Ctrl + D`, player debug markers mean:

```text
cyan P    raw player x/y read from MAME
yellow T  player turn target from turnTargetX / turnTargetY
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

### 5.4 scripts/tools/trace/

Current role:

- owns the trace model classes;
- owns JSON / JSONL parsing;
- centralizes MAME-to-Godot actor coordinate conversion.

Current files:

```text
EnemyTraceFrame.cs
EnemyTraceActor.cs
EnemyTraceGateState.cs
EnemyTraceEnemyWorkState.cs
EnemyTraceTimersState.cs
EnemyTracePortsState.cs
EnemyTraceRawMemoryState.cs
MameTraceCoordinates.cs
MameTraceLoader.cs
```

`MameTraceLoader` currently parses:

- frame / tick index;
- top-level metadata such as schema, phase, MAME frame, PC, and R register;
- player state;
- enemy slots;
- gate states;
- `enemyWork` diagnostic RAM;
- timers;
- input ports;
- optional raw memory blocks such as `logicalMaze6200_62AF`, `ram6000_62AF`, `vramD000_D3FF`, and `colorD400_D7FF`.

It does not yet expose dedicated typed models for all possible future top-level metadata fields. Those remain future v0.3 cleanup work if the trace schema grows.

`MameTraceCoordinates` currently defines the actor Y mirror:

```csharp
MameYMirror = 0xDD
GodotArcadeY = 0xDD - MameY
```

### 5.5 MameTraceLauncher.cs

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

It can be edited directly from the simulator through the **⚙** toolbar button. The settings dialog loads this JSON file, displays one field per setting, and saves the file when **OK** is pressed. **Cancel** closes the dialog without writing changes.

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
- export player turn target;
- export enemy slot state;
- export rotating gate state;
- use the loaded MAME save-state as the start of the sequence.

The script is still part of the trace-generation pipeline, not a simulation or comparison component.

### 6.4 assets/sprites/player/ladybug_spritesheet.png

Optional local asset used for player rendering.

If the texture is present, the board renders the player with the sprite. If it is absent, the board falls back to a simple debug marker.

### 6.5 assets/sprites/enemies/enemy_level1.png

Optional local asset used for level-1 enemy rendering.

The current viewer supports only level-1 enemy graphics. The sheet is expected to contain six 64 x 64 frames:

```text
0,1,2 = move right / move left through horizontal flip
3,4,5 = move up / move down through vertical flip
```

If the texture is absent, the board falls back to simple blue debug markers.

### 6.6 traces/mame/

This directory is used for generated traces.

Trace outputs are intentionally ignored by Git. The directory can be kept in the repository with `.gitkeep`.

### 6.7 tools/mame/states/

This directory exists as an optional project-side location for MAME state files.

Local `.sta` files are ignored by Git. In practice, the current setup can also point directly to the normal MAME `sta` directory through `config/mame_trace_settings.json`.

## 7. Current trace model classes

The trace model classes are now stored under:

```text
scripts/tools/trace/
```

Current classes:

```text
EnemyTraceFrame
EnemyTraceActor
EnemyTraceGateState
EnemyTraceEnemyWorkState
EnemyTraceTimersState
EnemyTracePortsState
EnemyTraceRawMemoryState
MameTraceCoordinates
MameTraceLoader
```

This means `EnemyTraceSimulatorWindow.cs` is no longer responsible for JSON parsing or for defining the trace DTOs. It still owns UI state, settings dialog behavior, playback, tick navigation, and console logging.

Current limitation:

- top-level metadata is still only partially represented beyond the currently used fields.

## 8. Current behavior

### 8.1 Startup

On `_Ready()`:

1. the UI nodes are bound;
2. playback buttons are configured;
3. button events are connected;
4. both boards load `res://data/maze.json`;
5. startup messages are written to the console.

### 8.2 Editing MAME settings

When **⚙** is pressed:

1. the simulator loads `res://config/mame_trace_settings.json`;
2. a modal settings window is opened;
3. string, integer, and boolean fields are displayed;
4. **OK** writes the edited JSON back to disk;
5. **Cancel** closes the window without saving.

The settings window is custom-built rather than using the native `ConfirmationDialog`, so the OK / Cancel buttons remain visible inside the simulator viewport.

### 8.3 Launching MAME/Lua

When **Lancer MAME/Lua** is pressed:

1. the simulator reads `res://config/mame_trace_settings.json`;
2. the launcher resolves paths;
3. a runtime Lua configuration is generated;
4. MAME is started with the configured game, ROM path, state directory, save-state, and Lua script;
5. the UI waits for MAME to terminate;
6. launcher messages are written to the console;
7. the expected generated trace path is remembered for loading.

### 8.4 Loading a trace

When **Charger trace** is pressed:

1. the simulator loads the most recent generated trace path when available;
2. otherwise it falls back to the configured default trace path;
3. JSONL is parsed line by line;
4. frame data is stored in memory;
5. frame 0 is displayed on both boards;
6. the status label is updated;
7. the console reports the frame count, gate count, metadata, raw memory block sizes, first player position, and a compact diagnostic scan of the first frames when available.

### 8.5 Playback

The toolbar includes a tick field after the step button. After a trace is loaded, it displays the current trace tick. Entering a tick jumps directly to the corresponding frame. If the exact tick is not present, the nearest frame is displayed and a console message explains the fallback.

When **↺** is pressed:

- playback restarts from the first frame;
- the state becomes running.

When **▶ / ❚❚** is pressed:

- playback resumes or pauses;
- the button text changes according to the current state.

When **▶|** is pressed:

- playback pauses;
- one frame is advanced manually.

When **Dump** is pressed:

- a separate diagnostic window opens for the current frame;
- the main console receives only a short activity message;
- the dump window shows metadata, player state, enemy slots, gates, gate changes from the previous frame, enemy work RAM, timers, ports, and memory block sizes.

When **Find** is pressed:

- a separate navigation helper window opens;
- the user can jump to the first active enemy frame;
- the user can jump to the first enemy direction change;
- the user can search the first active frame for a selected enemy slot;
- the user can search the first direction change for a selected enemy slot;
- the user can jump to the first gate change;
- when jumping to the first gate change, the console reports the changed gate ids and orientation transitions;
- the user can run condition-based searches with **Find condition** or **Find next**;
- supported conditions currently include `enemyWork rejectedMask != 0`, `enemyWork fallbackMask != 0`, `enemyWork tempDir == value`, active enemy direction checks, selected-slot direction checks, and player direction checks;
- the selected frame is displayed immediately and playback is paused.

When **Compare** is pressed:

- a separate **Trace comparison** window opens;
- **Run identity comparison** compares the loaded MAME trace against an identity simulation generated from the same trace;
- the expected identity result is zero mismatches;
- **Run injected mismatch test** deliberately alters the first active enemy X coordinate by one pixel;
- the injected test is expected to report a mismatch;
- the console reports compared frame count and mismatch count;
- comparison currently covers actors, gates, metadata, `enemyWork`, timers, and ports;
- if a mismatch is found, the first mismatch is reported and the viewer jumps to that frame.

### 8.6 Player debug workflow

By default, the board stays visually clean.

Use `Ctrl + D` to show or hide player debug markers:

```text
P = raw MAME player x/y
T = turnTargetX / turnTargetY
```

Use `Ctrl + arrows` to tune only the visual player sprite offset. This is useful when comparing screenshots against MAME. The current default is:

```text
(0, 2)
```

Use `Ctrl + E` to show or hide inactive enemy slots. Inactive slots are hidden by default because they can contain stale or diagnostic RAM positions that should not be treated as visible monsters.

Use `Ctrl + Home` to restore that default.

## 9. Implemented now

- Standalone Godot project skeleton.
- Main scene with compact simulator UI.
- Settings dialog for `config/mame_trace_settings.json`.
- Scene-authored controls.
- C# node binding.
- C# button handling.
- Console output area.
- JSON configuration for MAME paths and trace parameters.
- C# external MAME process launch.
- Runtime Lua configuration generation.
- MAME Lua trace script integration.
- Save-state-based trace start through MAME configuration.
- JSONL trace loading through `MameTraceLoader`.
- Trace model classes extracted into `scripts/tools/trace/`.
- Diagnostic trace blocks parsed: `enemyWork`, timers, and ports.
- Optional raw memory blocks parsed into `EnemyTraceRawMemoryState`.
- Frame playback at 60 Hz.
- Manual tick/frame stepping.
- Direct tick jump field.
- Current-frame diagnostic dump window.
- Trace navigation helper window.
- Gate-change diagnostics in frame dumps and Find results.
- Condition-based trace search.
- Initial comparison data model.
- Identity trace comparison through the Compare button.
- Injected mismatch comparison test.
- Diagnostic state comparison: metadata, enemyWork, timers, ports.
- Native subwindows for diagnostic windows.
- Logical maze rendering.
- Rotating gate debug rendering.
- Player sprite rendering with optional debug markers.
- Runtime player sprite offset tuning.
- Level-1 enemy sprite rendering.
- Inactive enemy slot diagnostic toggle.
- MAME Y mirror conversion for actor rendering.
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
- Level 2+ enemy sprite selection.
- Original gate sprite rendering.
- Advanced validation in the MAME settings dialog, such as path existence checks or browse buttons.
- Deterministic handling and replay of random arcade decisions on the C# side.

## 11. Current known limitations

### 11.1 Left and right boards are not different yet

The left board is named `Simulation C# / Godot`, but it currently displays the same frame as the MAME trace board.

This is a temporary visualization placeholder.

### 11.2 Actor coordinate mapping is provisional

The rotating gates are now aligned with the debug maze, and actor Y positions are now mirrored from MAME space into the debug-board space with `0xDD - mameY`.

Actor placement is much closer to the MAME screenshots, but it is still considered provisional until the conversion is fully documented and tested against more scenarios.

### 11.3 Actor sprite offsets are visual only

The current player sprite offset `(0, 2)` and enemy sprite offset `(0, 1)` were chosen by visual comparison with MAME screenshots.

They are useful for readability, but they are not gameplay coordinates. The raw MAME coordinates remain the source of truth.

### 11.4 Enemy rendering is level-1 only

The viewer currently renders enemies with the level-1 spritesheet only. Later versions should select the enemy spritesheet from trace metadata or simulator configuration.

### 11.5 Trace schema is still evolving

The JSONL trace schema is practical enough for the current viewer, but it is not final.

The parser is intentionally tolerant, but the project should eventually define one canonical trace schema.

### 11.6 Trace model classes need extraction

The trace DTOs currently live in `EnemyTraceSimulatorWindow.cs`.

They should move into separate files before the comparison logic grows.

### 11.7 Settings dialog limitations

The settings dialog currently supports manual text editing and basic integer / boolean fields only.

It does not yet provide file or directory picker buttons, path validation, or per-field error messages.

### 11.8 Godot .NET rebuild behavior

After replacing C# files, Godot can keep running an older compiled assembly.

When the UI does not reflect the expected patch, use:

```text
MSBuild > Rebuild
```

then relaunch the scene.

### 11.9 No collectibles

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

The original roadmap has been refreshed after the v0.2.x implementation work.

Already implemented before the next phase:

- MAME/Lua launch from Godot;
- settings dialog for `config/mame_trace_settings.json`;
- JSONL trace loading;
- tick playback and direct tick jump;
- maze and gate rendering;
- player sprite rendering;
- level-1 enemy sprite rendering;
- MAME Y mirror conversion for actor rendering;
- optional player and inactive-enemy diagnostics.

The next steps should focus on making the trace and comparison architecture cleaner, rather than adding more visual features first.

### v0.3: trace model cleanup and loader extraction

Status after v0.3.4: trace extraction, diagnostic DTOs, and raw memory block parsing done.

Implemented:

- `EnemyTraceFrame`, `EnemyTraceActor`, and `EnemyTraceGateState` moved into separate files;
- dedicated `MameTraceLoader` added;
- JSONL parsing centralized;
- MAME-to-Godot actor coordinate conversion centralized in `MameTraceCoordinates`;
- `EnemyTraceSimulatorWindow.cs` no longer owns trace parsing or trace model definitions;
- current UI behavior kept unchanged;
- `enemyWork`, timers, and ports parsed into dedicated DTOs;
- optional raw memory blocks parsed into `EnemyTraceRawMemoryState`;
- first-frame enemy scan logs compact enemy-work diagnostics;
- trace loading logs raw memory block sizes when present.

Remaining v0.3 cleanup:

- add explicit DTOs for top-level metadata if needed;
- document which fields are source-of-truth and which are visual/debug fields;
- add loader-focused sample trace checks or tests.

### v0.4: trace inspection and diagnostic state

Status after v0.4.8: current-frame dump window, trace navigation helpers, gate-change diagnostics, and condition-based search implemented.

Implemented:

- toolbar **Dump** button;
- separate diagnostic window for the current frame;
- diagnostic dump of frame metadata, player state, enemy slots, gates, `enemyWork`, timers, ports, and raw-memory block sizes;
- compact main console;
- native subwindows so diagnostic windows are not trapped inside the main viewport;
- toolbar **Find** button;
- navigation helpers for first active enemy, first direction change, selected-slot first active frame, selected-slot first direction change, and first gate change;
- gate-change diagnostics comparing the selected frame to the previous frame;
- console summary of gate ids and orientation transitions after **Find → First gate change**;
- condition-based search with **Find condition** and **Find next**.

Remaining v0.4 work:

- keep this phase read-only;
- after this, the next roadmap phase is v0.5: comparison data model.

### v0.5: comparison data model

Status after v0.5.6: initial comparison model, identity comparison, injected mismatch test, and diagnostic state comparison implemented.

Implemented:

- `SimulationFrame`;
- `SimulationActorState`;
- `SimulationGateState`;
- `SimulationEnemyWorkState`;
- `SimulationTimersState`;
- `SimulationPortsState`;
- `ComparisonFrame`;
- `TraceMismatch` and `TraceMismatchKind`, including `Metadata`, `EnemyWork`, `Timer`, and `Port`;
- `TraceComparisonResult`;
- `TraceComparisonRunner`;
- `TraceSimulationStub` identity source;
- injected mismatch source that alters the first active enemy X coordinate;
- comparison of frame metadata, `enemyWork`, timers, and ports;
- toolbar **Compare** button;
- comparison test window;
- console report with compared frame count, mismatch count, and first mismatch when present;
- automatic jump to the first mismatch frame;
- status label moved below the board views.

Remaining v0.5 work:

- keep the left board trace-driven until the comparison model is stable;
- then move to v0.6: C# enemy simulation adapter.

### v0.6: C# enemy simulation adapter

Goal:
- connect the diagnostic tool to the real C# enemy movement logic.

Planned changes:

- reuse or port the existing enemy movement classes from the Lady Bug remake;
- create a standalone simulation adapter independent of the normal game scene;
- initialize the simulation from the MAME trace:
  - maze;
  - gates;
  - player position;
  - enemy slots;
  - timers;
  - chase state;
  - enemy work state, if required;
- advance one tick at a time;
- compare simulated enemy positions and directions to MAME.

### v0.7: mismatch visualization

Goal:
- make divergences obvious in the UI.

Planned changes:

- highlight mismatching enemies or gates;
- add first-mismatch navigation;
- add previous/next mismatch navigation;
- add exportable mismatch reports;
- add focused scenarios for:
  - center decisions;
  - fallback;
  - door-local rejection;
  - forced reversal;
  - chase/BFS overrides.

### Later improvements

Possible later additions:

- original gate sprite rendering;
- level 2+ enemy sprite selection;
- file/directory picker buttons in the settings dialog;
- trace schema versioning and validation;
- frame scrubber or timeline view;
- side-by-side MAME screenshot reference support, if useful.

## 14. Design principle

The simulator should not try to look like the final game.

It should be a strict diagnostic tool:

- integer arcade-pixel positions;
- explicit frame numbers;
- raw MAME state visible when needed;
- deterministic playback;
- minimal visuals;
- precise mismatch reporting.
