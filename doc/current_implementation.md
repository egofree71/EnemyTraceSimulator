# Current Implementation

**Project:** Enemy Trace Simulator  
**Current package version:** v0.9.6  
**Latest validated milestone:** `Validate v0.9.6 transition source-path enemy inspector`  
**Engine target:** Godot Engine .NET 4.6.2  
**Language:** C#  

## Purpose of this document

This document describes the current implementation of Enemy Trace Simulator, the reverse-engineering assumptions it depends on, the validation workflows, and the next steps.

The root `README.md` gives the project overview. This file records the detailed implementation state.

## 1. Project goal

Enemy Trace Simulator is a standalone Godot/.NET diagnostic tool for validating and debugging enemy movement logic for a Lady Bug arcade remake.

The intended workflow is:

```text
1. run Lady Bug in MAME;
2. load a known MAME save-state;
3. export a deterministic JSONL reference trace;
4. load that trace in the simulator;
5. initialize a C# / Godot candidate simulation from the same state;
6. run the visual replay tick by tick;
7. compare the candidate against MAME;
8. stop at the first useful mismatch;
9. use deeper diagnostics to explain and fix the divergence.
```

The simulator should not become a second MAME-assisted game engine. Its role is to compare the real or candidate Godot enemy logic against the MAME reference.

## 2. v0.9 strategic shift

v0.8.0 validated a replay bridge. v0.9 changes the direction.

Previous diagnostic direction:

```text
MAME exact-PC logs -> replay provider -> runtime decisions -> perfect trace replay
```

Current v0.9 direction:

```text
MAME trace = reference
Godot / C# logic = candidate
Enemy Trace Simulator = visual comparison harness
```

This means:

```text
- standard JSONL traces are reference inputs;
- exact-PC logs are diagnostic aids;
- error.log must not become a normal gameplay dependency;
- the main success criterion is finding the first useful divergence.
```

A perfect `mismatches=0` remains useful when validating a known bridge, but the v0.9 goal is broader: make it easy to see where the current Godot logic first disagrees with the arcade.

## 3. Main UI workflow

The main window shows two boards:

```text
left  = Simulation C# / Godot
right = Trace MAME
```

The toolbar contains:

```text
⚙    edit MAME trace settings
MAME generate / launch trace capture
Load load the current JSONL trace
↺    restart visual sequence from the beginning
▶    resume playback
❚❚   pause playback
▶|   advance one tick manually
Dump dump the current frame
Find helper navigation in the trace
Compare open analytical comparison tools
```

The three visual sequence buttons are:

```text
↺   reset to frame 0
▶/❚❚ pause or resume
▶|  advance exactly one tick
```

### 3.1 Restart behavior

Restart sets:

```text
_currentFrameIndex = 0
_isRunning = true
_isPaused = true
_playbackAccumulator = 0
```

It applies frame 0 on both boards, clears the current visual mismatch, and resets the status message.

The replay does not need to auto-start after reset. The user can press play or advance tick by tick.

### 3.2 Playback behavior

During automatic playback, `_Process()` accumulates time and calls `StepOneFrame()` at 60 Hz:

```text
PlaybackTickSeconds = 1.0 / 60.0
```

After each step, the simulator displays the current frame and compares the current visual state.

If a mismatch occurs:

```text
_isPaused = true
playback stops immediately
the display remains on the mismatching tick
the status label shows the mismatch
the console logs the mismatch details
```

### 3.3 Tick-by-tick behavior

The manual step button uses the same comparison path as automatic playback.

A click on `▶|`:

```text
1. advances to the next frame if possible;
2. applies the frame to both boards;
3. compares the visual state;
4. logs any mismatch and leaves the UI on that tick.
```

This makes manual and automatic replay consistent.

## 4. Visual replay comparator

Implemented files:

```text
scripts/tools/comparison/VisualReplayComparator.cs
scripts/tools/comparison/VisualReplayFrameConverter.cs
scripts/tools/comparison/VisualReplayMismatch.cs
```

