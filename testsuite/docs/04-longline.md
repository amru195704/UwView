# 04 — One huge line

**Question.** What happens when a file has no newline at all (minified JSON), or one line of 100 MiB among normal lines?

## Data (`longline`)

* `oneline-<size>.json`: one line, no newline at the end, with `UWTEST-NEEDLE` at the very end.
* `longmix-<size>.log`: 1,000 log lines; line 500 is one long JSON line of the given size; line 900 has the needle.

| preset | sizes |
|---|---|
| smoke | 1M, 8M |
| trial | 10M, 100M |
| full | 1M, 10M, 100M, 1G |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set longline
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set longline --cache cold,hot --product uvf --product uvp
```

## What is checked

* `oneline-*-needle`: one result line, line number 1 (the result line is the whole file, so the time includes writing it).
* `oneline-*-nohit`: full scan.
* `longmix-*-needle`: line number 900 must stay right after the long line.

## By hand

GUI: open, move to the end of line 1, search the needle; note whether the window stays responsive.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 4). Tables: `python3 tools/summarize.py ../results/*`.
