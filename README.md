# Enemy Trace Simulator

Enemy Trace Simulator is a small Godot/.NET tool for validating the enemy movement logic of the arcade game **Lady Bug**.

The long-term goal is to compare two timelines frame by frame:

- the **reference timeline**, exported from MAME with a Lua trace script;
- the **candidate timeline**, produced by the C# / Godot enemy simulation.

The first milestone is intentionally modest: create a clean standalone window that can load a trace file, display the maze, show actors on two side-by-side boards, and step through the recorded frames.

## Current status

Current package version: **v0.1.2**

Implemented now:

- standalone Godot 4.6.2 .NET project skeleton;
- main scene: `scenes/tools/EnemyTraceSimulator.tscn`;
- C# UI controller: `scripts/tools/EnemyTraceSimulatorWindow.cs`;
- C# board renderer: `scripts/tools/EnemyTraceBoardView.cs`;
- logical 11 x 11 maze rendering from `data/maze.json`;
- JSON sample trace loading from `tools/simulator/sample_enemy_trace.json`;
- playback controls: run, pause/resume, tick/frame step;
- two synchronized boards: left for future C# simulation, right for MAME trace;
- bottom console area;
- placeholder MAME Lua script path.

Not implemented yet:

- real MAME process launching;
- real Lua trace export;
- real C# enemy simulation;
- frame-by-frame mismatch detection;
- rotating gate rendering and collision validation;
- integration with the full Lady Bug game codebase.

## Requirements

- Godot Engine .NET 4.6.2
- .NET SDK compatible with Godot .NET projects
- MAME with Lua support, later, when trace export is implemented

This repository does **not** include Lady Bug ROM files, MAME binaries, or copyrighted arcade assets.

## Running the current tool

1. Open this repository folder in Godot Engine .NET.
2. Let Godot generate/import the C# project files if needed.
3. Build the C# project from Godot or with:

```powershell
dotnet build
```

4. Run the main scene:

```text
scenes/tools/EnemyTraceSimulator.tscn
```

5. Click **Charger trace** to load the default sample trace:

```text
res://tools/simulator/sample_enemy_trace.json
```

6. Use **Lancer simulation**, **Pause / Continuer**, and **Tick suivant**.

In v0.1.2, both boards display the same trace frame. The left board will become the real C# / Godot simulation output in a later version.

## Repository layout

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
└─ README.md
```

## Trace format, current draft

The current trace format is a temporary JSON schema:

```json
{
  "meta": {
	"game": "ladybug",
	"source": "sample placeholder trace",
	"tick_rate": 60
  },
  "initial_state": {
	"frame": 0,
	"gates": [
	  { "gate_id": 0, "orientation": "horizontal" }
	]
  },
  "frames": [
	{
	  "frame": 0,
	  "player": { "slot": -1, "x": 88, "y": 86, "dir": "down", "active": true },
	  "enemies": [
		{ "slot": 0, "x": 88, "y": 86, "dir": "up", "active": true }
	  ],
	  "gates": [
		{ "gate_id": 0, "orientation": "horizontal" }
	  ]
	}
  ]
}
```

Positions are expressed in original arcade pixels. The current renderer assumes 16 arcade pixels per logical maze cell.

Enemy direction encoding used by the reverse-engineering notes:

```text
01 = left
02 = up
04 = right
08 = down
```

## Design direction

The simulator should be strict and boring on purpose. MAME is treated as the reference source, and the C# / Godot movement logic is advanced from the same initial state and the same input timeline. The comparison tool should then report the first frame where the two states diverge.

The long-term comparison loop should look like this:

```text
MAME Lua trace -> JSON trace -> Godot trace loader
								├─ right board: raw MAME frame
								└─ left board: C# simulation frame
									  ↓
							   comparator / mismatch report
```

## Roadmap

### v0.2

- Extract trace model classes into separate files.
- Add a proper comparison model: expected frame, actual frame, mismatch list.
- Add a first textual mismatch report.
- Keep the simulation side fake or trace-driven until the comparison plumbing is stable.

### v0.3

- Implement a real MAME Lua exporter.
- Export player state, enemy slots, direction bytes, active flags, preferred directions, chase timers, and gate states.
- Decide the exact sampling point in the arcade frame.

### v0.4

- Add a real C# simulation adapter.
- Reuse or port the enemy movement classes from the Lady Bug Godot project.
- Compare simulation output to the MAME trace frame by frame.

### v0.5+

- Render rotating gates.
- Add breakpoint-style mismatch navigation.
- Add export of mismatch reports.
- Add focused test scenarios for center decisions, fallback, door-local rejection, forced reversal, and chase/BFS overrides.

## Relationship with the Lady Bug remake

This repository is a validation tool for the Lady Bug remake project. It is deliberately separate from the game scene so that enemy movement can be tested without collectibles, score, HUD, transition screens, or normal player rendering.

The current renderer is only a diagnostic view. It is not intended to reproduce the final arcade visuals.
