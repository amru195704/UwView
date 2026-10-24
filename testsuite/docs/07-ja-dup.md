# 07 — Japanese 1: CP932 characters with two codes

**Question.** In CP932 (Windows Shift_JIS) some characters have two or three byte codes: NEC special characters (0x87xx), NEC-selected IBM extensions (0xED/0xEE) and IBM extensions (0xFA–0xFC). Do all codes of one character display and search as the same character?

## Data (`ja-dup`)

* `cp932-dup.txt`: one line per code of every such character (396 characters, taken from the CP932 table itself); `cp932-dup-table.tsv` lists them.
* `cp932-dup.utf8.txt`: the same lines in UTF-8 as a control.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set ja-dup
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set ja-dup --product uvf --product uvp
```

## What is checked

* Searching `∵`, `≒`, `￢`, `Ⅰ`, `ⅰ`, `髙`, `﨑`, `∪`, `№` (typed in UTF-8) must find **every** code of that character, with the right line numbers.

## By hand

GUI: open the file, check that both codes show the same glyph; copy a line and save as CP932 and compare the bytes (which code is written back).

## Notes

* Background for readers outside Japan: Unicode maps these codes many-to-one, so converting to Unicode and back can change the bytes.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 7). Tables: `python3 tools/summarize.py ../results/*`.
