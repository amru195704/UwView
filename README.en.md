# UwView

*[日本語](README.md) ｜ English*

🌐 **[Official site](https://uvp.y42u.net/en/)** · 📊 **[Benchmarks](https://uvp.y42u.net/en/benchmarks-en/)** · 🧪 **[Try it in your browser](https://amru195704.github.io/UwView/)**

📥 **Download: [GitHub Releases](https://github.com/amru195704/UwView/releases/latest)** (Windows / macOS / Linux, with `SHA256SUMS`, free)

> **⚠️ About versions (as of 2026-09-19)**
> The **GUI "opening" figures (§2 below) and the 53.7 s end-to-end run from the window are measured on v1.6.6.**
> **v1.6.6 is coming shortly**; what Releases currently serves is **v1.6.5**.
> **`uvf … -open`'s 53.69 s (§3) and the CLI search speeds (§1) are what v1.6.5 already does.**
> What v1.6.6 changes is the wait **when you open from the window.**

---

## If you search with ripgrep and then open in klogg

**This tool collapses that round trip into a single pass over the file.**

When you investigate a huge log, the usual shape is: run `rg`, find the hit, then reopen the file in a viewer
to read around it. **That reads the file twice.**

`uvf` searches and hands the result to the window **in one read**.

| One 51.25 GB file, from searching to reading the hit on screen | Time |
|---|---:|
| `rg` to search (55.38 s) + klogg to open (52.55 s) | **107.9 s** — two measured figures added |
| klogg alone (open + search) | 108.14 s |
| **`uvf … -open`** | **53.69 s** |

**About 2.01×.** Not because of a cleverer algorithm, but because **the file is read once instead of twice.**
51.25 GB ÷ 53.69 s = 910 MB/s, and inside those 53.69 seconds it **searches, builds the index and puts the hits on screen.**

> Mac M4 / 32 GB / external USB SSD (raw read 950–970 MB/s). Term: `東京` (94,979 hits).
> **Every run cold, after `sudo purge`.** Full conditions and data: [benchmarks](https://uvp.y42u.net/en/benchmarks-en/).

---

## 1. Searching is level with ripgrep

`uvf` builds no index. It reads the file sequentially, once, and searches — **the same arena ripgrep plays in.**

| Seven searches, cold + the warm run right after | ripgrep 15.2.0 | **`uvf`** | Ratio |
|---|---:|---:|---:|
| 3 GB | **32.30 s** | 33.31 s | 1/1.03 (**ripgrep wins**) |
| 10 GB | 158.69 s | **147.01 s** | 1.08× |
| 50 GB | 806.22 s | **735.97 s** | 1.10× |

**We lose at 3 GB.** That size fits in RAM, so ripgrep's second run comes entirely from cache.
Past 10 GB it does not fit, and `uvf` edges ahead by 8–10%. **Both are limited by how fast the disk reads**, so this is
exactly what should happen.

**Windows is a different story.**

| Windows laptop, 16 GB, 50 GB file | ripgrep | **`uvf`** |
|---|---:|---:|
| Seven searches, total | 1,780.77 s | **693.84 s** |

**2.57×** — and that is not `uvf` being fast, it is **ripgrep's default memory-mapped read backfiring at 50 GB**.
Adding `--no-mmap` makes ripgrep 2.89× faster ([the write-up](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/)).
`uvf` does not memory-map, so it never falls into that hole. **If you run `rg` over huge files on Windows, try `--no-mmap` first.**

---

## 2. Opening is now slightly faster than klogg (v1.6.6, coming shortly)

klogg is an excellent viewer. **It opens at 930 MB/s — saturating the medium.**
Through v1.6.5 we ran at 486 MB/s, half of that. **v1.6.6 closed the gap.**

| Just opening (cold) | 3 GB | 10 GB | 50 GB |
|---|---:|---:|---:|
| klogg 24.11.0 | 3.65 s | 10.98 s | 52.55 s |
| UwView free GUI, **v1.6.5** | 5.27 s | 19.62 s | 100.6 s |
| **UwView free GUI, v1.6.6** | **2.99 s** | **10.13 s** | **50.44 s** |
| **Against klogg** | **1.22×** | **1.08×** | **1.04×** |

That is **967 / 966 / 969 MB/s** — **the same figure at all three sizes**, which is the speed of the medium itself
(3 GB fits in RAM and still reads at 967, so this is not a cached number).

**The bigger the file, the smaller the margin.** At 50 GB it is 1.04× — **level, in honest terms.**
**The gap opens up afterwards, when you search.**

---

## 3. The CLI and the window are the same engine on the same file

This is what `uvf` actually is.

```bash
uvf japan-latest.osm '東京' -open
```

**The UwView window opens the moment the search finishes, with a line-numbered list of hits.**
**The window does not search again** — the CLI hands over the byte offsets of the matching lines, the index
checkpoints and each hit's line number.

| | Passes over the file | Line numbers in the hit list |
|---|---|---|
| `rg`, then reopen in a viewer | **2** | after the viewer builds its index |
| klogg (open, then search) | **2** | after the index is done |
| **`uvf … -open`** | **1** | **from the start** |

From there it is the GUI's job: **read the lines around a hit, jump from the result list, colour several keywords at
once, drop bookmarks, change the pattern and look again.** Investigation is made of that back and forth.

**258.68 GB and 4.5 billion lines behaved the same way.** `uvf … -open` took **265.21 s**.
**klogg needs 258 s merely to finish opening that file** — so in about the time klogg takes just to open it, we have
already searched it and put the hits on screen (7 s apart, 2.8%).

> **The same holds if you open the window directly.** From v1.6.6 **the search starts without waiting for the index**,
> so open → search → hits on screen at 50 GB takes **53.7 s**, matching `uvf … -open`'s 53.69 s.
> **Know the term, start from the CLI; don't know it yet, start from the window. The wait is the same either way.**

---

## 4. All of the above is the free edition

Both `uvf` and the GUI are in the **free** build on [GitHub Releases](https://github.com/amru195704/UwView/releases/latest).
Single executables — no installer, no sign-up. Windows, macOS and Linux behave the same.

**Up to about 3 GB you don't even need the download** → [browser build](https://amru195704.github.io/UwView/)
(10.4 s to index, 5.8 s to search)

---

## 5. Coming back to the same file — UwView Pro

The boundary is not file size. It is **the second question.**

For the first question klogg and `uvf` both have to read the whole file once. That is physics; there is no way around
it. What differs is **what is left behind.**

- **klogg** — nothing is kept. **It rebuilds its index every time you open the file**
- **`uvf` (free)** — builds no index. **The second question costs the same as the first**
- **`uvp` (Pro)** — builds a `.uwvz` on the first run (about one ninth of the original, with a line index) and
  **never touches the original again**

| 50 GB, second question | Open | Search | Total |
|---|---:|---:|---:|
| klogg | 52.55 s (every time) | 55.59 s | 108.14 s |
| `uvf … -open` (free) | — | — | 53.69 s |
| **`uvp` (with `.uwvz`)** | **0.01–0.07 s** | **6.34 s** | **6.41 s** |

**16.9× klogg, and 8.4× our own free `uvf`.** This is where the order of magnitude changes.

**Stated honestly: on the first question the free `uvf` is level with ripgrep, while `uvp` is 15–20% slower because it
builds its `.uwvz` (a compressed cache plus index).** On a 3 GB file that fits in RAM, ripgrep stays ahead on the second
question too. **`uvp` pays off past 10 GB, when you ask the same file more than one question.**

A `.uwvz` is **searchable with the original deleted, and `-extract` puts it back**. 50 GB becomes about 5.7 GB.
Add **Edit Upgrade** and you can **edit without rewriting the original** — it keeps only the diff, so saving does not
depend on file size (at 48 GB, "save so you can stop for the day" takes 0 s).

> **$129 one-time / $9 per month** (Edit Upgrade +$120 / +$8), with a **14-day free trial**
> → **[UwView Pro](https://uvp.y42u.net/en/pro-en/)**

---

## Which one

| Situation | Use |
|---|---|
| Just searching, no window needed | **`uvf`** (free). Level with ripgrep |
| **Search, then read the hit** | **`uvf … -open`** (free). **The shortest path** |
| You don't know the term yet, you just want to open and look | **the UwView free GUI**. klogg is excellent too |
| **Coming back to the same file / keeping it compressed** | **[UwView Pro](https://uvp.y42u.net/en/pro-en/)** |
| **Editing** a huge file | **UwView Pro + Edit Upgrade** |
| Up to 3 GB, nothing installed | **[browser build](https://amru195704.github.io/UwView/)** |

---

## What it does

- **Huge-file viewing** — largest measured: **258.68 GB, 4,509,830,821 lines** (reached on the free edition). The file is never held in memory
- **The `uvf` command** — `-i` (ignore case), `-E` (regex), `-v` (non-matching lines), grep-compatible exit codes (0/1/2), `-open` to hand over to the GUI
- **Hit list window** — matching lines only, with their original line numbers. Double-click to jump, see the lines around a hit, save the list to a file
- **Multi-keyword colouring** — 32 colour-blind-safe colours, named sets, `.uwvhl` export, 7 presets (syslog, HTTP access, JSON, NMEA, GeoJSON, KML and more)
- **Automatic encoding detection** — BOM + UTF-8 / Shift-JIS / EUC-JP / UTF-16, switchable without rebuilding the index
- **Real-time tail** — opens logs that another process is still writing to
- **Opens gzip directly**
- **Tabs, bookmarks, horizontal scrolling, session restore**
- **Identical rendering on every OS** — Avalonia with custom Skia drawing

**Requirements**: .NET 10 / Avalonia UI 12.x / Windows, macOS, Linux (plus the browser build)

📄 **Full feature list, architecture, build instructions and test recipes are in the
[previous README (as of v1.6.5)](2-doc/archive/README.en-v1.6.5-2026-09.md).**

---

## About the measurements

Every figure here is **the same machine, the same file, and every run cold with the cache dropped.**
**Do not compare seconds across machines.** Only the ratios inside one machine mean anything.

📊 **Conditions and full data** → [benchmarks](https://uvp.y42u.net/en/benchmarks-en/)
(EmEditor, klogg, 010 Editor, UltraEdit, Log Viewer, grep, ripgrep, amber and BSD grep, from 3 GB to 250 GB.
**The numbers where UwView loses are published as they are.**)

Related: [an honest re-measurement against klogg](https://uvp.y42u.net/en/blog/uvp-klogg-open-lose-flow-win-en/)
· [the window catching up with the command (v1.6.6)](https://uvp.y42u.net/en/blog/uvp-gui-catches-up-v166-en/)
· [one 50 GB file, three arenas](https://uvp.y42u.net/en/blog/uvp-three-arenas-50gb-en/)
· [ripgrep's `--no-mmap`](https://uvp.y42u.net/en/blog/uvp-rg-no-mmap-50gb-en/)

---

## Supporting the project

**The free edition of UwView will stay free.**

Free for personal use and for internal use inside a company. If it earns its place in your work and you would like it
to keep going, you can support it through [GitHub Sponsors](https://github.com/sponsors/amru195704). **Entirely optional.**

**What helps most is not money** —

- **Telling us when it did not work** (which file, what went wrong) → [Issues](https://github.com/amru195704/UwView/issues)
- **Numbers from your own huge files** — measurements where we lose are especially welcome
- **"This file won't open"**, with as much detail as you can give

Development is funded mainly by sales of [UwView Pro](https://uvp.y42u.net/en/pro-en/).
**Buying Pro is the most direct support there is.**

---

## License

UwView is distributed under the [PolyForm Internal Use License 1.0.0](LICENSE).

- **Free for personal use and for internal business use** inside a company
- **Redistribution, bundling into a product or service, resale and supply to third parties are not permitted.**
  Those require a separate commercial (redistribution) license
- Commercial licensing enquiries: [GitHub Issues](https://github.com/amru195704/UwView/issues)

> Japanese reference translation: [LICENSEjp.txt](LICENSEjp.txt) (the English [LICENSE](LICENSE) is the binding text)

## Other products by the same author

iOS apps for surveyors and land investigators, by the same author (y4u).

| App | What it does | Links |
| --- | --- | --- |
| **GeoConverter Pro** | Coordinate conversion (geodetic / plane rectangular systems, semi-dynamic and steady-state corrections) | [App Store](https://apps.apple.com/jp/app/geoconverter-pro/id6761740960) · [Site](https://gcpro.y42u.net/) |
| **GeoPrism JP** | Visualises datum shifts and the geoid on maps and heatmaps | [App Store](https://apps.apple.com/app/id6780149823) · [Site](https://gmp.y42u.net/) |
| **GeoDiveExa** | High-precision RTK-GNSS position surveying | [Site](https://y42u.net/tec001/) |

> UwView is an independent utility and does not depend on any of the Geo products above.

---

📰 [Press kit](press-kit/PRESSKIT.md) · 📄 [Previous README (v1.6.5 — feature list and build instructions)](2-doc/archive/README.en-v1.6.5-2026-09.md)
