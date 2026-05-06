# EnemyWork exact write-PC diagnostics

`v0.6.81` refines the EnemyWork exact-PC diagnostic introduced in `v0.6.80`.

The first report proved that the diagnostic sees logical validation, local-door rejection, and fallback events, but it also showed two limitations:

- the breakpoint action stored `activeEnemies=0` because that value was computed when breakpoints were installed, not when they were hit;
- `fallbackMask` stayed `00` in the report because the diagnostic only sampled around fallback entry, not the exact `61C2` increment/read path.

This patch adds exact breakpoints around the known RAM writes:

```text
42CC = rejectedMask reset write to 61C1
42CF = fallbackMask reset write to 61C2
4315 = rejectedMask |= rejected candidate
4331 = rejectedMask |= current temp direction before fallback
43C4 = fallbackMask / fallback step counter increment
43C5 = fallbackMask value after increment
```

The analyzer now derives active enemy count from the four raw enemy slot bytes instead of trusting a debugger-action helper value.

This diagnostic is still observational. It does not implement the rejected/fallback generator yet.
