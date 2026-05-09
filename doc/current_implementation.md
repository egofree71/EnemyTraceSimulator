# Current Implementation

**Project:** Enemy Trace Simulator  
**Current package version:** v0.9.9b  
**Latest validated milestone:** `Source-path single-enemy replay candidate`  
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

v0.8.0 validated a replay bridge. v0.9 changed the direction.

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
- the main success criterion is finding the first useful divergence;
- diagnostics should follow the source path, not ask artificial questions.
```

v0.9.9b is the first checkpoint where the normal candidate replay can compute the active one-enemy movement step from the reconstructed source path.

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
▶|   advance exactly one tick
Dump dump the current frame
Find helper navigation in the trace
Compare open analytical comparison tools
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

`Ctrl+E` remains useful for toggling inactive known enemy slots.

## 6. MAME trace workflow

### 6.1 Standard JSONL trace

Generated by:

```text
tools/mame/lua/ladybug_sequence_trace.lua
```

Recommended settings for one-enemy validation:

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

Longer traces can be used to verify that the replay detects the transition out of the one-enemy scope:

```json
{
  "luaScriptPath": "res://tools/mame/lua/ladybug_sequence_trace.lua",
  "outputPrefix": "ladybug_sequence_v8_fullmem_1200",
  "framesAfterTick0": 1200,
  "includeFullMemoryEachFrame": true,
  "includeLogicalMazeEachFrame": true,
  "enableDebugger": false
}
```

Do not enable the MAME debugger for the standard JSONL trace. Earlier combined attempts could produce an unusable 1-frame capture.

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

The raw-memory blocks are used by source-path, static maze, and tile-level diagnostics.

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

### 8.2 Enemy decision center

Enemy movement decisions occur at enemy decision centers:

```text
x & 0x0F == 0x08
y & 0x0F == 0x06
```

This is not necessarily the same as the player anchor or the generic gameplay anchor.

### 8.3 Enemy direction encoding

Enemy source directions:

```text
01 = left
02 = up
04 = right
08 = down
```

When comparing source enemy directions to Godot `maze.json`, vertical directions must be mirrored:

```text
source 01/left  -> Godot maze left
source 02/up    -> Godot maze down
source 04/right -> Godot maze right
source 08/down  -> Godot maze up
```

This v0.9.6 correction removed false `sourceAcceptedButStaticBlocked` reports.

### 8.4 Player direction / input encoding

Player direction/input uses a different vertical convention:

```text
01 = left
02 = down
04 = right
08 = up
```

Do not mix player and enemy direction encodings.

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

### 9.3 Logical maze and movement validator

The trace can include:

```text
6200..62AF = logical maze / navigation data
```

But the source-path movement validator uses:

```text
0x3911  logical maze validation through ROM table 0x0DA2
0x4130  local door / tile validation
```

Therefore, `0x6200..0x62AF` is useful diagnostic data, but it should not be treated as the complete final movement oracle.

## 10. Source-first enemy movement references

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

The enemy advances by one pixel per update, but it does not choose a new direction at every pixel. Outside decision centers, the source path normally keeps the current direction and only checks the forced-reversal path.

## 11. Compare adapters

Implemented / relevant files:

```text
scripts/tools/simulation/LadyBugEnemySimulationAdapter.cs
scripts/tools/simulation/LadyBugSourcePathSingleEnemyReplayAdapter.cs
scripts/tools/simulation/LadyBugSourcePathDecisionInspector.cs
scripts/tools/simulation/LadyBugEnemyDecisionModel.cs
scripts/tools/simulation/LadyBugEnemyDecisionGate427EModel.cs
scripts/tools/simulation/LadyBugMameLocalTile4130Oracle.cs
scripts/tools/simulation/LadyBugGodotStaticMazeOracle.cs
```

### 11.1 Reference-direction baseline

Compare mode:

```text
Lady Bug reference-direction step
```

This mode keeps visual replay stable by applying the direction observed in the MAME trace, then runs the compact transition-based source-path inspector.

It remains useful as a baseline check for trace loading, visual comparison, and source-path explanation.

### 11.2 Source-path single-enemy replay

Compare mode:

```text
Lady Bug source-path single-enemy replay
```

This is the v0.9.9b milestone adapter.

For one-active-enemy transitions, it computes the visible enemy movement step through the reconstructed source path rather than using the reference direction:

```text
0x427E decision gate
  carry set:
    preferred 0x42E6
    current   0x4325 if preferred rejected
    fallback  0x4241 if current rejected
  carry clear:
    outside-center keep / forced reversal 0x433A
then:
  0x43BA one-pixel movement step
