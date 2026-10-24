# 12 — 10,000 files

**Question.** Searching a deep tree of many small files.

## Data (`many`)

* `many-<count>/`: 10,000 / 100,000 files of 5–45 log lines, up to 8 folders deep. Every 997th file holds the needle.
* `.ignore` excludes `ignored/` (50 files with the needle) and `*.tmp`.

| preset | sizes |
|---|---|
| smoke | 500 |
| trial | 10K |
| full | 10K, 100K |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set many
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set many --cache cold,hot --product uvf --product uvp
```

## What is checked

* `many-*-needle`: excluded files must not be searched.
* `many-*-noignore`: `--no-ignore` finds them too.
* `many-*-tokyo`: many hits across files.
* `many-*-files`: `--files` lists exactly the files the wildcard expands to.

## By hand

GUI: search the folder with the same wildcard and note the time to the full result list.

## Notes

* NTFS, ext4 and APFS order directory entries differently; the answers do not depend on the order.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 12). Tables: `python3 tools/summarize.py ../results/*`.
