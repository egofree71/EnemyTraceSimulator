# v0.6.95 — 0x3C0A tile lookup analyzer

This checkpoint keeps the v0.6.94 exact-PC breakpoint at `0x3C2B`, but makes the C# analyzer understand those events.

## Source routine

`0x3C0A..0x3C2B` does not read a tile. It computes an address in video RAM and returns with `HL` pointing to the probed tile. The caller then performs `LD A,(HL)`.

The address formula is:

```text
actualHL = 0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
```

This matches the Lady Bug VRAM layout used in the project: screen tiles are arranged by columns, bottom-to-top, not by screen rows.

## Important debugger caveat

The Lua breakpoint action logs `h` and `l` separately, and also logs a convenience expression named `hl`.

The exact-PC logs showed that the composite `hl` expression is unreliable in this MAME debugger context. The analyzer therefore ignores the composite `hl` field and reconstructs the real address as:

```text
actualHL = (H << 8) | L
```

## What the analyzer now reports

The report includes a new section:

```text
0x3C0A tile-address lookup summary
```

It prints:

```text
events
comparable
matches
mismatches
missingRegisters
uniqueActualAddresses
uniqueProbeCells
debuggerCompositeHlMatchesActual
debuggerCompositeHlDiffersFromActual
sample computed lookups
```

A good run should show:

```text
mismatches=0
```

## What this still does not do

This checkpoint validates the `0x3C0A` address calculation only.

It does not yet validate the tile value used by `0x4130`, because `0x3C0A` restores `AF` before returning. The real tile is loaded by the caller after return, for example:

```text
0x4143  LD A,(HL)   down probe
0x4156  LD A,(HL)   left probe
0x4169  LD A,(HL)   up probe
0x417C  LD A,(HL)   right probe
```

The next source-first step is to add exact-PC breakpoints at those caller-side tile load / compare points, then wire `0x4130` to a real VRAM-backed tile reader.
