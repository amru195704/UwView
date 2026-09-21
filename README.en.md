# UwView

*[日本語](README.md) ｜ English*

**A tool for investigating huge logs and text files. Search in the terminal, read in the window.**

📥 **[Download (free)](https://github.com/amru195704/UwView/releases/latest)** · 🌐 **[Official site](https://uvp.y42u.net/en/)** · 🧪 **[Try it in your browser](https://amru195704.github.io/UwView/)**

---

## UwView is on par with ripgrep and on par with klogg. It does both in one pass, so end to end it is about 2× quicker.

**Searching is on par with `ripgrep`. Opening is on par with `klogg`.** Compared one at a time, it is a tie.

But what you actually want is to **find it and read the place it hit**. Search with `rg`, find the hit, then reopen the
file in a viewer to read around it — **and the file has now been read twice.**
With UwView you type `uvf <file> '<pattern>' -open`, and **a single read searches the file and puts the hits on screen.**

**That is where the 2× comes from** — not a cleverer algorithm, but **two passes over the file becoming one.**

**One 51.25 GB file** (Mac M4, external USB SSD, every run cold with the cache dropped)

| Task | Against | Theirs | **UwView** | |
|---|---|---:|---:|:---:|
| **Search** (CLI, one term) | ripgrep 15.2.0 | 55.54 s | **`uvf … -open` 50.76 s** | level |
| **Open** (GUI) | klogg 24.11.0 | 52.55 s | **50.44 s** | level |
| **Search, then read the hit** | `rg` + klogg | 108.1 s | **`uvf … -open` 50.76 s** | **2.13×** |

**Rows 1 and 3 are the same command, the same single run.** Searching, and searching plus putting the hits on screen,
take the same time.

**All of that is the free edition.**

> The 108.1 s for `rg` + klogg is two measured figures added (55.54 s + 52.55 s). klogg alone (open + search) is also 108.14 s.
> Term: `東京` (94,979 hits). `uvf … -open`'s 50.76 s is 51.25 GB ÷ 50.76 s = **963 MB/s** — exactly one pass over the file.
> **[Conditions and full data →](https://uvp.y42u.net/en/benchmarks-en/)**

### By size

| | 3 GB | 10 GB | 50 GB |
|---|---:|---:|---:|
| ripgrep 15.2.0 (search, one term) | 3.29 s | **11.38 s** | 55.54 s |
| klogg 24.11.0 (open) | 3.65 s | 10.98 s | 52.55 s |
| **UwView CLI (`uvf … -open`, search and hand to the window)** | **3.40 s** | **10.42 s** | **50.76 s** |
| **UwView GUI (open only)** | **2.99 s** | **10.13 s** | **50.44 s** |

**Every row is one pass over the file.** None of these tools keeps an index, so the disk's read speed is the limit.

**Compare the last two rows.** Opening in the window and nothing else takes 50.44 s; searching from the command line and
putting the hits on screen takes 50.76 s. The difference is **+0.41 s / +0.29 s / +0.32 s** — **the file grows 17×, and
searching still adds only 0.3–0.4 s.** The search happens during the read, so it needs no pass of its own.

**Ask the same file twice and ripgrep wins at 3 GB** — that size fits in RAM, so its second run comes from cache.
Running seven searches twice each totals 32.30 s for rg against 33.31 s for `uvf` at 3 GB, 158.69 s against
**147.01 s** at 10 GB, and 806.22 s against **735.97 s** at 50 GB.

> **If you run `rg` on Windows:** at 50 GB, adding `--no-mmap` makes it **2.89× faster**
> → **[the write-up](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/)**

---

## How it works

```bash
uvf japan-latest.osm 'pattern' -open
```

**The window opens the moment the search finishes, with a line-numbered list of hits. It does not search again.**
**From the command finishing to the list being on screen: 0.03–0.08 s** (0.03 s at 3 GB and 10 GB, 0.08 s at 50 GB) — the window only has to lay out
the positions it was handed.
From there: read the lines around a hit, jump from the list, colour several keywords at once, change the pattern and
look again. Investigation is made of that back and forth.

**258.68 GB and 4.5 billion lines** behave the same way: `uvf … -open` takes **265.21 s**, while
**klogg needs 258 s merely to finish opening that file** (7 s apart, 2.8%).

---

## Which one

| Situation | Use |
|---|---|
| Just searching, no window needed | **`uvf`** (free) |
| **Search, then read the hit** | **`uvf … -open`** (free) — **the shortest path** |
| You just want to open it and look | **the UwView window** (free) — **scroll to the end the moment it opens** (below) |
| **Coming back to the same file / keeping it compressed** | **[UwView Pro](https://uvp.y42u.net/en/pro-en/)** |
| **Editing** a huge file | **UwView Pro + Edit Upgrade** |
| Up to 3 GB, nothing installed | **[browser build](https://amru195704.github.io/UwView/)** |

**Both `uvf` and the GUI are in the free build.** Single executables, no installer, same on Windows, macOS and Linux.

### Even at 258 GB, you can look at the end the moment it opens

`klogg` **cannot scroll to the end until its index is built** — **4 min 18 s at 258 GB**,
and until then you only see the top of the file.

UwView makes the whole file navigable **first** and builds the index in the background.
"Just show me the tail" and "let me skim the middle" involve **no waiting at all**.

| 258.68 GB, 4.5 billion lines | Until you can reach the end | What is on screen |
|---|---|---|
| klogg 24.11.0 | **4 min 18 s** (258 s) | only the top of the file until the index is built |
| **UwView (free)** | **no wait** | the text (**line numbers once the index is built**) |
| **UwView Pro, 1st open** | **no wait** | same as the free edition |
| **UwView Pro, 2nd open onward** | **no wait** (opens in 0.01–0.07 s) | the text **and line numbers**, from the start |

**You simply scroll.** Mouse wheel, dragging the scrollbar, `Cmd/Ctrl+End` — you can reach the end
of the file the moment it opens. **You do not have to type a ratio**; `50%` and the like are just a
shortcut when you want to jump somewhere in one move.

**The first open is the same in both editions.** The only difference is **when line numbers appear**:
they show up once the index is built (about 4.5 minutes at 258 GB — much the same as klogg).
**Scrolling works normally the whole time.**

**The difference starts at the second open.** Pro keeps the index in its `.uwvz`, so line numbers are
there **from the moment it opens** — type a line number and go.

- The jump feels instant regardless of size (0.003 ms at 892 million lines, measured)

> **What is fast here is not the index — it is the order.** Build the index and then let people use
> the file, or let them move around first and build the index behind them. That is the whole difference.

### UwView Pro — the order of magnitude changes at the second question

On the first question klogg, `uvf` and `uvp` all have to read the whole file once. That is physics; there is no way
around it. What differs is **what is left behind.** `uvp` builds a `.uwvz` on the first run (about one ninth of the
original, with a line index) and **never touches the original again.**

**Asking a second question of the same 51.25 GB file**

| | Open | Search | Total | |
|---|---:|---:|---:|:---:|
| klogg 24.11.0 | 52.55 s (rebuilt every time) | 55.59 s | 108.14 s | |
| **`uvf … -open`** (free) | — | — | **50.76 s** | 2.13× |
| **`uvp`** (with `.uwvz`) | **0.01–0.07 s** | **6.34 s** | **6.41 s** | **16.9×** |

**16.9× klogg, and 7.9× our own free `uvf`.** This is where the order of magnitude changes.
klogg keeps no index, so it **rebuilds one every time you open the file**; `uvf` builds none, so **the second question
costs the same as the first**. Only `uvp` **earns back what the first pass cost.**

**It stays searchable with the original deleted, and `-extract` puts it back.** 51.25 GB becomes about 5.7 GB.

**On the first question the free `uvf` is level with ripgrep, while `uvp` is 15–20% slower because it builds its `.uwvz`.**
**`uvp` pays off past 10 GB, when you ask the same file more than one question.**

> **$129** one-time / **$9** per month (Edit Upgrade +$120 / +$8), with a **14-day free trial**
> → **[UwView Pro](https://uvp.y42u.net/en/pro-en/)**

---

## What it does

**Largest measured: 258.68 GB, 4.5 billion lines** (on the free edition) / the `uvf` command (`-i`, `-E`, `-v`,
grep-compatible exit codes, `-open`) / **searches `.gz` directly** (v1.6.6+, below) / hit-list window
(original line numbers, jump, surrounding context, save) / multi-keyword colouring (32 colour-blind-safe colours,
7 presets, `.uwvhl`) / automatic encoding detection (UTF-8, Shift-JIS, EUC-JP, UTF-16) / real-time tail /
opens gzip directly / tabs, bookmarks, horizontal scrolling, session restore / identical rendering on every OS

**Requirements**: .NET 10 / Avalonia UI 12.x / Windows, macOS, Linux

### 🗜 Searching a `.gz` is faster than `zgrep` (v1.6.6+)

```bash
uvf app.log.gz 'ERROR'          # no pipe to write
uvf app.log.gz 'ERROR' -open    # hand the hits straight to the window
```

The decompressor now runs on the OS's own zlib, which pushed it **past `gzip -dc | rg`** (i.e. `zgrep`).

| Searching a `.gz` (cold / hot) | `gzip -dc \| rg` | **`uvf`** | Ratio |
|---|---:|---:|---:|
| 3 GB of text (301 MB gz) | 1.18 s / 1.16 s | **1.16 s / 1.00 s** | 1.02× / 1.16× |
| 10 GB of text (1.12 GB gz) | 4.38 s / 4.29 s | **3.66 s / 3.38 s** | 1.20× / 1.27× |
| 50 GB of text (5.75 GB gz) | 22.06 s / 21.71 s | **17.20 s / 17.04 s** | **1.28× / 1.27×** |

Beating the `gzip` command itself comes from **not going through a pipe between processes**.

### Known limitation: extremely long lines inside a compressed file

If a `.gz` contains **a single line longer than 64 MiB** (roughly 67 million ASCII characters) and a search
matches that line, the search may stop with a message that the file "could not be read to the end". **The file is not actually damaged.**

**This will not be fixed.** Logs and XML dumps essentially never contain a 64 MiB line, and removing the limit
would require a design with no bound on line length — which **slows down every ordinary file as well**.
UwView exists to look at huge files fast, so **speed wins**.

If you have such a file, `gunzip` it first and open the plain text.

---

## More detail

| | |
|---|---|
| 📊 **All measurements and conditions** | [Benchmarks](https://uvp.y42u.net/en/benchmarks-en/) (EmEditor, klogg, 010 Editor, UltraEdit, Log Viewer, grep, ripgrep, amber, from 3 GB to 250 GB. **The numbers where we lose are published as they are.**) |
| 📖 **An honest re-measurement against klogg** | [Article](https://uvp.y42u.net/en/blog/uvp-klogg-open-lose-flow-win-en/) |
| 📖 **One 50 GB file, three arenas** | [Article](https://uvp.y42u.net/en/blog/uvp-three-arenas-50gb-en/) |
| 📖 **The window catching up with the command** | [Article](https://uvp.y42u.net/en/blog/uvp-gui-catches-up-v166-en/) |
| 📖 **ripgrep's `--no-mmap`** | [Article](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/) |
| 🔧 **Full feature list, architecture, build and test instructions** | [Previous README (as of v1.6.5)](docs/README.en-v1.6.5.md) |
| 📰 **Press kit** | [PRESSKIT.md](press-kit/PRESSKIT.md) |

> **On versions:** the timings on this page are measured on **v1.6.6.1** (the window and `uvf` are unchanged from v1.6.6;
> v1.6.6.1 changes the `uvp` command's first question to "search while building").
> → [what changed in v1.6.6](2-doc/release-body-v1.6.6.md)

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
