# Current Implementation

**Project:** Enemy Trace Simulator  
**Current package version:** v0.1.2  
**Engine target:** Godot Engine .NET 4.6.2  
**Language:** C#  

## Purpose of this document

This document describes only what is currently implemented in this repository.

It intentionally avoids describing future systems as if they already existed. Planned work is listed in the final section.

## 1. Project goal

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool for validating the enemy movement logic of the Lady Bug arcade remake.

The intended final workflow is:

1. run Lady Bug in MAME;
2. export a deterministic trace with a Lua script;
3. load that trace in Godot;
4. run the C# enemy simulation from the same starting state;
5. compare the C# simulation against the MAME trace frame by frame.

The current version does not yet perform the real comparison. It provides the first UI and playback shell.

## 2. Current repository structure

```text
.
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
│     └─ EnemyTraceSimulatorWindow.cs
├─ tools/
│  ├─ mame/
│  │  └─ ladybug_enemy_trace_v0.lua
│  └─ simulator/
│     └─ sample_enemy_trace.json
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

In `project.godot`:

```text
run/main_scene="res://scenes/tools/EnemyTraceSimulator.tscn"
```

The current viewport is configured as:

```text
window/size/viewport_width=1152
window/size/viewport_height=648
```

## 4. Scene structure

### 4.1 EnemyTraceSimulator.tscn

Current scene tree:

```text
EnemyTraceSimulator (Control)
└─ Root (MarginContainer)
   └─ MainLayout (VBoxContainer)
      ├─ Title (Label)
      ├─ LuaPathRow (HBoxContainer)
      │  ├─ LuaScriptLabel (Label)
      │  ├─ LuaScriptPath (LineEdit)
      │  └─ LaunchMameLuaButton (Button)
      ├─ TracePathRow (HBoxContainer)
      │  ├─ TracePathLabel (Label)
      │  ├─ TracePath (LineEdit)
      │  └─ LoadTraceButton (Button)
      ├─ PlaybackControls (HBoxContainer)
      │  ├─ RunSimulationButton (Button)
      │  ├─ PauseResumeButton (Button)
      │  ├─ StepButton (Button)
      │  └─ StatusLabel (Label)
      ├─ BoardComparison (HBoxContainer)
      │  ├─ SimulationBoard (Control + EnemyTraceBoardView.cs)
      │  └─ MameTraceBoard (Control + EnemyTraceBoardView.cs)
      └─ Console (TextEdit)
```

The scene-authored UI is intentional. It means the interface is visible even if the C# assembly has not been rebuilt yet. If the script is missing or fails to compile, the buttons will not work, but the window should not appear empty.

## 5. Current scripts

### 5.1 EnemyTraceSimulatorWindow.cs

Current role:

- root UI controller;
- binds scene nodes by path;
- connects button handlers;
- loads the default logical maze into both board views;
- loads a JSON trace file;
- stores loaded frames in memory;
- manages playback state;
- advances playback at 60 Hz while running;
- supports manual single-frame stepping;
- writes messages to the bottom console and to Godot output.

Current playback constants:

```csharp
private const double PlaybackTickSeconds = 1.0 / 60.0;
```

Current default paths shown in the UI:

```text
res://tools/mame/ladybug_enemy_trace_v0.lua
res://tools/simulator/sample_enemy_trace.json
```

Important current limitation:

- the left board and the right board currently display the same loaded trace frame;
- the left board is reserved for the future C# simulation output.

### 5.2 EnemyTraceBoardView.cs

Current role:

- draws one diagnostic board;
- loads `data/maze.json`;
- draws a background rectangle;
- draws a logical grid;
- draws static maze walls from wall bitmasks;
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

Actor positions are interpreted as original arcade-pixel coordinates, not Godot scene pixels.

## 6. Current data files

### 6.1 data/maze.json

The current maze file contains:

```text
width  = 11
height = 11
cells  = flat array of 121 wall masks
```

It is used only for drawing the static logical maze in the diagnostic board views.

Rotating gates are not yet rendered from this file.

### 6.2 tools/simulator/sample_enemy_trace.json

This is a hand-authored placeholder trace used to validate the UI and playback code.

Current top-level fields:

```text
meta
initial_state
frames
```

Current frame fields:

```text
frame
player
enemies
gates
```

Current actor fields:

```text
slot
x
y
dir
active
```

Current gate fields:

```text
gate_id
orientation
```

The sample trace is not exported from MAME. It is only test data.

### 6.3 tools/mame/ladybug_enemy_trace_v0.lua

This file is only a placeholder.

It currently documents the planned direction of the MAME exporter but does not yet export a usable trace.

The comments currently remind the expected important RAM areas:

```text
Player state begins at 0x6026.
Enemy slots are five bytes each, starting at 0x602B, 0x6030, 0x6035, 0x603A.
Enemy direction encoding: 01=left, 02=up, 04=right, 08=down.
```

## 7. Current trace model classes

The trace model classes are currently defined at the bottom of `EnemyTraceSimulatorWindow.cs`.

Current classes:

```text
EnemyTraceFile
EnemyTraceMeta
EnemyTraceInitialState
EnemyTraceFrame
EnemyTraceActor
EnemyTraceGateState
```

This is acceptable for the initial UI prototype, but these classes should be moved into separate files in the next cleanup step.

## 8. Current behavior

### 8.1 Startup

On `_Ready()`:

1. the UI nodes are bound;
2. button events are connected;
3. both boards load `res://data/maze.json`;
4. startup messages are written to the console.

