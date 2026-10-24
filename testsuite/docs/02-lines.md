# 02 — 100 million and 1 billion lines

**Question.** Is the number of lines (not the size) a wall?

## Data (`lines`)

* `lines-<count>.txt`: 8-byte lines (7 random letters + LF). 100M lines = 763 MiB, 1G lines = 7.45 GiB.
* `UWMID01` is the middle line, `UWEND99` is the very last line.

| preset | sizes |
|---|---|
| smoke | 1M |
| trial | 100M |
| full | 100M, 1G |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set lines
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set lines --cache cold,hot --product uvf --product uvp
```

## What is checked

* `lines-*-end`: the last line is found and its line number is exactly the line count.
* `lines-*-mid`: the middle line and its line number.
* `lines-*-nohit`: full scan without output.

## By hand

GUI: open, jump to the last line, scroll; check that the line number shown at the end equals the count.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 2). Tables: `python3 tools/summarize.py ../results/*`.
