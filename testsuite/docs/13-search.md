# 13 — Does search speed depend on the number of hits?

**Question.** The same file searched for 0, 3, thousands, millions of hits, fixed strings, regular expressions and `-i`.

## Data (`search`)

* `search-<size>.log`: log text. The hit count of every pattern is computed while the file is generated.

| preset | sizes |
|---|---|
| smoke | 64M |
| trial | 1G |
| full | 1G, 10G |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set search
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set search --cache cold,hot --product uvf --product uvp
```

## What is checked

* Patterns: `UWTEST-NOHIT` (0), `UWTEST-NEEDLE` (3), `status=503`, `東京` (~1/12 of the lines), `INFO` (~70 %), `-E status=5\d\d`, `-E id=12345\d\b`, `-i error`.
* All result lines are written to a file, so the time includes output.

## By hand

GUI: the same searches on screen; note the time until the hit count is final.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 13). Tables: `python3 tools/summarize.py ../results/*`.