```

Still synchronized from the trace in v0.9.9b:

```text
release timing
preferred[]
player
gates
timers
VRAM context
inactive slots
multi-enemy frames after scope limit
```

The adapter reports:

```text
modeledSingleEnemyTransitions
modeledSlotMismatches
modeledEnemyWorkMismatches
modeledClean
fullScopeClean
firstModeledProblem
firstMultiEnemySync
```

### 11.3 v0.9.9b rejected-mask correction

The first v0.9.9 replay exposed a single `EnemyWork` mismatch in the fallback path:

```text
model=rej:07
reference=rej:03
```

The corrected rule in v0.9.9b is:

```text
preferred rejected -> 0x61C1 |= preferred
current rejected   -> 0x61C1 |= current
fallback scan rejected directions are not added to 0x61C1
```

After this correction:

```text
modeledEnemyWorkMismatches=0
```

## 12. Source-path decision inspector

The inspector remains useful for explaining transitions and validating the source path.

### 12.1 Transition-based timing

The inspector uses:

```text
source input     = frame[i-1] enemy slot
reference result = frame[i] enemy slot and EnemyWork
```

This fixed a false-positive class where the already displayed frame was treated as if it were the source routine input.

### 12.2 Source path followed

The inspector follows only source-tested directions:

```text
0x427E decision gate
  carry set:
    preferred 0x42E6
    current   0x4325 if preferred rejected
    fallback  0x4241 if current rejected
  carry clear:
    outside-center keep / forced reversal 0x433A
then:
  0x43BA one-pixel movement step
```

The all-four-directions collision report is disabled in the normal Compare output because it can create false positives by testing directions the source never tried.

### 12.3 Multi-enemy-safe mode

v0.9.8 does not implement multi-enemy source-path analysis. It only prevents misleading reports.

Transition classification:

```text
activeStart=0 -> skippedNoActiveStartEnemyTransitions
activeStart=1 and activeResult=1 -> inspect or replay normally
activeStart>1 or activeResult>1 -> skipped / synced multi-enemy transition
```

For the 1201-frame test trace under v0.9.9b:

```text
modeledSingleEnemyTransitions=827
multiEnemySyncTransitions=363
modeledClean=true
fullScopeClean=false
firstMultiEnemySync startTick=837 resultTick=838 activeStart=1 activeResult=2
```

Interpretation:

```text
- one-enemy section is clean;
- the trace later leaves the supported scope;
- multi-enemy transitions are intentionally resynchronized;
- fullScopeClean=false only because unsupported multi-enemy transitions occurred.
```

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

## 14. Current validation status

Validated on two different 600-tick static-player traces:

```text
visual replay comparison: mismatches=0
modeledSlotMismatches=0
modeledEnemyWorkMismatches=0
modeledClean=true
fullScopeClean=true
firstModeledProblem: none
```

Validated on a 1200-tick trace that reaches two active enemies:

```text
visual replay comparison: mismatches=0
modeledSingleEnemyTransitions=827
multiEnemySyncTransitions=363
modeledSlotMismatches=0
modeledEnemyWorkMismatches=0
modeledClean=true
fullScopeClean=false
firstMultiEnemySync startTick=837 resultTick=838 activeStart=1 activeResult=2
```

This proves the current one-enemy replay remains clean and that the tool now reports unsupported multi-enemy transitions instead of pretending to model them.

## 15. Current limitations

Current known limitations:

```text
- release timing is still trace-derived;
- preferred[] is still trace-synced;
- player state is still trace-synced;
- gates, timers, VRAM context, inactive slots, and multi-enemy frames are still trace-synced;
- den-exit logic is not autonomous;
- true multi-enemy replay is not implemented;
- moving-player traces are not implemented yet;
- pivoting-door interaction as a gameplay input is not yet part of the validation workflow;
- chase timers and round-robin behavior are not independently modeled;
- full BFS/chase direction selection is not independently validated;
- exact-PC logs remain diagnostic aids only.
```

## 16. Next milestones

### Priority: validate one-enemy movement with static player

This remains the immediate priority, but v0.9.9b is a major step because the active one-enemy movement step is now modeled through the source path.

Tasks:

```text
- keep the clean two-board visual replay as the main workflow;
- validate source-path single-enemy replay on several static-player traces;
- reduce trace-synced inputs one by one;
- stop at the first visible mismatch;
- use the source-path inspector to explain that mismatch.
```

### Candidate next steps

Likely next dependencies to remove or reduce:

```text
1. preferred[] trace-sync;
2. release timing / den-exit trace-sync;
3. player movement traces;
4. explicit pivoting-door interaction traces;
5. true multi-enemy source-path replay later.
```

Multi-enemy support is intentionally not the next priority. v0.9.8/v0.9.9b only make the current code safe when a trace leaves the one-enemy scope.

## 17. Documentation rhythm

Update documentation at significant milestones:

```text
- strategy change;
- workflow change;
- new stable validation mode;
- new trace format requirement;
- important reverse-engineering correction;
- removal of obsolete diagnostic paths;
- validation scope changes such as multi-enemy-safe skipping;
- replay scope changes such as v0.9.9b source-path single-enemy replay.
```

Do not update the documentation for every tiny temporary counter or throwaway experiment.

Recommended commit practice:

```text
1. implement milestone;
2. verify compile/run;
3. update README.md and doc/current_implementation.md;
4. commit the code and documentation together.
```
