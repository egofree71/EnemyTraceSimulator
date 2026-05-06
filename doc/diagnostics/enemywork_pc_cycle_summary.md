# EnemyWork exact-PC cycle summary

`v0.6.82` improves only the analysis report generated from
`ladybug_enemywork_pc_trace.lua`.

No new MAME breakpoints are added.

Why
---

`v0.6.81` proved that the diagnostic now captures the real write/loop PCs around
the EnemyWork scratch fields:

```text
42CC_REJECT_RESET
42CF_FALLBACK_RESET
4315_REJECT_OR_CANDIDATE
4331_REJECT_OR_TEMPDIR
43C4_FALLBACK_STEP_INC
43C5_FALLBACK_STEP_READ
4241_FALLBACK_ENTRY
```

The raw report is useful but too verbose. The next useful view is a grouped
`Enemy_UpdateOne` cycle view.

What the report adds
--------------------

The analyzer now groups events into cycles starting at:

```text
42CC_REJECT_RESET
```

For each cycle, it summarizes:

```text
start temp direction / X / Y
start rejectedMask
start fallback counter
start preferred[]
reject writes at 4315 / 4331
local-door rejects at 4187
fallback entries at 4241
fallback step-read count at 43C5
forced reversals at 4347
next cycle temp/enemy position
```

Expected report sections
------------------------

The generated report should now contain:

```text
Enemy_UpdateOne cycle summary
cycles: ...
cycles with rejectedMask write candidates: ...
cycles with local-door reject breakpoint: ...
cycles entering fallback: ...
cycles with forced reversal: ...

Fallback step-read count by cycle
...

Interesting cycles
...
```

Interpretation
--------------

This is still diagnostic-only. It does not change the simulator behavior.

The goal is to make the next implementation step easier:

```text
preferred[] -> rejectedMask -> fallback counter -> final direction
```

Important terminology note
--------------------------

`61C2` was previously called `fallbackMask` in the simulator. The exact-PC
trace now shows that it behaves like a fallback-step counter/helper:

```text
42CF resets 61C2
43C4 increments 61C2
43C5 reads 61C2
43CA compares it with E
43CB loops back to 42D2 while it has not reached E
```

The C# field can remain named `FallbackMask` temporarily, but the documentation
and diagnostics should treat it cautiously as a fallback helper/counter.
