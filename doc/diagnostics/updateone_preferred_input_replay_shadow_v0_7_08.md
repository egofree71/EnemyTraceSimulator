# v0.7.08 — Enemy_UpdateOne preferred-input replay shadow

This diagnostic is a bridge between the validated `preferred[]` generator work and the source-first `Enemy_UpdateOne` shadow.

## Goal

v0.7.07b proved that every active-frame end tuple `0x61C4..0x61C7` in the current static-player trace can be represented by the source-first preferred generator model:

- `0x2E97` rotate branch
- `0x2EC7` random branch
- `0x477D` slot-0 BFS/chase override

v0.7.08 checks the next, narrower question:

> For the enemy slot selected by `Enemy_UpdateOne`, does the replay/classifier provider supply the same `preferred[slot]` value that the current source-first update shadow consumes from the reference trace?

## What is added

New summary block:

```text
Lady Bug Enemy_UpdateOne preferred-input replay shadow v0.7.08
```

Expected result on the current trace:

```text
checks=496
matches=496
mismatches=0
```

This does not change movement or comparison frames. It only validates the preferred-direction input that would be passed into `0x42E0` / `0x4130` decision logic.

## What it still does not prove

This is not yet a 100% autonomous preferred generator. The standard JSONL trace still does not contain an exact-PC tape of `LD A,R` values from `0x2EA5`.

Therefore v0.7.08 still uses the v0.7.07b replay/classifier provider, which classifies the end-of-frame preferred tuple. This is useful because it isolates the bridge and proves that `Enemy_UpdateOne` can receive the same selected preferred input from that provider, but it is not yet the final authoritative random-source replay.

## Next step

The real next milestone is to feed an exact-PC random tape into the standard replay path, so the preferred generator can produce `preferred[]` without classifying the already-computed tuple.
