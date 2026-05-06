# Enemy direction shadow model

`v0.6.86` adds a diagnostic-only shadow model for the final enemy direction.

The adapter still moves enemies using the MAME reference direction. This patch
only compares a locally classified direction with the reference `EnemyWork.tempDir`.

## New fields

```text
EnemyDirectionShadow
EnemyDirectionShadowSource
```

## Covered cases

The model intentionally starts narrow:

```text
PLAIN_STEP_KEEP_CURRENT
DECISION_CENTER_USE_PREFERRED0
DECISION_CENTER_KEEP_CURRENT_AFTER_REJECT
DECISION_CENTER_KEEP_CURRENT_REVERSE_IGNORED
DEN_EXIT_FORCED_UP_BRIDGE
FALLBACK_REFERENCE_DIRECTION_BRIDGE
DECISION_CENTER_REFERENCE_DIRECTION_BRIDGE
INITIAL_REFERENCE_DIRECTION_BRIDGE
```

## Important limitation

`FALLBACK_REFERENCE_DIRECTION_BRIDGE` is not a real fallback direction search.
It is an explicit bridge marking the exact place where the real fallback direction
selection still has to be implemented.

The goal of this patch is to validate how often the current decision-layer model
already explains the final direction and to expose the remaining fallback cases
cleanly in the Compare summary.
