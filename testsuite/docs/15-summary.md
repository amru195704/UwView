# 15 — Summary: Web edition, uvf or uvp?

**Question.** From all the results: which product fits which job?

## How the tables are made

```sh
python3 tools/summarize.py results/<date>_<mac> results/<date>_<linux> results/<date>_<windows> > tables.md
python3 tools/summarize.py --failures results/*      # wrong answers, input for ../weak-points.md
```

* One table per episode, one row per machine × product × case × cache: median seconds, MB/s, runs,
  answer (`ok` / `ng` / `obs` / `skip` / `missing`) and hits.
* uvf and uvp are compared **only on the same machine and cache**; within 1.5× is written "same".
* Seconds of different machines are never put in one comparison.

## What goes into the summary post

* For each episode, the limit found (size, line count, encoding, format) for the Web edition, uvf and uvp.
* The list of weak points with the version they were found in, and what changed in the next version.
