# MonsterPreferenceSystem checkpoint

`v0.6.70` introduces the first C# model of the arcade preferred-direction generator.

The new class is:

```text
scripts/tools/simulation/LadyBugMonsterPreferenceSystem.cs
```

It is intentionally side-effect free and is not yet wired into `LadyBugEnemySimulationAdapter`.

Current covered pieces:

- `2E97_ROTATE_WRITE`
  - rotate right over the four direction bits;
  - starting from `PLAYER_DIR_CURRENT=08`, predicts `[04,02,01,08]`.

- `2EC7_RANDOM_WRITE`
  - starts from the reconstructed internal `R` low nibble used by `LD A,R`;
  - maps the nibble through the observed arcade direction mapping;
  - advances the internal low nibble with direction-dependent deltas:
    - after `01`: `+D`
    - after `02`: `+F`
    - after `04`: `+0`
    - after `08`: `+1`

- `477D_BFS_WRITE`
  - provides a helper to apply a one-slot override when `IY` points into `61C4..61C7`.

The exact-PC analyzer now performs a C# model check:

```text
preferred[] exact-PC C# model check
  2EC7 random model matches: ...
  2E97 rotate model from PLAYER_DIR_CURRENT=08 matches: ...
```

Expected current result for the known one-enemy diagnostic capture:

```text
2EC7 random model matches: 409/409
2E97 rotate model from PLAYER_DIR_CURRENT=08 matches: 265/265
```

This is still a diagnostic validation step. The next patch can start wiring this model into the simulation adapter while keeping the MAME reference-sync available as a fallback.
