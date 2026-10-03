# UwView

*[日本語](README.md) ｜ English*

**Search in the CLI, read in the GUI. A CLI/GUI tool for investigating huge logs and text files.**

**The CLI (`uvf`) searches as fast as ripgrep, and the GUI opens files as fast as klogg.** The difference is that the two **connect in a single pass**. Type `uvf file 'word' -open` and the GUI opens the moment the search ends, with the hits already listed. **The GUI does not search again.**

📥 **[Download (free)](https://github.com/amru195704/UwView/releases/latest)** · 🌐 **[Official site](https://uvp.y42u.net/en/)** · 🧪 **[Try it in your browser](https://amru195704.github.io/UwView/)**  
📖 **[uvf command manual](2-doc/uvf_コマンド操作マニュアル.md)** · 🖥 **[GUI manual](2-doc/UwView_操作マニュアル.md)** ([PDF](2-doc/UwView_操作マニュアル.pdf)) — both in Japanese · 🧾 **[v1.7.3.6.9 release notes](2-doc/release-body-v1.7.3.6.9.md#uwview-v17369--wide-field-english)**  
▶ **[Watch on YouTube (English)](https://www.youtube.com/playlist?list=PLBJs4svTLd_w)** · [Japanese](https://www.youtube.com/playlist?list=PLVa-Z1XEnkKs) · [Channel @uwviewapp](https://www.youtube.com/@uwviewapp)

> **The latest release is v1.7.3.6.9 "Wide Field".** Search many files with a single wildcard. Mix plain text with **seven compressed formats** (gz, bz2, xz, lzma, zst, lz4, br) in one run — no external commands needed.
> In v1.7.3.6.9 the second search of a cached file is at or above ripgrep, and **on macOS, Windows and Linux the overall result is 2.49× / 3.42× / 1.88×** the combinations of ripgrep and friends ([results on three systems](#results-on-three-operating-systems)).

---

## UwView in numbers

| When you want to… | The others | **UwView** |
|---|---|---|
| **Search a compressed log** (7 formats, 979 MB) | **On par** with ugrep and ripgrep; **up to 2.73× faster** than the standard grep tools (zgrep etc.) | **`uvf` is the fastest in all 7 formats** |
| **Search the same compressed log again** | Every tool decompresses again (bz2 takes 7–8 s) | **`uvp` takes 0.11–0.12 s in every format** (**62×** `uvf` on bz2) |
| **Search 12 gz/plain files, ~60 GB uncompressed** | zgrep 89.35 s | **`uvf` 17.60 s (5.08×)** |
| **Search 50 GB and read the hits in the GUI** | klogg (open + search) 108.1 s | **`uvf … -open` 50.76 s (2.13×)** |
| **Ask a 50 GB file a second question** | ripgrep 70.33 s | **`uvp` 6.45 s (10.9×)** |
| **Search 258 GB / 4.5 billion lines and hand the hits to the GUI** | klogg needs 258 s just to finish opening | **`uvf … -open` 261.37 s**, search done and hit list shown |

**`uvf` (CLI) and the GUI are the free edition.** Only the `uvp` rows (2nd and 5th) are the paid [UwView Pro](https://uvp.y42u.net/en/pro-en/) (14-day free trial).

**Where we lose, first.**

- **On many small files, ripgrep is slightly faster** (Linux kernel source, 86,000 files, five searches combined: `uvf` at 1/1.20 of ripgrep — under 1.5×, so on par).
- **On Windows, ripgrep was faster for some compressed formats** (xz, zst and lz4 at 1/1.5–1/2.1; on par or better on macOS and Linux). We are still looking into why.
- **Because `uvp` builds an index the first time, a one-off search can be slower than ripgrep** (bundling 12 files: 42.45 s vs ripgrep 28.19 s). From the second search on it is 2.3× faster than ripgrep, so two searches come out even and the gap opens from the third.

> A difference under 1.5× is written as "on par", never as a win. Timings are from one Mac (conditions at the [end of this page](#where-the-numbers-come-from)). **Do not compare seconds across machines.**

---

## CLI: against ripgrep and the grep family

### Search compressed logs as they are (7 formats)

The same 979 MB text, compressed in seven formats, searched for 東京 (hot, seconds). **Every tool's output matched `uvf` in line count and content.**

| Format | Standard grep | ugrep 7.8.5 | ripgrep 15.2.0 | **`uvf`** | `uvp` 1st | **`uvp` 2nd** |
|---|---:|---:|---:|---:|---:|---:|
| gz | 0.90 (zgrep) | 0.45 | 0.43 | **0.33** | 0.42 | **0.12** |
| bz2 | 8.03 (bzgrep) | 7.62 | 7.89 | **7.43** | 7.64 | **0.12** |
| xz | 4.01 (xzgrep) | 3.64 | 4.21 | **3.37** | 3.44 | **0.11** |
| lzma | 3.21 (xzgrep) | 2.83 | 3.40 | **2.57** | 2.64 | **0.12** |
| zst | 0.63 (zstdgrep) | 0.65 | 0.54 | **0.50** | 0.57 | **0.12** |
| lz4 | 0.65 (lz4 -dc \| grep) | 0.33 | 0.31 | **0.28** | 0.35 | **0.12** |
| br | 1.33 (brotli -dc \| grep) | 0.91 | 0.87 | **0.74** | 0.85 | **0.12** |

- **For a one-off search, `uvf` is the fastest in all seven formats.** Against ugrep and ripgrep, though, the gap is 1.03–1.36× — **on par**.
- **Against the standard grep tools there is a real gap**: **2.73×** on gz, **2.32×** on lz4, **1.80×** on br (bz2, xz, lzma and zst are on par).
- **`uvp`'s second search takes 0.11–0.12 s whatever the original format**, because it searches the index (`.uwvz`) it built the first time and the weight of the original format disappears. That is **2.3–62×** `uvf` (bz2 62×, xz 31×, lzma 21×). `uvp`'s first search, index-building included, is on par with `uvf`.
- `uvf` **calls no external commands**. `rg -z` calls gzip, xz and so on for each format and silently skips the file when the command is missing.
- ag (The Silver Searcher) could not decompress four of the formats and crashed on files over 2 GB uncompressed, so it was left out of the comparison.

### Search many files at once

| hot, seconds | zgrep | ugrep | ripgrep | **`uvf`** | `uvp` 1st (bundling) | **`uvp` 2nd** |
|---|---:|---:|---:|---:|---:|---:|
| 5 gz files (~7.4 GB uncompressed) | 9.43 | 2.56 | 1.34 | **1.04** | 3.04 | **0.73** |
| 7 gz + 5 plain = 12 files (~60 GB uncompressed) | 89.35 | 25.38 | 24.62 | **17.60** | 41.23 | **9.52** |

- With 12 files, `uvf` is **5.08× zgrep** and on par with ugrep and ripgrep (1.44×, 1.40×). Cold (cache dropped), `uvf` 21.35 s and ugrep 26.85 s were on par too.
- **If you search the same 12 files again and again, use `uvp`.** It bundles them into one `.uwvz`, and from the second search on it is **1.85×** faster than `uvf`.

```bash
uvf '*.log' ERROR                    # every .log in this folder
uvf '**/*.log' ERROR -open           # include subfolders and send the hits to the GUI
uvf 'app.log,app.log.*.gz' ERROR     # plain and compressed together, in one run
```

Files are skipped by **the same rules as ripgrep** (`.ignore`, `.gitignore`, hidden files; use `--no-ignore` to keep them).

### One large file

Plain search for 東京 (seconds; cold = right after dropping the cache / hot = the second run straight after)

| | ripgrep 15.2.0 | **`uvf`** (free) | **`uvp`** (Pro; 1st run includes building the index) |
|---|---:|---:|---:|
| 3 GB | 3.26 / 0.31 | **3.04 / 0.29** | 3.43 / **0.29** |
| 10 GB | 10.94 / 0.97 | **10.15 / 0.88** | 11.82 / 1.00 |
| 50 GB | 64.68 / 70.33 | **50.66 / 55.94** | 58.81 / **6.45** |

**`uvf` is on par with ripgrep or better at every size, on both the first and the second run** (v1.7.3.6.9 made counting newlines for line numbers 5× faster, which put the second run ahead of ripgrep too). At 50 GB the file does not fit in memory, so neither ripgrep nor `uvf` gets faster the second time (ripgrep's second run varies between 55 and 70 s from one measurement to the next). **Only `uvp` does: 6.45 s on the second run** — **10.9×** ripgrep.

Seven searches (plain, `-i`, `-E`, `-E` anchored, `-E -i`, `-v`, `-E -v`), cold + hot total:

| | ripgrep | **`uvf`** | **`uvp`** |
|---|---:|---:|---:|
| 3 GB | 33.63 s | 33.55 s (on par) | **14.05 s (2.39×)** |
| 10 GB | 95.41 s | 81.99 s (on par) | **29.07 s (3.28×)** |
| 50 GB | 870.64 s | 768.93 s (on par) | **232.98 s (3.74×)** |

Across all 96 items (searches plus filtering, counting, sorting, head/tail, writing out, gz input and more) against combinations of rg, sed, sort, uniq and gzip, the overall result is **2.49×**, with **zero mismatched results**.

### Many small files

The Linux kernel source (86,602 files, about 22 KB on average) searched with `uvf 'src/linux/**' word -H`. Five searches (0 to 38,253 matching lines), totals in seconds:

| | First (cold) | Second (hot) | Total |
|---|---:|---:|---:|
| ripgrep 15.2.0 | 22.76 | 7.47 | 30.22 |
| **`uvf`** (free) | 26.49 | 9.72 | 36.21 (1/1.20, on par) |
| **`uvp`** (Pro; the first search bundles all 86,000 files into one `.uwvz`) | 10.76 | **1.42** | **12.19 (2.48×)** |

This is where **ripgrep is slightly faster** (under 1.5×, so on par). In v1.7.3.6.5 the second run took about 18× as long as ripgrep, and in v1.7.3.6.8 still about 2.2×. If you search the same place again and again, use `uvp`: from the second search on, about 0.3 s.

### Results on three operating systems

The same suite was run on macOS, Windows and Linux. **All correctness tests (66 input types, 36 multi-file, 43 exclusion) passed on all three, and the speed runs had zero mismatched results.** Figures are ratios to ripgrep (above 1 = `uvf`/`uvp` faster); **seconds are never compared across systems**.

| | macOS | Windows | Linux |
|---|---|---|---|
| Machine | Apple M4, 10 cores, 32 GB | x64, 8 logical cores, 16 GB | aarch64, **2 logical cores**, 7 GB |
| `uvf`, one large file, seven searches (3 GB / 10 GB / 50 GB) | 1.00 / 1.16 / 1.13 | 1.48 / 1.90 / **2.47** | 1.31 / 1.13 / 1.33 |
| `uvp`, the same seven (including building the index) | 2.39 / 3.28 / 3.74 | 2.22 / 2.81 / 3.44 | 1.62 / 2.45 / 1.99 |
| `uvf`, many files (5 plain / 7 gz / 12 mixed) | 1.02 / 1.31 / 1.29 | 1.86 / 1.54 / 1.58 | 2.22 / 1.67 / 1.67 |
| **Overall, 96 items** | **2.49** | **3.42** | **1.88** |

- **The gap is larger on Windows and Linux than on the Mac.** On Windows ripgrep's first run is slow; on Linux (2 cores) `rg -z` pays for calling external decompressors.
- **Weak spots**: on Windows, `uvf` is 1.5–2.1× slower than ripgrep on compressed xz, zst and lz4. On Linux, `uvp`'s first run (building the index) is heavy for fast-to-decompress zst and lz4 and for plain text, giving 1/1.2–1/1.4 over two runs.
- Windows and Linux were measured with 1.7.3.6.8 (1.7.3.6.9 only changes searching many small files).

> **If you use `rg` on Windows:** on a 50 GB file, `--no-mmap` makes it **2.89×** faster → **[Article](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/)**

---

## GUI: against klogg

| cold, seconds | 3 GB | 10 GB | 50 GB |
|---|---:|---:|---:|
| **Open**: klogg 24.11.0 | 3.65 | 10.98 | 52.55 |
| **Open**: **UwView GUI** | **2.99** | **10.13** | **50.44** |
| **Search**: klogg | 0.56 | 11.75 | 55.59 |
| **Search**: **UwView GUI** | **0.32** (1.75×) | **10.15** | **53.50** |
| **Find and read**: klogg (open + search) | 4.21 | 22.73 | 108.1 |
| **Find and read**: **`uvf … -open`** | **3.40** | **10.42 (2.18×)** | **50.76 (2.13×)** |

**Opening and searching are on par with klogg** (only the 3 GB search is 1.75×). **The gap opens up at "find it and read it."** klogg reads the file once to open it and again to search it. **`uvf … -open` reads it once, searching as it goes.**

**The GUI "just opening" versus `uvf … -open` "searching and handing the hits to the GUI": +0.41 / +0.29 / +0.32 s.** The file grows 17×, and the cost of searching stays at 0.3–0.4 s. From the end of the command to the hit list appearing in the GUI takes **0.03–0.08 s**, because the GUI only lays out the positions it was given.

### Even at 258 GB, the end of the file is there the moment it opens

klogg **cannot scroll to the end until its index is built** — **4 min 18 s** at 258 GB, and until then you only see the start. UwView **makes the whole file navigable first** and builds the index in the background.

| 258.68 GB, 4.5 billion lines | Until you can reach the end | What the GUI shows |
|---|---|---|
| klogg 24.11.0 | **4 min 18 s** | only the start, until the index is built |
| **UwView (free)** | **no wait** | the text (line numbers once the index is built; finishes opening in 4 min 15.5 s) |
| **UwView Pro, first time** | **no wait** | same as free (5 min 18 s, as it also saves the index) |
| **UwView Pro, second time on** | **no wait** (opens in **0.01–0.07 s**) | the text + **line numbers** from the start |

Mouse wheel, dragging the scroll bar or `Cmd/Ctrl+End` — all reach the end straight after opening. Jumping to a line number feels instant whatever the file size (0.003 ms at 890 million lines).

---

## UwView Pro — the second question is where the numbers change

The first time, every tool has to read the whole file at least once. That is physics; there is no way round it. The difference is **what is left afterwards**.

`uvp` builds a **`.uwvz`** (a compressed format plus an index, about 1/9 of the original) the first time, and **from then on never touches the original file.**

| Same file, same word, second search (hot) | ripgrep | `uvf` (free) | **`uvp` (Pro)** |
|---|---:|---:|---:|
| 50 GB plain text | 70.33 s | 55.94 s | **6.45 s (10.9× ripgrep)** |
| 979 MB bz2 | 7.89 s | 7.43 s | **0.12 s (62× `uvf`)** |
| 12 gz + plain files (~60 GB uncompressed) | 24.62 s | 17.60 s | **9.52 s (2.59× ripgrep)** |

- **The first time, it is on par**: 50 GB, `uvp` 58.81 s vs ripgrep 64.68 s (while building the index).
- **The 16 things only `uvp` does** — filtering, counting, sorting and so on — finish **5.15–7.49×** faster than combinations of rg, sed, sort and friends.
- **Delete the original and you can still search the `.uwvz`; `-extract` brings it back.** 51.25 GB becomes about 5.7 GB.
- Text inside `.zip` files and OpenStreetMap `.pbf` files can be used as input too.
- In the GUI, from the second time on it **opens in 0.01–0.07 s, with line numbers from the start**.

**`uvp` pays off when you ask a file over 10 GB, or a compressed log, more than one question.** For a one-off, the free edition is enough.

> **$129** one-time / **$9** a month (Edit Upgrade +$120 / +$8) · **14-day free trial** → **[UwView Pro](https://uvp.y42u.net/en/pro-en/)**

---

## Install

**Mac (Homebrew)**
```bash
brew install --cask amru195704/uwview/uwview
```
**Windows (Scoop)**
```powershell
scoop bucket add uwview https://github.com/amru195704/scoop-uwview
scoop install uwview
```
**Linux, or to install by hand**: download the dmg / zip / tar.gz from [GitHub Releases](https://github.com/amru195704/UwView/releases/latest).

Both Homebrew and Scoop fetch from the official GitHub Releases and check the SHA256. **The `uvf` command works straight away.**
To update: `brew upgrade --cask uwview` / `scoop update uwview`.

---

## Usage

```bash
uvf japan-latest.osm '東京' -open    # search, then read the hits in the GUI
uvf app.log.zst ERROR                # compressed files are searched as they decompress (7 formats)
uvf '**/*.log' ERROR --files         # list the files that would be searched, without searching
```

**The GUI opens the moment the search ends, with a hit list carrying line numbers.** From there you read the lines around a hit, jump from the list, colour several keywords, search again with other conditions — that back-and-forth is what investigating is. Results from several files can also be opened in extra tabs (up to 8) from the file list.

Syntax, options, regular expressions, exit codes and a ripgrep cheat sheet are in the **[uvf command manual](2-doc/uvf_コマンド操作マニュアル.md)**;
the GUI (search, hit list, file list and tabs, highlighter, keys) is covered in the **[GUI manual](2-doc/UwView_操作マニュアル.md)** ([PDF](2-doc/UwView_操作マニュアル.pdf)). Both are in Japanese.

---

## Which one to use

| You want to… | Use |
|---|---|
| **Search in the CLI only** | **`uvf`** (free) — on par with ripgrep |
| **Search many files or compressed logs together** | **`uvf '*.log' word`** (free, 7 compressed formats) |
| **Search in the CLI and read the hits in the GUI** | **`uvf … -open`** (free) — **the shortest path** |
| **Open a file in the GUI and look around** | **UwView GUI** (free) — scroll to the end the moment it opens |
| **Keep coming back to the same file or logs / keep them compressed** | **[UwView Pro](https://uvp.y42u.net/en/pro-en/)** (`uvp`) |
| **Edit** a huge file | **UwView Pro + Edit Upgrade** |
| Up to ~3 GB, no install | **[Browser edition](https://amru195704.github.io/UwView/)** |

**Both `uvf` and the GUI are in the free edition.** A single executable, no installer, the same on Windows / macOS / Linux.

---

## Features

**CLI (`uvf`)**: `-i`, `-E`, `-v`, exit codes 0/1/2 (hit / no hit / error, as grep), `-open`, `--json`, `--files`, `--tune` /
**search many files at once** (wildcards, `**`, comma lists; `.ignore` / `.gitignore` follow ripgrep's rules) /
**search 7 compressed formats as they are** (gz, bz2, xz, lzma, zst, lz4, br; no external commands; mix with plain text in one run)

**GUI**: **measured up to 258.68 GB / 4.5 billion lines** (with the free edition) / scroll to the end the moment it opens / hit list (original line numbers, jump, context, save) /
read results from many files (file list, up to 8 tabs) / colour several keywords (32 colours, 7 presets, `.uwvhl`) / minimap /
encoding detection (UTF-8, Shift-JIS, EUC-JP, UTF-16) / live tail / tabs, bookmarks, horizontal scroll, session restore / identical rendering on every OS

**Runs on**: .NET 10 / Avalonia UI 12.x / Windows, macOS, Linux

**Known limitation**: if a compressed file contains **a single line longer than 64 MiB** and that line matches, the search may stop (the file is not damaged). We favour speed, so no fix is planned; decompress such files before opening them.

---

## More detail

| | |
|---|---|
| 📖 **uvf command manual** (Japanese) | [2-doc/uvf_コマンド操作マニュアル.md](2-doc/uvf_コマンド操作マニュアル.md) — syntax, options, compressed files, regular expressions, exit codes, ripgrep cheat sheet, performance |
| 🖥 **GUI manual** (Japanese) | [2-doc/UwView_操作マニュアル.md](2-doc/UwView_操作マニュアル.md) · [PDF](2-doc/UwView_操作マニュアル.pdf) — layout, search, hit list, file list and tabs, highlighter, keys |
| 🧾 **v1.7.3.6.9 Wide Field release notes** | [release-body-v1.7.3.6.9.md](2-doc/release-body-v1.7.3.6.9.md#uwview-v17369--wide-field-english) (previous: [v1.7.3.6.7](2-doc/release-body-v1.7.3.6.7.md#uwview-v17367--wide-field-english), [v1.7.3.6](2-doc/release-body-v1.7.3.6.md#uwview-v1736--wide-field-english)) |
| ▶ **Videos (YouTube)** | [Playlist "UwView\|English"](https://www.youtube.com/playlist?list=PLBJs4svTLd_w) · [Japanese](https://www.youtube.com/playlist?list=PLVa-Z1XEnkKs) · [Channel @uwviewapp](https://www.youtube.com/@uwviewapp) — how to use v1.7.3.6 in 8 topics, plus an overview video |
| 📊 **All measurements and conditions** | [Benchmarks](https://uvp.y42u.net/en/benchmarks-en/) (EmEditor, klogg, 010 Editor, UltraEdit, Log Viewer, grep, ripgrep, amber, from 3 GB to 250 GB. **The numbers where we lose are published as they are.**) |
| 📖 **An honest re-measurement against klogg** | [Article](https://uvp.y42u.net/en/blog/uvp-klogg-open-lose-flow-win-en/) |
| 📖 **One 50 GB file, three arenas** | [Article](https://uvp.y42u.net/en/blog/uvp-three-arenas-50gb-en/) |
| 📖 **Choosing a log compression format (7 formats measured)** | [Article](https://uvp.y42u.net/en/blog/log-compression-format-choice-en/) |
| 📖 **ripgrep's `--no-mmap`** | [Article](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/) |
| 🔧 **Full feature list, architecture, build and test instructions** | [Previous README (as of v1.6.5)](docs/README.en-v1.6.5.md) |
| 📰 **Press kit** | [PRESSKIT.md](press-kit/PRESSKIT.md) |

---

## Where the numbers come from

- **Machine**: MacBook Air (Apple M4, 10 cores, 32 GB memory), external USB SSD. Data: OpenStreetMap (Japan) XML; search word 東京. **Timings vary by machine. Do not compare seconds across machines.**
- **CLI, one large file and many small files**: `uvf` / `uvp` 1.7.3.6.9, ripgrep 15.2.0, 2026-10-04. Cold = right after dropping the cache (`sudo purge` + 10 s wait); hot = the second run straight after.
- **Three systems**: the Mac as above. Windows (x64, 8 logical cores, 15.6 GB) and Linux (aarch64, 2 logical cores, 7.0 GB): `uvf` / `uvp` 1.7.3.6.8, ripgrep 15.2.0 (15.1.0 on Linux), 2026-10-03. On Windows `uvf` was set to 4 threads (`uvp` used 8).
- **CLI, 7 compressed formats and many files**: `uvf` 1.7.3.4 (search code identical to v1.7.3.6) / `uvp` 1.7.3.5 (same search code as 1.7.3.6), ugrep 7.8.5, ripgrep 15.2.0, macOS's standard zgrep / bzgrep / xzgrep / zstdgrep, 2026-09-28. **Hot** (faster of two runs each); only the 12-file cold figures were measured by the owner. Every run's output was checked for line count and content. The 5-plain-file comparison alone is `uvf` 1.7.2.7 (cold + hot).
- **GUI vs klogg**: the latest comparison is v1.6.6, klogg 24.11.0, 2026-09-20, cold. 258 GB: v1.6.6, 2026-09-21.
- A difference under 1.5× is written as "on par".

---

## Supporting the project

**The free edition will stay free** — for personal use and for internal use inside a company.

If it earns its place in your work, you can support it through [GitHub Sponsors](https://github.com/sponsors/amru195704). **Entirely optional.**
**What helps more than money**: telling us when it did not work (which file, what went wrong), and **numbers from your
own huge files** — **measurements where we lose are especially welcome** → [Issues](https://github.com/amru195704/UwView/issues)

Development is funded mainly by sales of [UwView Pro](https://uvp.y42u.net/en/pro-en/). **Buying Pro is the most direct support there is.**

## License

[PolyForm Internal Use License 1.0.0](LICENSE). **Free for personal use and internal business use.**
**Redistribution, bundling into a product or service, resale and supply to third parties are not permitted** — those
require a separate commercial (redistribution) license → [Issues](https://github.com/amru195704/UwView/issues).
Japanese reference translation: [LICENSEjp.txt](LICENSEjp.txt) (the English text is binding).

> **On package-manager submissions (clarification)**
> **A Homebrew, Scoop or winget manifest that points at the official download
> ([GitHub Releases](https://github.com/amru195704/UwView/releases/latest)) is not redistribution** — the user's own
> machine fetches the file from the official source. **Submissions and pull requests of that kind are welcome.**
> For the hash in the manifest, use the `SHA256SUMS` attached to each release.
> Hosting a copy of the file anywhere other than the official source is the case that needs to be discussed first, as above.

## Other products by the same author

iOS apps for surveyors and land investigators by the same author (y4u), independent of UwView:
**[GeoConverter Pro](https://gcpro.y42u.net/)** (coordinate conversion) · **[GeoPrism JP](https://gmp.y42u.net/)** (visualising datum shifts) · **[GeoDiveExa](https://y42u.net/tec001/)** (RTK-GNSS surveying)