The comparator is intentionally visual-first. It compares what should matter for the side-by-side replay:

```text
player:  active, x, y, direction
enemies: active, x, y, direction
gates:   orientation, pivot
```

It does not compare internal diagnostic state:

```text
EnemyWork.preferred[]
EnemyWork.rejectedMask
fallback helper / 0x61C2
chase timers
round-robin selector
temporary scratch fields
ports
timers
raw memory blocks
```

Those fields are still useful in analytical tools, but they are not part of the normal visual stop condition.

### 4.1 Simulation frame conversion

`VisualReplayFrameConverter` converts an `EnemyTraceFrame` into a neutral `SimulationFrame` so both sides can be compared with one common model.

Current conversion preserves MAME-coordinate values for comparison. Display conversion is handled by the board renderer.

### 4.2 Mismatch shape

`VisualReplayMismatch` stores the first mismatch found:

```text
hasMismatch
frameIndex
tick
category
details
```

The mismatch is printed to the console and surfaced in the status label.

### 4.3 Den / lair release rule

Enemy release from the central den is special.

The arcade places an enemy in the den and forces it upward through the exit. The den is surrounded by walls on the left, bottom, and right, so treating this as ordinary maze movement produces false conclusions.

The visual comparator uses this inactive-slot rule:

```text
inactive vs inactive -> OK, ignore x/y/dir
inactive vs active   -> mismatch
active vs inactive   -> mismatch
active vs active     -> compare x/y/dir
```

This means the release sequence is not ignored. It is compared as visible behavior once the slot becomes active.

If the first mismatch happens in the den-release area, the log can label it as a lair-release-zone mismatch, but it is still a real mismatch if activation, position, or direction differs.

## 5. Board rendering

Implemented file:

```text
scripts/tools/EnemyTraceBoardView.cs
```

The board renders:

```text
- static logical maze from data/maze.json;
- rotating gates from the loaded trace;
- player sprite;
- enemy sprites;
- optional inactive enemy slots.
```

The old diagnostic overlay from the v0.7 single-enemy release focus has been removed from the normal display:

```text
removed: bottom HUD overlay on the maze
removed: D den marker
removed: E0 highlight ring
removed: yellow recent trail
removed: player raw-position cross
removed: yellow target line
```

The simulator now favors a cleaner visual comparison between the left and right boards.

`Ctrl+E` remains useful for toggling inactive known enemy slots.

The previous player-debug marker and single-enemy-focus overlay are no longer part of the normal workflow.

## 6. MAME trace workflow

### 6.1 Standard JSONL trace

Generated by:

```text
tools/mame/lua/ladybug_sequence_trace.lua
```

Recommended settings for the current 601-frame validation trace:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_sequence_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_fullmem",
  "framesAfterTick0": 600,
  "includeFullMemoryEachFrame": true,
  "includeLogicalMazeEachFrame": true,
  "enableDebugger": false
}
```

Expected trace:

```text
res://traces/mame/ladybug_sequence_v8_fullmem_trace.jsonl
```

Do not enable the MAME debugger for the standard JSONL trace. Earlier combined attempts could produce an unusable 1-frame capture.

No new Lua script is required for the v0.9.6 milestone. The problems found between v0.9.1 and v0.9.5 were diagnostic interpretation errors in the simulator:

```text
- logicalMaze6200_62AF was over-treated as a final movement oracle;
- all four directions were probed even when the source path did not test them;
- the displayed frame was initially inspected as the source input frame;
- source enemy vertical directions were compared to maze.json without the Y mirror.
```

### 6.2 Exact-PC diagnostic trace

Generated by:

```text
tools/mame/lua/ladybug_enemywork_pc_trace.lua
```

This produces diagnostic output such as:

```text
tools/mame/lua/error.log
```

This is not a standard JSONL trace and should not be loaded through the normal trace loader.

It remains useful for investigating exact source behavior, but v0.9 does not use it as a normal runtime input.

## 7. Trace model

Important trace classes:

```text
scripts/tools/trace/EnemyTraceFrame.cs
scripts/tools/trace/EnemyTraceActor.cs
scripts/tools/trace/EnemyTraceGateState.cs
scripts/tools/trace/EnemyTraceEnemyWorkState.cs
scripts/tools/trace/EnemyTraceRawMemoryState.cs
scripts/tools/trace/MameTraceCoordinates.cs
```

A frame can include:

```text
schema
phase
frame / tick
mameFrame
pc
r
player
enemies
gates
enemyWork
timers
ports
rawMemory
```

Full-memory traces include:

```text
logicalMaze6200_62AF
ram6000_62AF
vramD000_D3FF
colorD400_D7FF
```

The raw-memory blocks are used by static maze and tile-level diagnostics.

## 8. Coordinate conventions

### 8.1 MAME Y mirror

MAME RAM Y and Godot display Y are mirrored.

The project uses the verified conversion:

```csharp
private const int MameYMirror = 0xDD;

