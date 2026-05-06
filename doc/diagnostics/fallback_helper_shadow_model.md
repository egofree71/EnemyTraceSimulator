# Fallback helper shadow model

`v0.6.85` adds an adapter-level shadow diagnostic for `0x61C2`.

The C# DTO still has the legacy field name:

```text
FallbackMask
```

but the exact-PC EnemyWork diagnostic indicates that `0x61C2` behaves as a fallback step counter/helper, not as a normal direction mask.

## New diagnostic fields

```text
FallbackHelperShadow
FallbackHelperShadowSource
```

The authoritative comparison value is still `FallbackMask`, synced from MAME.
The new shadow fields only validate the current reconstruction in parallel.

## Current narrow model

The validated exact-PC trace showed this regular pattern per `Enemy_UpdateOne` cycle:

```text
42CF_FALLBACK_RESET
43C4_FALLBACK_STEP_INC
43C5_FALLBACK_STEP_READ
```

with one fallback-step read per cycle.

Therefore the first shadow model expects the end-of-cycle standard JSONL value:

```text
0x61C2 = 01
```

and classifies it as:

```text
ONE_STEP_PER_ENEMY_UPDATE
```

This is intentionally narrow. Future traces with additional fallback-step reads should produce shadow mismatches, which will tell us what case to model next.
