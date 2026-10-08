*日本語 ｜ [English](#uwview-v1819--finder-scope-english)*

## UwView v1.8.1.9 — Finder Scope

**画面に「コマンドライン」が入りました。`uvf`・`uvp` と同じ書き方で、画面から検索できます。** 結果は本体の窓に出て、そのまま読み進められます。ターミナルから `-open` で画面に渡した結果を、画面で検索し直すこともできます。
UwView Pro には、複数行のブロック（`-seq … -range`）・伏せ字（`-mask`）・整形して収集（`-format`）が入りました。
（前の安定版は v1.7.3.6.9 です。プレリリースの v1.7.3.7 の変更も、すべて含みます → [v1.7.3.7 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.7.md)）

### 🖥 画面の「コマンドライン」

ツールバーの「コマンドライン」ボタン（Ctrl＋Shift＋K、Mac は Cmd＋Shift＋K）でダイアログを開きます。欄は 2 つです。

| 欄 | 入れるもの | 例 |
|---|---|---|
| ファイル指定 | コマンドのファイルの部分 | `japan.osm`　`'*.osm,*.gz'`　`'linux/**'` |
| 検索パターン | それより後ろ | `東京 -i`　`'K="NAME[^"]*"' -E -i` |

- 2 つの欄から組み立てた **「コマンドの行」** を下に出します。［コピー］でターミナルに貼れば、同じ結果になります（Windows は PowerShell 向けのコピーもあります）。
- 欄に入れた文字は、その OS のターミナルと同じに引数に分けます。**Windows は PowerShell と同じ**（`\` は文字のまま、`` ` `` で次の 1 文字を逃がす、`'…'` の中の `''` は `'` 1 つ）、Mac・Linux は bash・zsh と同じです。Windows で引用なしの `\d+ -E` は、PowerShell で打ったときと同じ意味になります。どの OS でも同じに書くなら、正規表現は `'…'` で囲んでください。
- ［検索実行］で探し、結果を本体の窓に出します（1 本のファイルなら開いて結果一覧に、複数のファイルなら複数ファイルの結果に）。
- ［ファイル展開］で、探す前に対象のファイルの一覧（本数・大きさ・`.gitignore` などで除外した本数）を確かめられます。
- 書き方の誤りは、コマンドと同じ言葉で出して、実行しません。無料版で UwView Pro の機能（`-uniq` など）を書いたときは「UwView Pro の機能です」と出します。
- 実行中は進み具合（何本目・読んだ量・残りの目安）を出し、［中止］で止められます。終わると所要時間を下の行に出します。
- 基準フォルダー（コマンドを打つフォルダーにあたる所）は、`uvf … -open` で画面を開いたときはターミナルのフォルダー、画面から開いたときは前に使ったフォルダーが最初から入っています。
- 入れた 2 つの欄と基準フォルダーは履歴に残り、▼ から選び直せます。
- `--help`・`--version`・`--files` のように文字で答えるものは、ダイアログの「出力」に出します。［保存…］で書き出せます。

### ⚡ 速くなったところ

| 何が | 前 | **v1.8.1.9** |
|---|---:|---:|
| ヒットした行の書き出し（3GB・`\d+\.\d{6,}`・`-H`） | 4.70 秒 | **2.68 秒** |
| 肯定の先読み `(?=…)` を使う式（Linux カーネル 6 万本・`^(?=.*User: …)…`） | 1.5 秒 | **0.6 秒** |
| `**` で多数のフォルダーを広げる（6 万本） | 0.33 秒 | **0.24 秒** |

- ヒットした行の書き出しは、行ごとに文字列を作るのをやめました（使うメモリ 28GB → 0.4GB）。
- 先読み `(?=…)` の中に必ず出てくる語で、先に候補の行を絞ります。Mac で候補の行がとても多い 1 本のファイル（1GB で 370 万行）は、正規表現をコンパイルできる本体に任せます（4.7 秒 → 2.2 秒）。
- 出力は 1 バイトも変えていません。テストはすべて通っています。

### ⚠️ 注意

- 画面の「コマンドライン」から検索した結果は、本体の窓に出します。ダイアログの「出力」には出ません（`-open` を付けたコマンドと同じ動きです）。文字で欲しいときは［保存…］で書き出してください。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- 秒数は、ある 1 台の Mac（Apple M4・10 コア・メモリ 32GB・外付け USB SSD）で 2026 年 10 月 6〜7 日に測った値です。**秒数を環境をまたいで比べないでください。**

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
sudo snap install uwview                         # Linux（更新は snap refresh uwview）
```

手で入れる場合は、このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。チェックサムは `SHA256SUMS-1.8.1.9.txt` にあります。

```
cb084cbadb2fe7442a2311036c588674cff5ffabb254e97ee6019822cd9e9b1d  UwView-1.8.1.9-linux-aarch64.tar.gz
b18e58264ba4bea5a4160baaf6a23c2e3dbe969ded55ae67a9dce40aff884aee  UwView-1.8.1.9-linux-x86_64.tar.gz
d98bb2dcfdbab175763af3dabd523356be0da10a1809d6e0f095ce3c26e44fb1  UwView-1.8.1.9-mac-arm64.dmg
bc7f89ff811f2a7a89d7681be17925095b8bf8fb15f14b055997a8bed8c05f47  UwView-1.8.1.9-mac-x64.dmg
040d5465a0ed0189f7e4787016043438ed0e64e76f35d94d0044b762907b7545  UwView-1.8.1.9-win-arm64.zip
077db178b5bc11eafb0479b24b4ab5b70580340cd3818cb4d7c0c3f404a2ede2  UwView-1.8.1.9-win-x64.zip
（2026-10-07 21:00 作成・Linux 版は 2026-10-08 に作り直し。macOS 版は署名・公証済み。ビルド番号はすべて 26.10.07.20（日本時間）です）
```

> **配布は GitHub Releases が元です。** Homebrew・Scoop・Snap も、ここのファイルを使います（Homebrew と Scoop は SHA256 を照合します）。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。コマンドと画面の対比は記事「ripgrep・uvf・uvp 検索コマンド対比表」にまとめています。

### 📣 UwView Pro 側 — 複数行のブロック・伏せ字・整形、画面で束ねる

有償版の `uvp` と UwView Pro の画面には、次のものが入りました。

- **複数行のブロック**：`-seq '開始,終了' -range` で、開始の行から終了の行までをひとまとまりにして取り出し、`-grep` でブロックの中を絞ります（`-v` で含まないものを残す）。
- **伏せ字**：`-mask '正規表現' 置き換え` で、出す行の一部（IP アドレスなど）を伏せます。
- **整形して収集**：`-format '$1,$2,$3' --csv` で、正規表現で取り出した値を CSV などの形に並べます。
- **画面の「コマンドライン」**でも同じ書き方が使えます。結果は「多段階・シーケンシャル検索」の窓に、段のタブと「集計／並べ替え・順序／伏せる・形」のタブとして再現します（`uvp … -open` でも同じ）。
- **画面で束ねる**：ダイアログのファイル指定に `'*.log'` のように書けば、`uvp` と同じに 1 本の索引に束ねて開きます。束ねる前に、そのまま使う・足す・作り直す・新しく作るのどれになるかと、かかる時間の目安を出します。フォルダーのドロップや、［開く］で 2 本以上を選んだときも、ここから束ねられます。
- `-out` の書き出し先が既にあるときは上書きしてよいか尋ねる、`cat`・置換の結果を新しいタブで開く、なども画面から使えます。

ログの調査の 3 つの例（範囲＋除外＋伏せ字・近い行の AND と NOT・CSV への収集）で比べた結果です（cold＋hot の合計で、相手の時間 ÷ uvp の時間。uvp と ripgrep の結果は全件で同じ）。

| 対象 | 例 | ripgrep（Mac） | ripgrep（Windows） | PowerGREP（Windows） |
|---|---|---:|---:|---:|
| Linux カーネル 6 万本 | 範囲＋除外＋伏せ字 | 1.87 倍 | 1.85 倍 | 20.26 倍 |
| Linux カーネル 6 万本 | 近い行の AND・NOT | 2.14 倍 | 1.65 倍 | 22.92 倍 |
| Linux カーネル 6 万本 | CSV への収集 | 3.53 倍 | 2.39 倍 | 42.94 倍 |
| 1GB の 1 本 | 範囲＋除外＋伏せ字 | 1.07 倍 | 1/1.03 倍（同等） | 9.07 倍 |
| 1GB の 1 本 | 近い行の AND・NOT | 1.46 倍 | 1.09 倍 | 8.62 倍 |
| 1GB の 1 本 | CSV への収集 | 1.92 倍 | 2.27 倍 | 38.50 倍 |

- 1 を超えると uvp が速いことを表します。ripgrep の列は、同じことを ripgrep と perl などを組み合わせて行った時間です。PowerGREP は同じ式の検索の時間だけです（収集・伏せ字・除外はしていません）。
- Windows で Linux カーネル 6 万本を初めて探すとき（索引を作る 1 回目）は 65〜74 秒かかります（Mac は 6〜9 秒）。2 回目からは索引を使います。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.8.1.9 — Finder Scope (English)

*[日本語](#uwview-v1819--finder-scope) ｜ English*

**The window now has a Command Line. You can search from the window with the same arguments as `uvf` and `uvp`.** The results appear in the main window, ready to read on. Results handed to the window from the terminal with `-open` can be searched again in the window.
UwView Pro adds multi-line blocks (`-seq … -range`), masking (`-mask`) and formatted collection (`-format`).
(The previous stable release is v1.7.3.6.9. Everything in the v1.7.3.7 pre-release is included → [what changed in v1.7.3.7](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.7.md))

### 🖥 Command Line in the window

Open the dialog with the **Command Line** button on the toolbar (Ctrl+Shift+K, Cmd+Shift+K on a Mac). It has two fields.

| Field | What goes in | Examples |
|---|---|---|
| File pattern | the file part of the command | `japan.osm`　`'*.osm,*.gz'`　`'linux/**'` |
| Search pattern | everything after it | `東京 -i`　`'K="NAME[^"]*"' -E -i` |

- Below them, the dialog shows **the command line** built from the two fields. **Copy** it into a terminal and you get the same result (on Windows there is also a copy for PowerShell).
- The fields are split into arguments the way your terminal does it: **PowerShell rules on Windows** (`\` stays as it is, `` ` `` escapes the next character, `''` inside `'…'` is one `'`), and bash/zsh rules on macOS and Linux. So `\d+ -E` typed without quotes means the same as in PowerShell on Windows. To use the same text on every OS, put regular expressions in `'…'`.
- **Run** searches and shows the results in the main window (one file opens with its result list; several files appear as a multi-file result).
- **Expand Files** lists the target files before searching (count, size, and how many were excluded by `.gitignore` and the like).
- Mistakes are reported in the same words as the command, and nothing runs. In the free edition, a UwView Pro option (such as `-uniq`) is reported as “a UwView Pro feature”.
- While it runs, it shows progress (which file, how much has been read, time left) and can be stopped with **Cancel**. When done, the time taken appears on the bottom line.
- The base folder (where the command would be typed) starts as the terminal's folder when the window was opened with `uvf … -open`, and as the last folder you used when opened from the window.
- The two fields and the base folder are kept in a history; pick them again from ▼.
- Things that answer in text, such as `--help`, `--version` and `--files`, appear in the dialog's **Output**. **Save…** writes them out.

