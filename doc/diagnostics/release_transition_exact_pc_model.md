# Release transition exact-PC model — v0.6.92

This package updates the standard transition diagnostic only. It does not change
movement, reference-sync bridges, or the authoritative comparison output.

## Why this exists

The v0.6.91 exact-PC report showed that the first active enemy cycle is not a
normal previous-frame-to-current-frame transition.

The standard JSONL transition had previously used:

```text
previousFrame.enemyWork = 02:78,8E
currentFrame.enemyWork  = 08:58,87
```

and classified the transition as `PLAIN_STEP_OUTSIDE_CENTER`.

The exact-PC cycle instead showed the real cycle start:

```text
cycle=0
startTmp=08:58,86
startPref=[02,02,02,02]
rejectWrites=4315_REJECT_OR_CANDIDATE@4315:A=02:preC1=00
fallbackEntries=none
nextTmp=08:58,87
```

So the first activation is the same source pattern as the later current-kept case:

```text
preferred rejected at 0x4315
current direction kept
no 0x4331
no 0x4241 fallback
```

## Expected result

The transition diagnostic should now report:

```text
releaseActivationTransitions=1
releaseActivationModeledFromExactPc=1
preferredRejectedCurrentKept=2
rejectedMaskDiffersFromModeled=0
sources: ... 3061_4315_RELEASE_PREFERRED_REJECTED_CURRENT_KEPT=1 ...
```

The normal comparison should remain:

```text
mismatches=0
```

## Important limitation

This is still diagnostic-only. It does not yet simulate enemy release from the den.
It only fixes the transition diagnostic so it uses the source/exact-PC cycle start
instead of stale previous-frame EnemyWork for the first activation.
