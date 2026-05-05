# Preferred[] rotate classifier for all directions

`v0.6.78` generalizes the adapter-level preferred[] shadow classifier.

The first validated traces only needed:

```text
2E97_ROTATE_FROM_08 -> [04,02,01,08]
```

A later trace starts while the player is facing `04` and shows a tuple like:

```text
[02,01,08,04]
```

This is the same `2E97` rotation branch, but seeded from `04`:

```text
2E97_ROTATE_FROM_04 -> [02,01,08,04]
```

The adapter shadow classifier now recognizes all four rotate seeds:

```text
2E97_ROTATE_FROM_01 -> [08,04,02,01]
2E97_ROTATE_FROM_02 -> [01,08,04,02]
2E97_ROTATE_FROM_04 -> [02,01,08,04]
2E97_ROTATE_FROM_08 -> [04,02,01,08]
```

It also supports the existing observed slot-0 BFS overlay on top of any of those rotate tuples.

This is still a classifier for standard JSONL end-of-frame tuples. It does not yet replace the authoritative MAME-synced `preferred[]`, and it does not yet decide the exact branch PC independently.
