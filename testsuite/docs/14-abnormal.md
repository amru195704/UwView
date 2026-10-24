# 14 — Mean tests

**Question.** Does it fail cleanly and never lie?

## Data (`abnormal`)

* Empty file, random binary with one text line, NUL bytes inside a line, a log that another process appends to while it is searched, a file without read permission, a missing file, a folder given as a file, a gzip file cut in half, a `.gz` that is not gzip, a broken regular expression, a folder link that points to itself.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set abnormal
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set abnormal --product uvf --product uvp
```

## What is checked

* No crash (exit code 0, 1 or 2 only) and no hang (time limit).
* Errors are reported with exit code 2.
* The growing log: any consistent answer is fine; it must finish.

## By hand

GUI: open the same files; note messages and whether the window stays usable.

## Notes

* The permission case is skipped on Windows and when running as root.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 14). Tables: `python3 tools/summarize.py ../results/*`.
