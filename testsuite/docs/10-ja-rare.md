# 10 — Japanese 4: 𠮷, variation selectors, user-defined characters

**Question.** Characters outside the basic plane, variation sequences, JIS level 3/4, the user-defined area, and older encodings.

## Data (`ja-rare`)

* `utf8-rare.txt` / `utf16le-rare.txt`: 𠮷, 葛 + U+E0100, 𩸽, 丂, 鷗, private-use characters, 👍, é as one and two code points.
* `cp932-gaiji.txt`: CP932 user-defined codes F040/F041 (= U+E000/U+E001).
* `eucjp-0212.txt`: EUC-JP 3-byte JIS X 0212 characters (丂, 鷗). `iso2022jp-short.txt`.

| preset | sizes |
|---|---|
| smoke | fixed files |
| trial | fixed files |
| full | fixed files |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set ja-rare
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set ja-rare --product uvf --product uvp
```

## What is checked

* Each character is found where it is, in UTF-8 and UTF-16.
* `^01 .野家$` with `-E`: 𠮷 must count as **one** character for `.`.
* User-defined codes are found as private-use characters; text after them is still found.

## By hand

GUI: cursor movement over 𠮷 and 葛󠄀 (one step each?), character count, how undisplayable characters are shown.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 10). Tables: `python3 tools/summarize.py ../results/*`.