private static int ToMameY(int godotArcadeY)
{
    return MameYMirror - godotArcadeY;
}
```

Therefore:

```text
godotArcadeY = 0xDD - mameY
mameY        = 0xDD - godotArcadeY
```

This is essential for visual rendering, board overlays, and cell/probe diagnostics.

### 8.2 Enemy decision center

Enemy movement decisions occur at enemy decision centers:

```text
x & 0x0F == 0x08
y & 0x0F == 0x06
```

This is not necessarily the same as the player anchor or the generic gameplay anchor.

### 8.3 Enemy direction encoding

Enemy directions in source/MAME enemy logic:

```text
01 = left
02 = up
04 = right
08 = down
```

### 8.4 Player direction / input encoding

Player direction/input uses a different vertical convention:

```text
01 = left
02 = down
04 = right
08 = up
```

Do not mix player and enemy direction encodings.

### 8.5 Enemy source direction vs Godot maze direction

Because MAME Y and Godot display Y are mirrored, vertical enemy source directions must be inverted when checking `maze.json`:

```text
source enemy 01/left  -> Godot maze left
source enemy 02/up    -> Godot maze down
source enemy 04/right -> Godot maze right
source enemy 08/down  -> Godot maze up
```

This was the key v0.9.6 correction.

Without this mapping, the source-path inspector can produce false conflicts such as:

```text
source says 08/down accepted
maze.json says down blocked
```

when the correct comparison is actually:

```text
source 08/down -> Godot maze up
```

The v0.9.6 log explicitly reports:

```text
staticMazeDirectionMapping=source-enemy-y-mirrored-to-godot-maze
```

## 9. VRAM and maze conventions

### 9.1 VRAM layout

Lady Bug background VRAM is column-major and bottom-to-top:

```text
D080-D09F = first column, bottom to top
D0A0-D0BF = second column, bottom to top
...
D3E0-D3FF = last column, bottom to top
```

This is a critical detail. A row-major interpretation will produce incorrect tile positions.

### 9.2 `0x3C0A` tile lookup

Validated formula:

```text
HL = 0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
```

Meaning:

```text
D & F8  -> align probe X to 8-pixel tile column
* 4     -> convert to 32-byte column stride
E >> 3  -> tile index inside the column
D0A0    -> base used by this routine
```

### 9.3 Logical maze and movement validation

The logical maze map is stored at:

```text
6200..62AF = 11 x 16 cells
```

Known interpretation:

```text
high nibble = allowed directions
low nibble  = BFS / guidance direction toward Lady Bug
```

Important v0.9 finding:

```text
logicalMaze6200_62AF is useful diagnostic data, but it should not be treated as the sole final movement oracle.
```

The source path uses:

```text
0x3911  logical maze validation using ROM table 0x0DA2
0x4130  local door/tile validation
0x4241  fallback scanning
0x42E6  preferred-direction attempt
0x4325  current-direction check
```

## 10. Source-path decision inspector

Implemented file:

```text
scripts/tools/simulation/LadyBugSourcePathDecisionInspector.cs
```

Supporting files:

```text
scripts/tools/simulation/LadyBugEnemyDecisionModel.cs
scripts/tools/simulation/LadyBugEnemyDecisionGate427EModel.cs
scripts/tools/simulation/LadyBugMameLocalTile4130Oracle.cs
scripts/tools/simulation/LadyBugGodotStaticMazeOracle.cs
scripts/tools/simulation/LadyBugStaticMazeRomTable.cs
```

### 10.1 Why the inspector is transition-based

The arcade update routine works as a transition:

```text
frame[i-1] enemy slot -> source movement routine -> frame[i] committed result
```

v0.9.4 incorrectly inspected the displayed frame as if it were the input to the routine. v0.9.5 fixed this by inspecting transitions.

Current rule:

```text
source input      = frame[i-1] enemy slot
reference result  = frame[i] enemy slot and EnemyWork
```

### 10.2 Source path followed

The inspector follows the source-level path:

```text
0x427E decision gate
  carry clear -> 0x433A outside-center keep/reverse path
  carry set   -> 0x42E6 preferred direction path
                   -> 0x4325 current direction check if preferred rejected
                   -> 0x4241 fallback scan if current rejected
