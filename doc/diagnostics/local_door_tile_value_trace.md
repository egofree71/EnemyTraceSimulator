# Lady Bug enemy local-door tile value diagnostic — v0.6.96

This diagnostic extends the existing EnemyWork exact-PC MAME trace.

It does **not** implement or replace the simulator's local-door validation yet. Its purpose is to observe the real Z80 tile values used by the `0x4130` local-door validator.

## Why this was needed

v0.6.94/v0.6.95 validated the address computation performed by `0x3C0A`:

```text
actualHL = 0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
```

That confirms the important Lady Bug VRAM layout rule:

```text
VRAM is column-major, bottom-to-top.
D chooses the screen column group.
E >> 3 chooses the tile within that column.
```

However, `0x3C0A` only returns the tile address. It restores `AF/BC` before returning, so the `A` register at `0x3C2B` is **not** the tile value.

The tile is loaded by the caller, `0x4130`, via `LD A,(HL)` at four branch-specific addresses.

## New exact-PC breakpoints

The Lua script now stops immediately after each `LD A,(HL)` by breaking at the following `CP` instruction. That means `A` contains the true tile value.

```text
0x4144 -> after 0x4143 LD A,(HL), down branch
0x4157 -> after 0x4156 LD A,(HL), left branch
0x416A -> after 0x4169 LD A,(HL), up branch
0x417D -> after 0x417C LD A,(HL), right branch
0x4185 -> local-door accept return path
0x4187 -> local-door reject return path
```

## Branch model captured by the analyzer

```text
down  dir=08 probe E+02 rejects tiles 35 / 37
left  dir=01 probe D-01 rejects tiles 3D / 3F
up    dir=02 probe E-07 rejects tiles 35 / 37
right dir=04 probe D+08 rejects tiles 3F / 3D
```

The order of the reject tile values is kept close to the source, but logically `3D/3F` and `3F/3D` are the same set for left/right.

## Logical maze cell vs tile probe

A logical maze cell appears to span 2 tiles horizontally and 2 tiles vertically, so roughly 16x16 pixels.

The local-door routine does not validate a whole logical cell directly. It probes one concrete 8x8 tile around the current position, depending on the tested direction.

So there are two levels that must not be confused:

```text
logical maze cell : 16x16, used by the maze graph / 0x6200 map
VRAM tile probe   :  8x8, used by 0x3C0A and local-door geometry checks
```

## Expected report sections

After running MAME/Lua with `ladybug_enemywork_pc_trace.lua`, the generated analysis should contain:

```text
0x4130 local-door tile-value summary
```

Look for:

```text
tileReadEvents=...
acceptsObserved=...
rejectsObserved=...
expectedAcceptsFromTileValue=...
expectedRejectsFromTileValue=...
addressMismatches=0
branches:
  down/dir=08: ...
  left/dir=01: ...
  up/dir=02: ...
  right/dir=04: ...
tiles by branch:
  ...
```

`addressMismatches=0` means the caller-side tile reads still agree with the source `0x3C0A` address formula.

The accept/reject expectation is only a diagnostic comparison based on the source constants. It should not be used yet as a gameplay replacement until we verify the full sequence inside `0x42E6`, `0x3911`, `0x4130`, and `0x4241`.

## Methodological note

This package avoids adding a special gameplay case. It only observes what the original Z80 code reads and which branch accepts or rejects.

The simulator remains reference-synced for the authoritative enemy movement and rejectedMask until the complete source-first validator is implemented.