### ⚡ Faster

| What | Before | **v1.8.1.9** |
|---|---:|---:|
| Writing out matching lines (3 GB, `\d+\.\d{6,}`, `-H`) | 4.70 s | **2.68 s** |
| Expressions with a positive lookahead `(?=…)` (Linux kernel, 60,000 files, `^(?=.*User: …)…`) | 1.5 s | **0.6 s** |
| Expanding many folders with `**` (60,000 files) | 0.33 s | **0.24 s** |

- Matching lines are no longer turned into a string one by one (memory used: 28 GB → 0.4 GB).
- A word that must appear inside a lookahead `(?=…)` now narrows the candidate lines first. On a Mac, a single file with a very large number of candidate lines (3.7 million lines in 1 GB) is handed to the main app, which can compile regular expressions (4.7 s → 2.2 s).
- Output is byte-for-byte unchanged. All tests pass.

### ⚠️ Notes

- A search run from the Command Line shows its results in the main window, not in the dialog's **Output** (the same as a command with `-open`). To get the text, use **Save…**.
- Not handled by the free edition: `.zip`, `.tar.gz` and OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- Times were measured on one Mac (Apple M4, 10 cores, 32 GB, external USB SSD) on 6–7 October 2026. **Do not compare times across environments.**

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
sudo snap install uwview                         # Linux (update: snap refresh uwview)
```

To install by hand, get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). Checksums are in `SHA256SUMS-1.8.1.9.txt`.

```
cb084cbadb2fe7442a2311036c588674cff5ffabb254e97ee6019822cd9e9b1d  UwView-1.8.1.9-linux-aarch64.tar.gz
b18e58264ba4bea5a4160baaf6a23c2e3dbe969ded55ae67a9dce40aff884aee  UwView-1.8.1.9-linux-x86_64.tar.gz
d98bb2dcfdbab175763af3dabd523356be0da10a1809d6e0f095ce3c26e44fb1  UwView-1.8.1.9-mac-arm64.dmg
bc7f89ff811f2a7a89d7681be17925095b8bf8fb15f14b055997a8bed8c05f47  UwView-1.8.1.9-mac-x64.dmg
040d5465a0ed0189f7e4787016043438ed0e64e76f35d94d0044b762907b7545  UwView-1.8.1.9-win-arm64.zip
077db178b5bc11eafb0479b24b4ab5b70580340cd3818cb4d7c0c3f404a2ede2  UwView-1.8.1.9-win-x64.zip
(built 2026-10-07 21:00 JST, Linux builds rebuilt on 2026-10-08; the macOS builds are signed and notarized. Build number: 26.10.07.20 (JST) for all builds)
```

> **GitHub Releases is the source.** Homebrew, Scoop and Snap use the files here (Homebrew and Scoop check the SHA256). Instructions and news are on the blog (https://uvp.y42u.net/). The article “ripgrep, uvf and uvp search commands side by side” compares the commands and the window.

### 📣 On the UwView Pro side — multi-line blocks, masking, formatting, bundling in the window

The paid `uvp` and the UwView Pro window add:

- **Multi-line blocks**: `-seq 'start,end' -range` takes everything from a start line to an end line as one block, and `-grep` narrows inside the blocks (`-v` keeps the ones without it).
- **Masking**: `-mask 'regex' replacement` hides part of each output line (IP addresses and the like).
- **Formatted collection**: `-format '$1,$2,$3' --csv` arranges values captured by a regular expression into CSV and similar shapes.
- The same arguments work in the window's **Command Line**. The result is rebuilt in the **Multi-stage / sequential search** window as stage tabs and the **Tally / Sort & order / Mask & format** tabs (the same with `uvp … -open`).
- **Bundling in the window**: write `'*.log'` in the dialog's file field and it bundles the files into one index and opens it, just like `uvp`. Before bundling it says whether it will reuse, add to, rebuild or newly create the index, and roughly how long it will take. Dropping a folder, or choosing two or more files with **Open**, also leads there.
- From the window you can also be asked before `-out` overwrites a file, open `cat` and replace results in a new tab, and so on.

Three log-investigation examples (range + exclude + mask, nearby lines with AND and NOT, collecting into CSV), compared as the total of cold + hot, the other tool's time ÷ uvp's time (uvp and ripgrep gave identical results in every case):

| Target | Example | ripgrep (Mac) | ripgrep (Windows) | PowerGREP (Windows) |
|---|---|---:|---:|---:|
| Linux kernel, 60,000 files | range + exclude + mask | 1.87× | 1.85× | 20.26× |
| Linux kernel, 60,000 files | nearby lines, AND, NOT | 2.14× | 1.65× | 22.92× |
| Linux kernel, 60,000 files | collect into CSV | 3.53× | 2.39× | 42.94× |
| one 1 GB file | range + exclude + mask | 1.07× | 1/1.03× (on par) | 9.07× |
| one 1 GB file | nearby lines, AND, NOT | 1.46× | 1.09× | 8.62× |
| one 1 GB file | collect into CSV | 1.92× | 2.27× | 38.50× |

- Above 1 means uvp is faster. The ripgrep columns are the time to do the same thing with ripgrep combined with perl and the like. PowerGREP is the time of the same search only (no collecting, masking or excluding).
- On Windows, the first search of the 60,000 kernel files (which builds the index) takes 65–74 s (6–9 s on a Mac). From the second time on, the index is used.

→ [UwView Pro](https://uvp.y42u.net/pro/) (one-time $129 / $9 a month, **14-day free trial**)
