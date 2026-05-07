# Lady Bug enemy preferred[] generator exact-PC diagnostic — v0.7.06

This diagnostic validates the preferred-direction generator around `0x2E5C` from exact-PC MAME logs.

## Scope

This is still a diagnostic step. It does not change the main simulation adapter and does not remove any MAME-synced fields from the authoritative comparison yet.

The purpose of v0.7.06 is to prove that the C# model can reproduce the observed `preferred[]` writes for one static-player, mostly single-enemy release sequence.

## Source regions observed

- `0x2E5C`: preferred[] generator entry
- `0x2E8C / 0x2E97`: rotate branch and preferred write
- `0x2E9E / 0x2EA3 / 0x2EA5 / 0x2EC7`: random branch, `LD A,R` capture, and preferred write
- `0x2ECB / 0x46D8 / 0x477D`: chase/BFS override path

## v0.7.06 model checks

The analyzer now groups events by each `0x2E5C` call and validates:

1. Rotate branch writes:
   - seed comes from `A` at `0x2E8C`
   - expected tuple is produced by rotating the direction bit right four times
   - expected writes are compared with the four `0x2E97` writes

2. Random branch writes:
   - each true `LD A,R` value is captured at `0x2EA5`
   - expected direction is computed from the low nibble exactly like the source branch
   - expected writes are compared with the four `0x2EC7` writes

3. BFS/chase override:
   - each `0x477D` write is checked for a valid `IY` target in `0x61C4..0x61C7`
   - the override is applied to the modeled base tuple
   - the resulting tuple is compared against the final observed preferred tuple before the next generator call

## Expected result for the current sequence

The current static-player sequence should show non-zero events for:

- `entries2E5C`
- `rotateWrites2E97`
- `randomRValues2EA5`
- `randomWrites2EC7`
- `bfsEntries46D8`
- `bfsWrites477D`

The new section should contain:

```text
preferred[] generator C# model validation
-----------------------------------------
...
baseWriteMismatches=0
randomRWritePairMismatches=0
bfsOverrideInvalidTargets=0
finalTupleMismatches=0
first mismatch: none
```

If those counters are zero, the exact-PC preferred generator model is validated for this trace.

## Important limitation

This does not yet make the main simulation fully autonomous. The exact-PC diagnostic can read the real `R` value at `0x2EA5`; a true replay/simulation must either:

- emulate the Z80 `R` register evolution precisely enough, or
- treat the `R` stream as an explicit replay input/seed.

Until that decision is made, the main adapter should not pretend to be fully independent from MAME for the random branch.
