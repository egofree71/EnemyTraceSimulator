# Enemy release / den-exit source model diagnostic

Version: v0.6.90

This diagnostic is intentionally **not** a mismatch filter.

It records the source-level release expectations from the Z80 code and compares
those expectations with the first active-enemy transition visible in the standard
JSONL trace.

## Source blocks

### 0x05AE..0x05D0 — enemy slot allocation / release helper

The code scans enemy slots at:

```text
0x602B + slot * 5
```

It tests the low two bits of the slot raw byte:

```text
A = (slotRaw & 0x03)
```

If the result is zero, it calls `0x3061` with `C = slotIndex`.

### 0x3061..0x3086 — released enemy slot initialization

`0x3061` computes:

```text
IX = 0x602B + C * 5
```

Then initializes the slot:

```text
(IX+0) = 0x82
(IX+1) = 0x58
(IX+2) = 0x86
(IX+3) = sprite byte from 0x3087
(IX+4) = attribute byte from 0x3087
```

The current standard JSONL trace sees the first active enemy at:

```text
raw = 0x82
x   = 0x58
y   = 0x87
```

That is consistent with source initialization at `0x58,0x86` followed by one
movement/update step before the frame boundary captures the state.

## What this patch does

Adds:

```text
scripts/tools/simulation/LadyBugEnemyReleaseModel.cs
```

and appends a source-release section to the existing decision diagnostic summary:

```text
Lady Bug enemy release diagnostics v0.6.90: ...
```

The diagnostic counts activation transitions and reports whether the first active
enemy matches the `0x3061` release shape.

## What this patch does not do

It does not:

- change movement;
- change rejectedMask modeling;
- change comparison frames;
- suppress or reclassify the existing `tick=5` rejectedMask mismatch;
- claim that den-exit is fully simulated.

The next implementation step should simulate the release path explicitly, then
verify whether the first update naturally produces the observed EnemyWork state.
