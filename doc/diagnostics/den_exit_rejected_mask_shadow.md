# Den-exit rejectedMask shadow holdoff

`v0.6.89` improves the `rejectedMask` / `0x61C1` shadow model for traces that start immediately before the first monster becomes active.

## Problem

A new one-enemy den-exit trace showed that the global comparison remained valid, and the direction shadow still matched, but the `rejectedMask` shadow had three mismatches:

```text
rejectedMask shadow model checks=491
matches=488
mismatches=3
first mismatch: tick=10 ... source=PLAIN_STEP reference=02 shadow=00
```

The first active enemy frame was:

```text
tick=10
enemyDir=08
tempDir=08
rejectedMask=02
preferred=[02,02,02,02]
```

This indicates a den-exit transient: `0x61C1` can still expose `02` while the enemy is already moving upward from the den. This is not a normal decision-center rejection.

## Fix

The shadow model now classifies this explicitly:

```text
DEN_EXIT_INITIAL_REJECTED_02_BRIDGE
DEN_EXIT_REJECTED_02_HOLDOFF
```

The authoritative `RejectedMask` is still reference-synced from MAME. This patch only improves the parallel shadow diagnostic.

## Expected result

On the new `test2` trace, the expected result is:

```text
rejectedMask shadow model mismatches=0
```

The existing enemy direction shadow should remain clean.
