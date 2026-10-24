# 03 — Cold vs hot

**Question.** How much faster is the second time, and what does that mean on a VM?

## Data (`huge`)

* Same files as episode 1 (`huge`).

| preset | sizes |
|---|---|
| smoke | 16M, 64M |
| trial | 1G, 10G |
| full | 1G, 10G, 100G |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set huge
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set huge --cache cold,hot --product uvf --product uvp
```

## What is checked

* Run with `--cache cold,hot`. The table shows cold and hot medians side by side for each size.
* macOS: also one cold run after a restart (by hand), because `purge` may keep some pages.
* Linux in VMware: drop the guest cache **and** the host cache (`--cold-hook`), and show both numbers if they differ.

## By hand

GUI: open the same file twice (close in between) and note both times.

## Notes

* Files larger than 60 % of memory: hot is skipped (it would be cold again).

## Results

`../results/<date>_<machine>/summary.csv` (episode = 3). Tables: `python3 tools/summarize.py ../results/*`.
