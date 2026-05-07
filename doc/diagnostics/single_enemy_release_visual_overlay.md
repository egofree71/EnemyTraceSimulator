# v0.7.04 - Single-enemy release visual overlay

This diagnostic package adds a small visual overlay to `EnemyTraceBoardView`.

It does not change the simulation or comparison logic.  It only renders extra information while replaying the existing MAME trace.

## Scope

Current milestone scope:

- static player sequence
- slot 0 / first released enemy focus
- single-enemy visual replay aid
- no multi-enemy attribution yet
- no claim that forced-reversal carry-set is fully validated

## What is rendered

The board now shows:

- a `D` marker at the known den release coordinate used by the current trace (`58,86` in MAME coordinates)
- an `E0` highlight around enemy slot 0 when it is active
- a direction arrow for the focused enemy
- a short recent trail while the replay advances
- a compact HUD with tick, mameFrame, slot 0 raw/dir/x/y and EnemyWork temp/rejected/fallback/preferred values

The trail is intentionally local to the playback session.  It is reset when the replay jumps backwards or skips too far.

## Why this is useful

v0.7.01 and v0.7.03 validated the source-first shadow on the static-player trace, but the feedback was still mostly textual.  This overlay makes the first enemy release and movement easier to inspect visually without turning the debug board into a full game renderer.

## Current limitations

The overlay is diagnostic-only:

- it follows slot 0 only
- it does not resolve multi-enemy EnemyWork attribution
- the den marker is fixed for the current release sequence
- forced reversal carry-set / 0x4347 is not validated by this visual layer
- the authoritative comparison still remains the text comparison output
