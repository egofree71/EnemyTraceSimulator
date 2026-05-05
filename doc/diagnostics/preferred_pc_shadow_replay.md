# preferred[] exact-PC shadow replay

`v0.6.71` adds a shadow replay check to the preferred[] exact-PC analyzer.

The analyzer now replays the complete `LBPREF` stream with the C# model:

- `2EC7_RANDOM_WRITE` groups are generated with `LadyBugMonsterPreferenceSystem.GenerateRandomBranchFromUsedRLow`.
- `2E97_ROTATE_WRITE` groups are generated with `LadyBugMonsterPreferenceSystem.GenerateRotateBranch(08)`.
- `477D_BFS_WRITE` hits are applied as observed one-slot overrides because full BFS pathfinding is not implemented yet.

The important new report section is:

```text
preferred[] exact-PC shadow replay check
  pre-write state matches p0..p3: ...
  modeled base write values match observed A: ...
  BFS overrides applied from observed 477D hits: ...
  final shadow preferred[] state: ...
```

Purpose:

- validate that the model is not only matching isolated tuples;
- validate that applying modeled base writes plus observed BFS overrides keeps the reconstructed preferred[] state aligned with the arcade snapshots;
- prepare the next step: wiring the model into `LadyBugEnemySimulationAdapter` in diagnostic/fallback mode.

This still does not replace the standard JSONL comparison pipeline.