```

It intentionally does not test all four directions independently.

### 10.3 Current validated v0.9.6 result

Current 601-frame trace result:

```text
frames=601
transitions=600
activeStartEnemyStates=595
pixelUnalignedStartStates=558
pixelAlignedStartStates=37
decisionGateCarryClearStates=563
decisionGateCarrySetStates=32
inspectedTransitions=595
testedDirectionProbes=50
preferredAccepted=23
preferredRejectedCurrentKept=1
fallbackEntered=8
fallbackSelected=8
fallbackNotFound=0
outsideCenterKeep=563
outsideCenterForcedReversal=0
sourceAcceptedButStaticBlockedProbes=0
resultMatchesSlot=595
resultMismatchesSlot=0
resultMatchesEnemyWork=595
resultMismatchesEnemyWork=0
missingVramInspections=0
```

Interpretation:

```text
The transition-based inspector reproduces the observed MAME slot and EnemyWork result for every inspected active transition in the current trace.
```

The specific den-adjacent case around `(58,76)` is now correctly explained:

```text
start=(58,76)
preferred=08/down
source 08/down maps to Godot maze up
0x3911 blocks the preferred direction
maze.json also blocks the mirrored Godot up direction
current left is kept
```

This confirms that earlier “MAME allows up/down under the den” reports were false positives in the diagnostic, not problems in `maze.json`.

## 11. Disabled all-four-directions collision probe mode

Earlier v0.9 diagnostics tested:

```text
for each decision center:
  test left, up, right, down
