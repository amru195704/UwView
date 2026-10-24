# 08 — Japanese 2: searching for 表 (the 0x5C problem)

**Question.** In Shift_JIS the second byte of 表, ソ, 能, 十, 予, ポ … equals `\`, `|`, `[`, `]`, `{`, `}` or `` ` ``. Does a search for 表 find only 表, and does a search for `\` avoid false hits?

## Data (`ja-dame`)

* `cp932-dame.txt` / `utf8-dame.txt`: words with such characters, and lines with the real ASCII symbols. `dame-list.tsv` shows the bytes.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set ja-dame
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set ja-dame --product uvf --product uvp
```

## What is checked

* Fixed strings `表`, `ソ`, `能力`, `\`, `|`, `[`.
* Regular expressions `表|ソ`, `[表ソ能]`, `能.`, `\\`, `a\|b`. The right answers are computed on the decoded text.

## By hand

GUI: the same searches on screen, plain and regular expression.

## Notes

* Background: byte-based tools that do not decode Shift_JIS find `\` inside 表 and break regular expressions.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 8). Tables: `python3 tools/summarize.py ../results/*`.
