# 00 — Rules of the series

This folder is the primary record of the **UwView test series**: the data generators, the procedure,
the raw results (CSV) and the list of weak points. Blog posts, notes and videos link here, so the
numbers in them always come from these files.

## What is tested

| Product | What | Where it runs |
|---|---|---|
| **uvf** | the command-line search that ships with the free UwView | `tools/run.py --product uvf` |
| **uvp** | the command-line search of the paid edition (UwView Pro) | `tools/run.py --product uvp` |
| UwView GUI / UwView Pro GUI | open, jump to the last line, search on screen | by hand, see [gui.md](gui.md) |
| Web edition | the same files, up to the size it accepts | by hand, see [gui.md](gui.md) |

## The three machines

macOS (real machine), Linux (a VMware virtual machine) and Windows 11 (real machine).
Machine, CPU, memory, storage and file system are listed in [environment.md](environment.md) and
are recorded automatically in `env.json` next to every result.

## Data

* Large data is **not** distributed. `tools/gen.py` makes it from a fixed seed, so anyone gets the
  same bytes on any OS. `tools/check.py data` compares the SHA-256 with [`../manifests/`](../manifests/).
* Small data (encodings, newlines, Japanese characters, broken files) is also committed under
  [`../testdata/`](../testdata/) so it can be inspected on GitHub.
* Every case carries its right answer (`cases.json`): exit code, number of result lines and, where
  it matters, the exact line numbers. A run is only counted as correct when all of them match.

## Timing

* Wall-clock time from starting the command to its exit, **including** writing all result lines
  to a file. Measured by `tools/run.py`; peak memory (RSS) is recorded too.
* **cold** = right after the OS file cache was dropped. **hot** = the file was just read
  (one unrecorded warm-up run first). **first** = the first search of the file in a session, kept
  apart from the medians (with `--cache cold,hot` the cache is dropped before it too). For uvp the
  first search and the following ones differ a lot; both are shown.

| OS | How cold is made | Note |
|---|---|---|
| macOS | `sync` + `sudo purge` | purge may not drop everything; one cold run after a restart is also taken for episode 3 |
| Linux (VMware) | `sync` + `echo 3 > /proc/sys/vm/drop_caches` | the **host** keeps its own cache. Drop it too with `--cold-hook` (e.g. run `purge` on the Mac host) |
| Windows | RAMMap `-Es` (empty standby list), `--rammap` | there is no built-in command; without RAMMap, cold is measured by hand after a restart |

* Files that do not fit in memory (over 60 % of RAM) are measured cold only: hot would be the same.
* **5 runs** per case and cache, **3 runs** for files of 10 GiB and more; the **median** is reported
  (`summary.csv`), with min and max.
* GUI times are taken by hand with a stopwatch, **3 runs**, median.

## Reading the numbers

* Seconds are compared **only on the same machine**. Never compare seconds across OSes or machines;
  the Linux numbers include the cost of virtualization and are not "Linux on real hardware".
* Two values within **1.5×** of each other are written as **same**.
* Every table states the product version and the date. Numbers change with new versions.

## Weak points

Wrong answers, crashes and hangs are **not fixed before publishing**. They are listed in
[`../weak-points.md`](../weak-points.md) with the version they were found in, and become targets
for the next version. A fix gets a follow-up post.

## Episodes

| # | Topic | Data set | Doc |
|---|---|---|---|
| 0 | Rules (this page) | – | – |
| 1 | Opening 1 GiB to 100 GiB | `huge` | [01](01-huge.md) |
| 2 | 100 million and 1 billion lines | `lines` | [02](02-lines.md) |
| 3 | Cold vs hot | `huge` | [03](03-cold-hot.md) |
| 4 | One huge line | `longline` | [04](04-longline.md) |
| 5 | Every encoding | `encoding` | [05](05-encoding.md) |
| 6 | Newlines and BOM | `newline` | [06](06-newline.md) |
| 7 | Japanese 1: CP932 characters with two codes | `ja-dup` | [07](07-ja-dup.md) |
| 8 | Japanese 2: searching for 表 (the 0x5C problem) | `ja-dame` | [08](08-ja-dame.md) |
| 9 | Japanese 3: wave dash, width, NFC/NFD | `ja-wave` | [09](09-ja-wave.md) |
| 10 | Japanese 4: 𠮷, variation selectors, user-defined characters | `ja-rare` | [10](10-ja-rare.md) |
| 11 | Compressed files without unpacking | `compress` | [11](11-compress.md) |
| 12 | 10,000 files | `many` | [12](12-many.md) |
| 13 | Does search speed depend on the number of hits? | `search` | [13](13-search.md) |
| 14 | Mean tests | `abnormal` | [14](14-abnormal.md) |
| 15 | Summary: which one to use | all | [15](15-summary.md) |
