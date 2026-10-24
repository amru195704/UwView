# 05 — Every encoding

**Question.** Is the encoding detected, and is anything lost when it is not?

## Data (`encoding`)

* The same 240 lines of Japanese text in UTF-8 (with and without BOM), UTF-16LE/BE (with and without BOM), UTF-32LE/BE (BOM), Shift_JIS (CP932), EUC-JP and ISO-2022-JP.
* `utf8-broken.txt`: UTF-8 with invalid bytes on some lines (not on the lines with the search word).
* Very short files (`東京` only), a file of half-width katakana, Latin-1.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set encoding
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set encoding --product uvf --product uvp
```

## What is checked

* `東京` must be found on the 60 known lines; `status=200` (ASCII) on 20 lines.
* UTF-32 (LE and BE, with BOM) is not supported: the right answer is an error message and exit code 2 (no hits).
* Cases the manual says are not supported are marked `documented_limit` (UTF-16 without BOM, ISO-2022-JP, Latin-1): a wrong answer there is a known limit, not a new weak point.

## By hand

GUI: open each file and note the encoding shown and whether the text is readable.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 5). Tables: `python3 tools/summarize.py ../results/*`.
