# Player sprite direction mapping

`v0.6.79` fixes the debug-board visual orientation of the player sprite.

Observed player RAM direction encoding at `0x6198`:

```text
01 = left
02 = down
04 = right
08 = up
```

The previous debug renderer used the same vertical sprite frame for `02` and `08`
without vertical mirroring. This made a player moving downward appear to face
upward in the simulator board.

The renderer now treats the player spritesheet as two base orientations:

```text
right-facing base frame
up-facing base frame
```

and mirrors them for the opposite directions:

```text
01 left  -> right base + horizontal flip
02 down  -> up base    + vertical flip
04 right -> right base
08 up    -> up base
```

This is a visual-only correction. It does not change trace parsing, comparison,
enemy simulation, or preferred[] diagnostics.
