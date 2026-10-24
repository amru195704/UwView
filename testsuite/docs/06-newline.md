# 06 — Newlines and BOM

**Question.** Do line numbers stay right with CRLF, old Mac CR, mixed newlines or no final newline?

## Data (`newline`)

* 12 lines with the needle on line 4 and 12, written with LF, CRLF, CR, a mix of the three, no final newline, BOM + CRLF, UTF-16LE + CRLF, CP932 + CRLF, and LF CR pairs.
* A file with blank lines, a file of 1,000 empty lines.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set newline
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set newline --product uvf --product uvp
```

## What is checked

* The right answer counts LF, CRLF and CR as one line break each (what most editors show).
* `wc -l` counts only LF; the article shows where tools disagree.

## By hand

GUI: open each file and check the line count and the line numbers of the needle.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 6). Tables: `python3 tools/summarize.py ../results/*`.
