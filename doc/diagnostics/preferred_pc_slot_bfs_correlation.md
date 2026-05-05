# preferred[] slot/BFS correlation check

`v0.6.68` adds a small but useful consistency check to the preferred[] exact-PC analyzer.

In a one-enemy diagnostic capture, the base generator writes four slots per generation:

- `2EC7_RANDOM_WRITE` writes `61C4..61C7`;
- `2E97_ROTATE_WRITE` writes `61C4..61C7`.

The chase/BFS override at `477D_BFS_WRITE` can then overwrite one preferred slot.

For the current one-enemy captures, `IY=61C4`, so the override targets:

```text
preferred[0] / 0x61C4
```

The report now explicitly checks:

```text
slot0 extra over slot1 == 477D_BFS_WRITE hits
slot0 extra over base-per-slot == 477D_BFS_WRITE hits
```

When both match, the report prints:

```text
conclusion: slot0 excess matches BFS/chase overrides exactly.
```

This makes the analysis self-explanatory before implementing the C# `MonsterPreferenceSystem`.
