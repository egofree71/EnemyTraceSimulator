# Den-exit rejectedMask reference bridge

`v0.6.90` adjusts the den-exit `rejectedMask` shadow handling.

## Why

`v0.6.89` added:

```text
DEN_EXIT_INITIAL_REJECTED_02_BRIDGE
DEN_EXIT_REJECTED_02_HOLDOFF
```

The second test trace showed that the fixed holdoff is wrong. MAME can expose:

```text
tick 10: rejectedMask=02
tick 11: rejectedMask=00
```

while the enemy is already moving upward from the den.

So the den-exit `02` duration is not a constant frame holdoff.

## New behavior

`v0.6.90` replaces the fixed holdoff with an explicit measured bridge:

```text
DEN_EXIT_REFERENCE_REJECTED_02_BRIDGE
```

This source only fires when the current reference frame actually exposes:

```text
tempDir = 08
rejectedMask = 02
```

This keeps the standard comparison useful while making the still-unknown den-exit behavior visible in the source summary.

## Important

This is still a bridge, not the final independent model of the den-exit routine.

The goal is to avoid pretending that the holdoff length is known.
