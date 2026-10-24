# 09 — Japanese 3: wave dash, width, NFC/NFD

**Question.** Windows and Mac turn the same Shift_JIS bytes into different Unicode (～ U+FF5E vs 〜 U+301C, － vs −, ∥ vs ‖). Full-width and half-width letters. macOS file names in NFD.

## Data (`ja-wave`)

* `utf8-wave.txt` and `cp932-wave.txt`: each look-alike on its own line; full/half-width letters and spaces.
* `nfc-text.txt` / `nfd-text.txt`: が written as one code point and as か + U+3099.
* File names `names/nfc/がぎぐ.txt` and `names/nfd/がぎぐ.txt` (NFD) are made **at run time** and are not committed.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set ja-wave
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set ja-wave --product uvf --product uvp
```

## What is checked

* UTF-8 file: exact code point matches (the right answer is known).
* CP932 file, NFD text, NFD names: `observe` — the hits are recorded and explained in the article, there is no single right answer.
* `names-all`: a plain wildcard must find both files.

## By hand

GUI: open both files, search each look-alike, and compare with what Windows Notepad / macOS TextEdit show.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 9). Tables: `python3 tools/summarize.py ../results/*`.
