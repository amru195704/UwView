# UwView

*[日本語](README.md) ｜ English*

**A tool for investigating huge logs and text files. Search in the terminal, read in the window.**

📥 **[Download (free)](https://github.com/amru195704/UwView/releases/latest)** · 🌐 **[Official site](https://uvp.y42u.net/en/)** · 🧪 **[Try it in your browser](https://amru195704.github.io/UwView/)**

---

## Level with ripgrep. Level with klogg. About 2× when you join them.

**One 51.25 GB file** (Mac M4, external USB SSD, every run cold with the cache dropped)

| Task | Against | Theirs | **UwView** | |
|---|---|---:|---:|:---:|
| **Search** (CLI, one term) | ripgrep 15.2.0 | 55.54 s | **`uvf` 52.87 s** | level |
| **Open** (GUI) | klogg 24.11.0 | 52.55 s | **50.44 s** | level |
| **Search, then read the hit** | `rg` + klogg | 108.1 s | **`uvf … -open` 53.69 s** | **2.01×** |

**Searching is level with ripgrep; opening is level with klogg. The gap appears when you join the two.**

**All of that is the free edition.**

`rg` then reopening in a viewer **reads the file twice.** `uvf … -open` reads it **once.**

> The 108.1 s for `rg` + klogg is two measured figures added (55.54 s + 52.55 s). klogg alone (open + search) is also 108.14 s.
> Term: `東京` (94,979 hits). **[Conditions and full data →](https://uvp.y42u.net/en/benchmarks-en/)**

### By size

| | 3 GB | 10 GB | 50 GB |
|---|---:|---:|---:|
| ripgrep 15.2.0 (search, one term) | 3.29 s | **11.38 s** | 55.54 s |
| klogg 24.11.0 (open) | 3.65 s | 10.98 s | 52.55 s |
| **UwView CLI (`uvf`, search)** | **3.26 s** | 11.41 s | **52.87 s** |
| **UwView GUI (open)** | **2.99 s** | **10.13 s** | **50.44 s** |

**Every row is one pass over the file.** None of these tools keeps an index, so the disk's read speed is the limit.

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
**From the command finishing to the list being on screen: 0.18 s** (measured at 50 GB) — the window only has to lay out
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
| You just want to open it and look | **the UwView window** (free) |
| **Coming back to the same file / keeping it compressed** | **[UwView Pro](https://uvp.y42u.net/en/pro-en/)** |
| **Editing** a huge file | **UwView Pro + Edit Upgrade** |
| Up to 3 GB, nothing installed | **[browser build](https://amru195704.github.io/UwView/)** |

**Both `uvf` and the GUI are in the free build.** Single executables, no installer, same on Windows, macOS and Linux.

### UwView Pro — the order of magnitude changes at the second question

`uvp` builds a `.uwvz` on the first run (about one ninth of the original, with a line index) and **never touches the
original again.** Searching 50 GB takes **6.34 s**; opening it a second time takes **0.01–0.07 s**.
**It stays searchable with the original deleted, and `-extract` puts it back.**

**On the first question the free `uvf` is level with ripgrep, while `uvp` is 15–20% slower because it builds its `.uwvz`.**
**`uvp` pays off past 10 GB, when you ask the same file more than one question.**

> **$129** one-time / **$9** per month (Edit Upgrade +$120 / +$8), with a **14-day free trial**
> → **[UwView Pro](https://uvp.y42u.net/en/pro-en/)**

---

## What it does

**Largest measured: 258.68 GB, 4.5 billion lines** (on the free edition) / the `uvf` command (`-i`, `-E`, `-v`,
grep-compatible exit codes, `-open`) / hit-list window (original line numbers, jump, surrounding context, save) /
multi-keyword colouring (32 colour-blind-safe colours, 7 presets, `.uwvhl`) / automatic encoding detection
(UTF-8, Shift-JIS, EUC-JP, UTF-16) / real-time tail / opens gzip directly / tabs, bookmarks, horizontal scrolling,
session restore / identical rendering on every OS

**Requirements**: .NET 10 / Avalonia UI 12.x / Windows, macOS, Linux

---

## More detail

| | |
|---|---|
| 📊 **All measurements and conditions** | [Benchmarks](https://uvp.y42u.net/en/benchmarks-en/) (EmEditor, klogg, 010 Editor, UltraEdit, Log Viewer, grep, ripgrep, amber, from 3 GB to 250 GB. **The numbers where we lose are published as they are.**) |
| 📖 **An honest re-measurement against klogg** | [Article](https://uvp.y42u.net/en/blog/uvp-klogg-open-lose-flow-win-en/) |
| 📖 **One 50 GB file, three arenas** | [Article](https://uvp.y42u.net/en/blog/uvp-three-arenas-50gb-en/) |
| 📖 **The window catching up with the command** | [Article](https://uvp.y42u.net/en/blog/uvp-gui-catches-up-v166-en/) |
| 📖 **ripgrep's `--no-mmap`** | [Article](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/) |
| 🔧 **Full feature list, architecture, build and test instructions** | [Previous README (as of v1.6.5)](2-doc/archive/README.en-v1.6.5-2026-09.md) |
| 📰 **Press kit** | [PRESSKIT.md](press-kit/PRESSKIT.md) |

> **On versions:** the "open, 50.44 s" figure above is measured on **v1.6.6 (coming shortly)**. `uvf … -open`'s 53.69 s
> and the CLI search speeds are what the currently shipping **v1.6.5** already does.
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

## Other products by the same author

iOS apps for surveyors and land investigators by the same author (y4u), independent of UwView:
**[GeoConverter Pro](https://gcpro.y42u.net/)** (coordinate conversion) · **[GeoPrism JP](https://gmp.y42u.net/)** (visualising datum shifts) · **[GeoDiveExa](https://y42u.net/tec001/)** (RTK-GNSS surveying)
