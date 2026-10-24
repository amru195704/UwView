# 01 — Opening 1 GiB to 100 GiB

**Question.** How do time and memory grow when the file gets 10× bigger?

## Data (`huge`)

* `huge-<size>.log`: log-like UTF-8 lines (date, level, city name, id, status, path), made of 1 MiB blocks.
* Three needle lines `UWTEST-NEEDLE` are placed in the first, the middle and the last 1 MiB block; their line numbers are known.

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

* `huge-*-nohit`: a word that never appears. Exit code 1, no output: this is the time to read the whole file.
* `huge-*-needle`: exactly 3 result lines with the right line numbers.

## By hand

GUI: open each file and wait until the line count is final; then jump to the last line (see [gui.md](gui.md)).

## Notes

* 100 GiB needs about 100 GiB of free space for the data alone. Delete it after the run.
* On a machine with less memory than the file, only cold is measured.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 1). Tables: `python3 tools/summarize.py ../results/*`.