### 8.2 Loading a trace

When **Charger trace** is pressed:

1. the path is read from the trace path field;
2. the file is checked with `FileAccess.FileExists`;
3. the JSON is parsed with `System.Text.Json`;
4. the frame list is stored;
5. frame 0 is displayed on both boards;
6. the status label is updated.

### 8.3 Playback

When **Lancer simulation** is pressed:

- playback starts;
- `_Process()` accumulates elapsed time;
- the current frame advances at 60 Hz.

When **Pause / Continuer** is pressed:

- playback pauses or resumes.

When **Tick suivant** is pressed:

- playback pauses;
- one frame is advanced manually.

## 9. Implemented now

- Standalone Godot project skeleton.
- Main scene with the simulator UI.
- Scene-authored controls.
- C# node binding.
- C# button handling.
- Console output area.
- Logical maze rendering.
- Sample JSON trace loading.
- Frame playback at 60 Hz.
- Manual tick/frame stepping.
- Player and enemy debug rendering.
- Placeholder MAME Lua path.

## 10. Not implemented yet

- Real MAME launch command.
- Real MAME Lua trace generation.
- Process management for MAME.
- Save-state loading.
- Input scripting for MAME.
- Exact arcade frame sampling point.
- Export of RAM bytes from the real arcade runtime.
- Initial rotating-gate extraction from MAME memory.
- Rendering of rotating gates.
- Simulation-side enemy movement.
- Reuse of the current Lady Bug remake enemy classes.
- Frame-by-frame comparison.
- Mismatch report.
- Navigation to first mismatch.
- Export of comparison logs.
- Deterministic handling of random arcade decisions.

## 11. Current known limitations

### 11.1 Left and right boards are not different yet

The left board is named `Simulation C# / Godot`, but it currently displays the same frame as the MAME trace board.

This is a temporary visualization placeholder.

### 11.2 The Lua script is not functional yet

The current Lua file is only a planning stub. It should not be expected to run in MAME yet.

### 11.3 The trace schema is provisional

The JSON schema is only the first draft. It will probably need additional fields for useful enemy validation, including raw direction bytes, flags, preferred directions, chase timers, rejection masks, and possibly raw RAM snapshots around important addresses.

### 11.4 Gate state is not used yet

The sample trace already contains `gates`, but the renderer does not display them and no movement logic consumes them.

### 11.5 No collectibles

Collectibles are intentionally absent from the current simulator view. The validation target is enemy movement, not scoring or item collection.

## 12. Planned next steps

### v0.2: trace and comparison architecture

- Move trace model classes into separate files.
- Add `MameTraceLoader`.
- Add `TracePlaybackState`.
- Add `SimulationFrame` / `ComparisonFrame` types.
- Add `TraceComparisonRunner`.
- Add a first mismatch model.
- Keep simulation output simple until the comparison pipeline is solid.

### v0.3: real MAME Lua exporter

- Implement a working Lua script for the current MAME version.
- Export the player state.
- Export all four enemy slots.
- Export active flags and raw direction bytes.
- Export relevant enemy AI helper RAM when possible.
- Export initial rotating-gate state.
- Decide exactly when to sample during the arcade frame.

### v0.4: C# simulation adapter

- Create a simulation adapter independent of the normal game scene.
- Reuse or port the existing enemy movement classes from the Lady Bug remake.
- Initialize the simulation from the trace initial state.
- Advance one tick at a time.
- Compare each simulated frame to the MAME frame.

### v0.5: visual and diagnostic improvements

- Render rotating gates.
- Highlight mismatching actors.
- Add frame scrubber.
- Add first-mismatch jump.
- Add exportable mismatch report.

## 13. Design principle

The simulator should not try to look like the final game.

It should be a strict diagnostic tool:

- integer arcade-pixel positions;
- explicit frame numbers;
- raw MAME state visible when needed;
- deterministic playback;
- minimal visuals;
- precise mismatch reporting.
