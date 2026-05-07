# v0.7.05b — preferred[] generator exact-PC breakpoint fix

This package fixes the v0.7.05 diagnostic package.

The v0.7.05 Lua file documented the preferred[] generator breakpoints and the C# analyzer expected them, but `install_breakpoints()` did not actually install the 0x2E5C / 0x2E97 / 0x2EA5 / 0x2EC7 / 0x46D8 / 0x477D breakpoints.

v0.7.05b adds the missing `set_breakpoint()` calls for:

- `0x2E5C` — preferred generator entry
- `0x2E6A` — timer threshold compare context
- `0x2E84` — input read result
- `0x2E8C` — rotate branch selected
- `0x2E91` — rotate loop
- `0x2E97` — rotate branch write to 0x61C4..0x61C7
- `0x2E9E` — random branch selected
- `0x2EA3` — random loop before `LD A,R`
- `0x2EA5` — true `LD A,R` value in A, before `AND 0F`
- `0x2EC7` — random branch write to 0x61C4..0x61C7
- `0x2ECB` — call to BFS/chase override
- `0x46D8` — BFS/chase override entry
- `0x477D` — BFS/chase preferred[] overwrite

Use the same exact-PC workflow:

```text
Lua script path:
res://tools/mame/lua/ladybug_enemywork_pc_trace.lua

Trace output prefix:
ladybug_sequence_v8_enemywork_pcdiag

Frames after tick 0:
600
```

Expected analyzer section:

```text
preferred[] generator exact-PC summary
--------------------------------------
events > 0
```

This is diagnostic-only. It does not change the simulator movement model.