```

This created useful initial signals but was not source-faithful. The arcade does not ask all four independent movement questions at every decision center. It follows:

```text
preferred -> current -> fallback
```

The normal adapter now disables all-four-directions comparison and reports:

```text
allDirectionProbeMode=disabled-by-transition-source-path-inspector
```

If an all-direction diagnostic is needed later, it should be exposed as a separate exploratory tool, not as part of the normal Compare summary.

## 12. Compare window and reference-direction adapter

Implemented file:

```text
scripts/tools/simulation/LadyBugEnemySimulationAdapter.cs
```

Current Compare workflow:

```text
Compare > Lady Bug reference-direction step
```

This path still uses a reference-direction replay adapter and remains useful for validating the trace pipeline and diagnostic models.

It should not be confused with the long-term autonomous candidate simulation.

Normal Compare output has been cleaned compared with the old v0.6-v0.8 shadow summaries, but v0.9.6 still prints too many inspector examples. The next polish step should make the normal output compact.

Recommended normal output after cleanup:

```text
Lady Bug source-path decision inspector v0.9.6:
frames=601
inspectedTransitions=595
resultMismatchesSlot=0
resultMismatchesEnemyWork=0
sourceAcceptedButStaticBlockedProbes=0
preferredAccepted=23
preferredRejectedCurrentKept=1
fallbackSelected=8
outsideCenterKeep=563
staticMazeDirectionMapping=source-enemy-y-mirrored-to-godot-maze
firstMismatch: none
```

Detailed examples should move to a separate dump/diagnostic path if still needed.

## 13. Historical v0.8 diagnostic bridge

v0.8.0 validated a diagnostic bridge for the current one-enemy static-player trace.

Result:

```text
Comparison [Lady Bug reference-direction step]: comparedFrames=501, mismatches=0
```

Runtime bridge removals validated in v0.8:

```text
0x61C1       rejectedMask no longer copied directly from standard JSONL
0x61C2       fallback helper no longer copied directly from standard JSONL
0x61C4-61C7  preferred[] no longer copied directly from standard JSONL when exact-PC provider available
```

Important limitation:

```text
preferred[] was still replayed from exact-PC MAME events in error.log.
```

So v0.8 was a validated replay bridge, not final autonomous AI.

## 14. Source-first enemy movement references

Primary reverse-engineering reference:

```text
LadyBug_enemy_management_extract.txt
```

Important source concepts:

```text
0x05AE  enemy slot allocation / release helper
0x3061  initialize released enemy slot
0x3911  logical maze validation
0x3C0A  tile address lookup
0x4130  local door / tile validation
0x4189  forced reversal tile probe
0x4241  fallback scan
0x427E  decision gate
0x42BA  Enemy_UpdateOne-style movement body
0x43BA  apply one-pixel movement
0x46D8  chase / BFS override entry
0x477D  BFS preferred[] overwrite
```

Core enemy movement principles:

```text
- fixed tick;
- integer arcade-pixel positions;
- one-pixel movement steps;
- direction decisions at enemy centers only;
- preferred direction is validated before use;
- fallback order is left, up, right, down;
- door-local validation can reject a direction;
- outside center, an enemy normally continues straight;
- door-related forced reversal can happen outside center;
- chase/BFS can overwrite preferred direction but does not bypass validation.
```

## 15. Current limitations

Current known limitations:

```text
- the left-side simulation is not yet the fully autonomous enemy AI from the main LadyBug project;
- some paths still use reference-direction replay;
- enemy release timing is still trace-derived;
- den-exit logic is not fully autonomous;
- chase timers and round-robin behavior are not independently modeled;
- full BFS/chase direction selection is not independently validated;
- multi-enemy behavior is not validated;
- exact-PC logs remain diagnostic aids only;
- normal Compare output is still too verbose.
```

## 16. Next milestones

### v0.9.7 — compact normal logs

Tasks:

```text
- keep the v0.9.6 metrics;
- remove long examples from the normal Compare summary;
- keep only first mismatch if any;
- expose detailed decision-path examples through Dump or a separate diagnostic action if needed.
```

### v0.9.x — connect real Godot enemy logic as candidate

Tasks:

```text
- identify the current enemy AI / movement classes in the main LadyBug project;
- mirror or reference the relevant logic inside EnemyTraceSimulator;
- initialize candidate state from frame 0;
- advance the candidate autonomously;
- compare candidate vs MAME visually;
- stop at first mismatch.
```

### Later

```text
- autonomous den release / slot activation;
- autonomous preferred-direction generation;
- chase activation and timers;
- round-robin selector;
- BFS / chase guidance;
- multi-enemy traces;
- regression trace fixtures.
```

## 17. Documentation rhythm

Update documentation at significant milestones:

```text
- strategy change;
- workflow change;
- new stable validation mode;
- new trace format requirement;
- important reverse-engineering correction;
- removal of obsolete diagnostic paths.
```

Do not update the documentation for every tiny temporary counter or throwaway experiment.

Recommended commit practice:

```text
1. implement milestone;
2. verify compile/run;
3. update README.md and doc/current_implementation.md;
4. commit the code and documentation together.
```

This keeps the repository understandable when coming back to it later.
