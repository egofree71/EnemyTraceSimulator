# preferred[] exact-PC error.log analyzer

This document is a short smoke-test note for `LadyBugPreferredPcLogAnalyzer`.

The analyzer is intentionally separate from the standard JSONL frame trace:

- JSONL remains the normal comparison format between MAME and Godot/C#.
- `tools/mame/lua/error.log` is an exact-PC reverse-engineering diagnostic output for `EnemyWork.preferred[]`.

Expected input lines contain `LBPREF`, for example:

```text
LBPREF|source=2EC7_RANDOM_WRITE|pc=2EC7|r=51|a=02|...
```

The analyzer reports:

- total `LBPREF` hits;
- counts by write source;
- inferred slot counts;
- complete 4-write tuples from `2EC7_RANDOM_WRITE` and `2E97_ROTATE_WRITE`;
- top tuples;
- reconstructed low nibble of the `R` value used by `LD A,R`;
- BFS/chase override summary for `477D_BFS_WRITE`.

`v0.6.65` fixes a nullable warning in the parser by replacing a nullable-return parse helper with `TryParseHit(...)`.
