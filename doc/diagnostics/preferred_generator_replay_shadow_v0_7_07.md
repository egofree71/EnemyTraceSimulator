# Lady Bug preferred[] generator replay shadow v0.7.07b

This checkpoint is deliberately non-authoritative.

v0.7.06 validated the source-first `preferred[]` generator against exact-PC events:

- `0x2E97` rotate branch
- `0x2EA5` real `LD A,R` value
- `0x2EC7` random branch writes
- `0x477D` BFS/chase overwrite

v0.7.07 added a standard-trace shadow summary named:

```text
Lady Bug preferred[] generator replay shadow v0.7.07
```

The first v0.7.07 run showed `tupleMatches=496` and `tupleMismatches=5`. The first mismatch was `tick=0`, before the first active enemy frame (`index=5`, `tick=5`). Those five pre-release frames contain useful RAM state, but they are not generated `Enemy_UpdateOne` preferred tuples for the current single-enemy static-player milestone.

v0.7.07b scopes the tuple classifier to frames where at least one enemy is active. Expected result on the current static-player full-memory trace:

```text
Comparison: comparedFrames=501, mismatches=0
Lady Bug preferred[] generator replay shadow v0.7.07b: ... tupleChecks=496, tupleMatches=496, tupleMismatches=0, skippedNoActiveEnemy=5 ...
```

Important limitation:

The standard JSONL trace still does not contain the exact `0x2EA5` random tape. Therefore v0.7.07b still does **not** make `Enemy_UpdateOne` fully autonomous. It is a bridge checkpoint after the exact-PC validation and before feeding a real preferred-generator replay tape into the source-first `Enemy_UpdateOne` shadow.
