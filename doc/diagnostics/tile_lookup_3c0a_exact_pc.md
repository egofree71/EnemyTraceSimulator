# 0x3C0A tile lookup exact-PC diagnostic

Package: v0.6.94

## Purpose

This diagnostic adds an exact-PC breakpoint at `0x3C2B`, the return point of the arcade routine usually labelled `GetTileUnderPlayerProbe` / `0x3C0A`.

The goal is to validate the source-level tile-address formula before using it inside the C# implementation of:

```text
0x4130  Enemy_CheckLocalDoorBlock
0x4189  door-local forced reversal probe
```

This package does **not** replace the local-door validator yet.

## Source formula

The source routine computes the VRAM address like this:

```text
D = probe X
E = probe Y

A = D & F8
BC = A << 2
HL = D0A0 + BC
A = E >> 3
HL = HL + A
RET
```

So the C# equivalent should be:

```text
address = 0xD0A0 + ((x & 0xF8) * 4) + (y >> 3)
```

This formula matches the Lady Bug VRAM layout used by the arcade driver / reverse engineering notes: tiles are stored by columns, bottom-to-top, not by row-major screen order.

## New exact-PC source

The Lua script now logs:

```text
3C2B_TILE_LOOKUP_RETURN
```

At this point:

```text
D/E = probe coordinate used by 0x3C0A
HL  = computed VRAM tile address
```

The existing analyzer will at least show this source in:

```text
Hits by source
First events
```

and the raw `error.log` will contain all exact-PC lines.

## Expected validation

For each `3C2B_TILE_LOOKUP_RETURN` line:

```text
reported HL == 0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
```

Examples of important cases to inspect first:

```text
4130_LOCAL_DOOR_CHECK_ENTRY
3C2B_TILE_LOOKUP_RETURN
4187_LOCAL_DOOR_REJECT
```

For a local-door rejection, the `4187` line contains the rejected tile value in register `A`, while `HL` should still point at the VRAM address returned by `0x3C0A`.

## Important limitation

This is still exact-PC observation only. The standard JSONL trace currently reports:

```text
vram=0 bytes
```

so the standard comparison cannot yet independently read the tile value from frame VRAM. This diagnostic establishes the address formula first. A later package should either:

1. export the relevant VRAM range in JSONL, or
2. reuse the existing gate/tile extraction path used for pivot-door rendering, if it already reconstructs the needed tile map.
