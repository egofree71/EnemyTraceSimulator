# v0.6.93 source-first 0x4315 shadow model

This diagnostic adds a narrow source-first shadow check for the 0x42E6 / 0x4315 path:

```text
preferred candidate rejected
0x61C1 |= preferred
current temp direction is still valid
current direction is kept
no 0x4331
no 0x4241 fallback
```

It deliberately does **not** replace the authoritative rejectedMask state yet.  The comparison adapter still keeps rejectedMask reference-synced from MAME after the shadow check.

## Why this is not a filter

The previous v0.6.92 transition diagnostic proved that the first release activation and the later normal decision-center current-kept case have the same exact-PC shape:

```text
4315_REJECT_OR_CANDIDATE without 4331_REJECT_OR_TEMPDIR / 4241_FALLBACK_ENTRY
```

This package does not say "ignore this mismatch".  It runs the existing `LadyBugEnemyDecisionModel.TryPreferredDirection()` transcription against the inferred start-of-cycle state and compares the resulting rejectedMask and post-step temp state with the JSONL frame.

## Covered cases

Current target trace should report:

```text
releaseChecks=1
normalCurrentKeptChecks=1
checks=2
matches=2
mismatches=0
```

The two covered cases are:

```text
release first cycle:
  startTmp=08:58,86
  preferred=02
  current kept=08
  rejectedMask=02
  final temp=08:58,87

normal decision-center cycle:
  startTmp=01:58,76
  preferred=08
  current kept=01
  rejectedMask=08
  final temp=01:57,76
```

## Important limitation

The local-door reader used by this diagnostic is permissive because these exact-PC current-kept cycles did not hit `4187_LOCAL_DOOR_REJECT`.

This is safe for this narrow check, but it is not a replacement for the real `0x4130` model.  The next broader decision-model work still needs a faithful `0x3C0A` tile lookup over VRAM.
