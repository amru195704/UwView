*日本語 ｜ [English](#uwview-v17369--wide-field-english)*

## UwView v1.7.3.6.9 — Wide Field

**キャッシュに載ったファイルの検索（2回目）が、ripgrep より速くなりました。小さなファイルが大量にある場所の2回目も、約 1.7 倍速くなりました（5種類の検索の合計 16.90秒 → 9.72秒）。**
mac・Windows・Linux の3つの OS でテスト一式を流し、**結果の不一致は 0 件、総合は3つとも `uvf`／`uvp` が速い**結果でした。画面の使い方と出力の形は変わりません。
（前回の無料版は v1.7.3.6.7 です → [v1.7.3.6.7 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.7.md)）

### ⚡ 2回目（hot）の検索が ripgrep 以上に

1本のファイルを続けてもう一度探すと、前の版は ripgrep より 1.3〜1.4倍遅いままでした（1回目は前から ripgrep より速い）。

| 2回目（hot）・「東京」 | ripgrep との比 前の版（1.7.3.6.7） | **v1.7.3.6.9** |
|---|---|---|
| 3GB | 1/1.36（ripgrep が速い） | **1.07（`uvf` が速い）**　0.29秒 対 0.31秒 |
| 10GB | 1/1.37（ripgrep が速い） | **1.10（`uvf` が速い）**　0.88秒 対 0.97秒 |

**遅かったのは「探す」ではなく「行番号のために改行を数える」ところでした。** 当たりが 0 件でも、行番号を出すために読んだバイトの改行を全部数えます。この数え方が 3GB で 0.15秒かかり、2回目の検索全体（0.38秒）の4割を占めていました。今の版は、16バイトずつ改行と比べた結果をそのまま足し込み、まとめて合計します。同じ 3GB で 0.028秒（5倍速い）になりました。

- 7種類の検索（素の検索・`-i`・`-E`・行頭つき・`-E -i`・`-v`・`-E -v`）の2回目の合計は、3GB で ripgrep 7.60秒・`uvf` 6.31秒、10GB で 12.58秒・10.61秒です。
- 1回目（ディスクから読む）は変わりません。行番号は1行もずれません（ripgrep と全出力を突き合わせています）。

### 📂 小さなファイルが大量にある場所の2回目が、約 1.7 倍速く

```bash
uvf 'src/linux/**' EXPORT_SYMBOL -H
```

| Linux カーネルのソース 8.6万本・5種類の検索の合計（秒） | 1回目（cold） | 2回目（hot） | 合計 | ripgrep との比（合計） |
|---|---:|---:|---:|---|
| ripgrep 15.2.0 | 22.76 | 7.47 | 30.22 | — |
| `uvf` 1.7.3.6.7 | — | — | 52.18 | 1/1.66 |
| `uvf` 1.7.3.6.8 | — | 16.90 | — | 2回目 1/2.26 |
| **`uvf` v1.7.3.6.9** | **26.49** | **9.72** | **36.21** | **1/1.20（同等）** |

（1.7.3.6.7 の合計は 10月3日、1.7.3.6.8・1.7.3.6.9 は 10月4日の測定です。ripgrep は 10月4日の値）

- **探す人の数を決めて、空いた人が次のファイルを取りに行く形にしました。** 前の版は、前のファイルの出力が出るのを待つ間も、探す枠を持ったままでした。出力は今までどおりファイルの順に並びます。
- **ファイルを開くときに、絶対パスを渡すようにしました。** 相対パスだと、開くたびに「今いるフォルダー」を OS に問い合わせていました（8並列で開いて読むだけで 2.33秒 → 1.59秒）。
- **256KB 以下のファイルは1回で読み切り、**文字コードの判定・中身の確かめ・検索を、その読んだ中から行います。
- UTF-8 かどうかの確かめで、英数字（ASCII）の部分をまとめて読み飛ばします（判定の規則は同じです）。
- まだ ripgrep が少し速いものの、1.5倍未満で**同等の範囲**に入りました。同じ場所を何度も探すなら、索引を持つ UwView Pro（下）が向いています。

### 🌏 mac・Windows・Linux の3つで測りました

同じテスト一式（入力の種類 66件・複数ファイル 36件・除外 43件と、速さの比較 96項目）を、3台で流しました。**正しさのテストは3つとも合格、速さの比較でも結果の不一致は 0 件**です。

| ripgrep との比（1超＝`uvf`／`uvp` が速い） | mac | Windows | Linux |
|---|---|---|---|
| 機械 | Apple M4・10コア・32GB | x64・8論理コア・16GB | aarch64・**2論理コア**・7GB |
| `uvf` 大きい1本・7つの検索の合計（3GB／10GB／50GB） | 1.00／1.16／1.13 | 1.48／1.90／**2.47** | 1.31／1.13／1.33 |
| `uvf` 複数ファイル（平文5本／gz 7本／混在12本） | 1.02／1.31／1.29 | 1.86／1.54／1.58 | 2.22／1.67／1.67 |
| **総合 96項目（`uvf`＋`uvp`）** | **2.49** | **3.42** | **1.88** |

- **Windows と Linux では、mac より差が大きく出ました。** Windows は ripgrep の1回目が遅く、`uvf` は 50GB の7つの検索で 2.47倍。7パターン × 3サイズのすべてで `uvf` が速い結果でした。
- Linux（2コア）では、`rg -z` が外部の展開コマンドを呼ぶ分が効き、gz で `uvf` が約 2倍速い結果でした。
- Windows・Linux は 1.7.3.6.8 で測りました（1.7.3.6.9 との違いは小さなファイルが大量にある場所の検索だけで、この表の項目にはほとんど効きません）。

### 🔧 そのほかの変更

- 調べるための環境変数を足しました（`UV_TRACE=1` で読む・探す・数える・出すの内訳、`UVF_NO_HANDOFF=1` など）。通常の動きには影響しません。
- 出力は1バイトも変えていません。テストはすべて通っています。

### ⚠️ 注意

- **Windows では、圧縮ファイルの一部の形式で ripgrep の方が速い結果でした。** 1回目＋2回目の合計で、xz（3GB で 1/1.69）・zst（1GB で 1/1.61）・lz4（1GB で 1/2.09、3GB で 1/1.51）。bz2・混在も 1/1.2〜1/1.4 です。mac と Linux では、この形式も同等以上でした。原因は調べている途中です（Windows 版が起動の速い形式〈NativeAOT〉でないこと、テスト機で `uvf` のスレッドが設定で 4本になっていたこと、を疑っています）。
- **小さなファイルが大量にある場所では、まだ ripgrep が少し速い**です（上の表、1/1.20。同等の範囲）。
- 1文字の `k` を大小無視で探すような、目印にできる部分が無い検索は速くなっていません。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- mac の秒数は、ある1台の Mac（Apple M4・10コア・メモリ 32GB・外付け USB SSD）で 2026年10月4日に測った値です。cold はキャッシュを捨てて（`sudo purge`）10秒待ってからの1回目、hot は続けて走らせた2回目です。Windows・Linux は 10月3日の測定です。**秒数を環境をまたいで比べないでください**（3つの OS の表は、ripgrep との比だけを並べています）。

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
```

Linux の場合と、手で入れる場合は、下のファイルを使ってください。チェックサムは `SHA256SUMS-1.7.3.6.9.txt` にあります。

```
1b8cef78bf64d24b41c3dc4d6f002fa5e27961d1a2a9fcb69e33f9942d78db47  UwView-1.7.3.6.9-linux-aarch64.tar.gz
951ae7711af5bf556644d050b0cb668f0bbd94f4d58358ae735d4d4e3518dbcb  UwView-1.7.3.6.9-linux-x86_64.tar.gz
dcca0487727872adfff8d07bfb86e5b170c8da41f8c60359b868ec13db6d5635  UwView-1.7.3.6.9-mac-arm64.dmg
a2bdb293fd6a272f3b81083d0ec34010ba5924b50d626c0010691dc29da33443  UwView-1.7.3.6.9-mac-x64.dmg
30d8ff9cd7455c86ca1acd4c9d55fd8cebfce5180684a29c8de7fe3f0592107d  UwView-1.7.3.6.9-win-arm64.zip
7c33a8a01173a3df31406cd98df25247920348acf184cb55d48487a4edd1b4eb  UwView-1.7.3.6.9-win-x64.zip
```

> **配布は GitHub Releases のみです。** Homebrew と Scoop も、ここからファイルを取得して SHA256 を照合します。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 3つの OS とも、大きいファイルで ripgrep の 1.6〜3.7倍

有償版の `uvp` にも、同じ改行の数え方が入りました（束ねるときの行数の数え方も同じものです）。

| `uvp`・7つの検索の合計（cold＋hot・索引作りを含む）・ripgrep との比 | mac | Windows | Linux |
|---|---|---|---|
| 3GB | 2.39 | 2.22 | 1.62 |
| 10GB | 3.28 | 2.81 | 2.45 |
| 50GB | **3.74** | **3.44** | 1.99 |

- mac の 50GB では、ripgrep 870.64秒に対し `uvp` 232.98秒でした。
- Linux カーネルのソース 8.6万本では、1本の `.uwvz` に束ねる1問目が 9.18秒、2問目からは 0.27〜0.31秒。5種類の検索の合計で ripgrep の **2.48倍**速い結果でした（mac）。
- 圧縮ファイルの2回目は、元の形式によらず 3GB で 0.30〜0.32秒です（mac）。
- Linux（2コア）では、展開の速い zst・lz4 と平文で、索引を作る1回目が重く、2回の合計で ripgrep の 1/1.2〜1/1.4 でした。3回目からは `uvp` が速くなります。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.7.3.6.9 — Wide Field (English)

**Searching a file that is already in the cache (the second run) is now faster than ripgrep. On many small files, the second run is about 1.7× faster (five searches: 16.90 s → 9.72 s).**
The full test suite was run on three operating systems — macOS, Windows and Linux. **Zero mismatched results, and `uvf`/`uvp` came out faster overall on all three.** The window and the output format have not changed.
(The previous free release was v1.7.3.6.7 → [what changed in v1.7.3.6.7](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.7.md#uwview-v17367--wide-field-english))

### ⚡ Second (hot) searches now at or above ripgrep

Searching the same file again, the previous version was still 1.3–1.4× slower than ripgrep (the first run was already faster).

| Second run (hot), `東京` | vs ripgrep, previous (1.7.3.6.7) | **v1.7.3.6.9** |
|---|---|---|
| 3 GB | 1/1.36 (ripgrep faster) | **1.07 (`uvf` faster)** — 0.29 s vs 0.31 s |
| 10 GB | 1/1.37 (ripgrep faster) | **1.10 (`uvf` faster)** — 0.88 s vs 0.97 s |

**The slow part was not the search but counting newlines for line numbers.** Even with zero hits, `uvf` counts every newline it reads so it can print line numbers. That count took 0.15 s on 3 GB — 40% of the whole second search (0.38 s). It now compares 16 bytes at a time against the newline and adds the results up in place, summing them in batches: 0.028 s on the same 3 GB (5× faster).

- Second-run totals of the seven searches (plain, `-i`, `-E`, anchored, `-E -i`, `-v`, `-E -v`): 3 GB ripgrep 7.60 s vs `uvf` 6.31 s; 10 GB 12.58 s vs 10.61 s.
- The first run (reading from disk) is unchanged. Line numbers do not shift by a single line (the full output is checked against ripgrep).

### 📂 Many small files: the second run is about 1.7× faster

```bash
uvf 'src/linux/**' EXPORT_SYMBOL -H
```

| Linux kernel source, 86,000 files, five searches (seconds) | First (cold) | Second (hot) | Total | vs ripgrep (total) |
|---|---:|---:|---:|---|
| ripgrep 15.2.0 | 22.76 | 7.47 | 30.22 | — |
| `uvf` 1.7.3.6.7 | — | — | 52.18 | 1/1.66 |
| `uvf` 1.7.3.6.8 | — | 16.90 | — | second run 1/2.26 |
| **`uvf` v1.7.3.6.9** | **26.49** | **9.72** | **36.21** | **1/1.20 (on par)** |

(The 1.7.3.6.7 total was measured on 3 October; 1.7.3.6.8, 1.7.3.6.9 and ripgrep on 4 October.)

- **A fixed number of workers now each take the next file when they are free.** Before, a worker kept its slot while waiting for the previous file's output to be written. Output is still printed in file order.
- **Files are opened with absolute paths.** With a relative path, .NET asked the OS for the current folder on every open (opening and reading with 8 workers: 2.33 s → 1.59 s).
- **Files of 256 KB or less are read in one go**, and encoding detection, the content check and the search all work from that buffer.
- The UTF-8 check skips ASCII runs in bulk (the rules are the same).
- ripgrep is still slightly faster, but within 1.5×, so **on par**. If you search the same place again and again, UwView Pro with its index (below) is the better fit.

### 🌏 Measured on macOS, Windows and Linux

The same suite (66 input-type tests, 36 multi-file tests, 43 exclusion tests and 96 speed comparisons) was run on three machines. **All correctness tests passed on all three, and the speed runs had zero mismatched results.**

| vs ripgrep (above 1 = `uvf`/`uvp` faster) | macOS | Windows | Linux |
|---|---|---|---|
| Machine | Apple M4, 10 cores, 32 GB | x64, 8 logical cores, 16 GB | aarch64, **2 logical cores**, 7 GB |
| `uvf`, one large file, seven searches (3 GB / 10 GB / 50 GB) | 1.00 / 1.16 / 1.13 | 1.48 / 1.90 / **2.47** | 1.31 / 1.13 / 1.33 |
| `uvf`, many files (5 plain / 7 gz / 12 mixed) | 1.02 / 1.31 / 1.29 | 1.86 / 1.54 / 1.58 | 2.22 / 1.67 / 1.67 |
| **Overall, 96 items (`uvf` + `uvp`)** | **2.49** | **3.42** | **1.88** |

- **The gap is larger on Windows and Linux than on the Mac.** On Windows ripgrep's first run is slow; `uvf` is 2.47× faster over the seven 50 GB searches, and faster in every one of the 7 patterns × 3 sizes.
- On Linux (2 cores), `rg -z` pays for calling external decompressors, and `uvf` is about 2× faster on gz.
- Windows and Linux were measured with 1.7.3.6.8 (1.7.3.6.9 only changes searching many small files, which barely affects the items in this table).

### 🔧 Other changes

- New environment variables for investigation (`UV_TRACE=1` prints the time spent reading, searching, counting and writing; `UVF_NO_HANDOFF=1` and others). Normal behavior is unaffected.
- The output has not changed by a single byte. All tests pass.

### ⚠️ Notes

- **On Windows, ripgrep was faster for some compressed formats.** First plus second run: xz (1/1.69 on 3 GB), zst (1/1.61 on 1 GB), lz4 (1/2.09 on 1 GB, 1/1.51 on 3 GB). bz2 and mixed sets are 1/1.2–1/1.4. On macOS and Linux these formats are on par or better. We are still looking into it (suspects: the Windows build is not NativeAOT, and the test machine had `uvf` set to 4 threads).
- **On many small files, ripgrep is still slightly faster** (1/1.20 in the table above — on par).
- Ignore-case searches with nothing to anchor on, such as the single letter `k` with `-i`, are not faster.
- The free edition does not read `.zip`, `.tar.gz` or OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- Mac timings come from one Mac (Apple M4, 10 cores, 32 GB memory, external USB SSD), measured on 4 October 2026. Cold is the first run after dropping the cache (`sudo purge`) and waiting 10 s; hot is the run straight after. Windows and Linux were measured on 3 October. **Do not compare seconds across machines** (the three-OS tables show only the ratio to ripgrep).

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
```

On Linux, or to install by hand, use the files below. Checksums are in `SHA256SUMS-1.7.3.6.9.txt`.

```
1b8cef78bf64d24b41c3dc4d6f002fa5e27961d1a2a9fcb69e33f9942d78db47  UwView-1.7.3.6.9-linux-aarch64.tar.gz
951ae7711af5bf556644d050b0cb668f0bbd94f4d58358ae735d4d4e3518dbcb  UwView-1.7.3.6.9-linux-x86_64.tar.gz
dcca0487727872adfff8d07bfb86e5b170c8da41f8c60359b868ec13db6d5635  UwView-1.7.3.6.9-mac-arm64.dmg
a2bdb293fd6a272f3b81083d0ec34010ba5924b50d626c0010691dc29da33443  UwView-1.7.3.6.9-mac-x64.dmg
30d8ff9cd7455c86ca1acd4c9d55fd8cebfce5180684a29c8de7fe3f0592107d  UwView-1.7.3.6.9-win-arm64.zip
7c33a8a01173a3df31406cd98df25247920348acf184cb55d48487a4edd1b4eb  UwView-1.7.3.6.9-win-x64.zip
```

> **Distribution is GitHub Releases only.** Homebrew and Scoop download from here and check the SHA256. Guides and news are on the blog (https://uvp.y42u.net/en/).

### 📣 On the UwView Pro side — 1.6–3.7× ripgrep on large files, on all three systems

The paid `uvp` gets the same newline counting (also used when it bundles files).

| `uvp`, seven searches, cold + hot (including building the index), vs ripgrep | macOS | Windows | Linux |
|---|---|---|---|
| 3 GB | 2.39 | 2.22 | 1.62 |
| 10 GB | 3.28 | 2.81 | 2.45 |
| 50 GB | **3.74** | **3.44** | 1.99 |

- On the Mac at 50 GB: ripgrep 870.64 s, `uvp` 232.98 s.
- Linux kernel source, 86,000 files: the first search bundles everything into one `.uwvz` in 9.18 s, later searches take 0.27–0.31 s. Five searches combined: **2.48× faster than ripgrep** (Mac).
- Compressed files: the second search takes 0.30–0.32 s on 3 GB whatever the original format (Mac).
- On Linux (2 cores), for fast-to-decompress zst and lz4 and for plain text, the first run (building the index) is heavy, and two runs combined come to 1/1.2–1/1.4 of ripgrep. From the third search on, `uvp` is faster.

→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)
