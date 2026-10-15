*日本語 ｜ [English](#uwview-v190--full-spectrum-english)*

## UwView v1.9.0 — Full Spectrum

> **2026-10-15 に配布物を差し替えました（build 26.10.13.22 → 26.10.14.16・版数は 1.9.0 のまま）。** 画面の「当たり」を「ヒット」に直し、見つかった不具合を直し、変換して開いたファイル（Word・Excel・PDF など）の元が変わったら知らせる設定を足しました。
> 10/14〜10/15 にダウンロードした方は、About または `uvf --version` の build で見分けられます。入れ直すときは、Homebrew は `brew reinstall --cask uwview`、Scoop は `scoop update uwview --force` です。

**（プレーン＋Word・Excel・PDF＋圧縮[gz・bz2・xz・lzma・zst・lz4・br]）×（CLI＋GUI）×（巨大＋多数）のファイルを、無料で、ripgrep と同等以上の速さで。**
v1.9.0 では、無料版でも **Word・Excel・PDF の中の文字を探せる**ようになりました。あわせて、ダークテーマ・フォント・キーの割り当て（vi・less 風のひな形つき）の設定、結果一覧へのブックマークの表示、ファイルの操作（パスのコピー・フォルダー・ターミナル・アプリで開く）、ファイル指定の波かっこ `{a,b}` が入りました。開いているログが外で切り詰められたり、ローテーションされたりしても落ちず、末尾追従がついていきます。
（前の安定版は v1.8.1.9 です → [v1.8.1.9 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.8.1.9.md)）

### 📄 Word・Excel・PDF を探す

```bash
uvf report.docx 東京            # Word
uvf '経費/**/*.xlsx' 交通費      # Excel（束ねて探す）
uvf 'docs/**/*.pdf' 契約 -i      # PDF
```

- コマンドの `uvf` は、文字を取り出しながら探します（索引は作りません）。1 本でも、`'**/*.pdf'` のような複数の指定でも探せます。名前で分からないもの（中身が PDF・Word）も、中身を見て探します。
- 画面で開くと「文字を取り出して開きます」と進み具合を出し、取り出した文字を見ながら探せます。元のファイルの隣には何も作りません。
- Excel は `シート名!行番号<TAB>A列<TAB>B列…` の行、PDF は `## page N` に続けて読む順の行になります。Excel は表示されている値で、`--raw` を付けると元の値で探します。
- **元の形式には書き出せません**（取り出した文字の保存・コピーはできます）。古い形式（.doc・.xls）・パスワード付き・文字の無い PDF（画像だけ）・壊れたファイルは、理由を出して飛ばします。

### 🎨 設定の画面（ダークテーマ・フォント・キーの割り当て）

Windows・Linux は「ツール → 設定…」（Ctrl＋,）、Mac はアプリ名のメニューの「設定…」（Cmd＋,）で開きます。

- **テーマ**：［OS に合わせる］（既定）［ライト］［ダーク］。OS の切り替えにその場でついていきます。ツールバーのアイコン・ハイライタの色も暗い背景に合わせて描きます。
- **フォント**：本文のフォントと大きさ（6〜48）。Ctrl（Cmd）＋ホイール・Ctrl＋＋／－・Ctrl＋0 でも変えられます。
- **キーの割り当て**：1 つの操作に 2 つまで。ひな形［標準］［vi・less 風］（`j` `k` `g` `G` `n` `N` `m` など。`5j` のような数の前置きも）。`.uwvkeys` に書き出して、別の機械（Mac と Windows・Linux の間でも）へ持っていけます。

### 📑 結果一覧とブックマーク・定義済み★のひな形

- 結果一覧の上に［ヒット＋ブックマーク］［ブックマークだけ］［ヒットだけ］の切り替え。ヒットとブックマークをファイルの行の順に混ぜて出します。行の範囲を選んで Ctrl＋B で、まとめて付け外しできます。
- 定義済み★に AND・NOT・OR の正規表現のひな形が最初から入ります（`^(?=.*語1)(?=.*語2)` など）。

### 📂 ファイルの操作・ファイル指定

- ステータスバーのファイル名を押すと、フルパスと［コピー］［フォルダー］［ターミナル］［アプリ］の窓が出ます（ファイル一覧の右クリックも同じ）。
- ファイル一覧の開く先を［メイン］［タブ］［アプリ］から選べます（アプリは、元のファイルを OS の既定のアプリで開きます）。
- ファイル指定に波かっこ：`'**/*.{txt,log}'`・`'app.log{,.*.gz}'`・`'logs/2026-{09,10}/*.log'`（1 つの指定から 1,000 通りまで）。画面の「コマンドライン」でも同じです。

### 📜 末尾追従が落ちない・ついていく

- 開いているファイルが外で**切り詰められた・作り直された・ローテーションされた・消された**ときに落ちていたのを直しました（読み方を mmap から普通の読み出しに替えました）。
- 末尾追従がオンなら自動で読み直して追従を続け、オフなら本文の上に「app.log が切り詰められました」などの帯と［読み直す］を出します。

### ⚠️ 注意

- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（zip と pbf は UwView Pro が扱います）。画面で開ける圧縮ファイルは 5 形式（gz・bz2・xz・lzma・zst）、コマンドの `uvf` は lz4・br を加えた 7 形式です。
- uvf のコマンドは、Word・Excel・PDF を読む部品（PdfPig・Apache 2.0）を入れたので大きくなりました（約 6.7MB → 17.4MB）。ライセンス文は配布物の `third-party/` にあります。

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
sudo snap install uwview                         # Linux（更新は snap refresh uwview）
```

手で入れる場合は、このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。チェックサムは `SHA256SUMS-1.9.0.txt` にあります。

```
8896bf79d1f2a4f92a23f4e0dae74641c9663f29b8b4fe6b0da3b8e5aa030862  UwView-1.9.0-linux-aarch64.tar.gz
a37bba83ed5989ab0cbdfb19230f4edfa4cf92ca04fd23ddcac1eb2a4434fa81  UwView-1.9.0-linux-x86_64.tar.gz
ffe839aa6a9673e00e373cd4c2841851050edc559f70df17d227b87f13a10967  UwView-1.9.0-mac-arm64.dmg
22d042884819aef62be472d7356e439698bdf4a8ab2814646582b3175ee8af04  UwView-1.9.0-mac-x64.dmg
a8c77b28ac0c51e4cb58fe6c538b7c3688304a34fc45b90e4d5f8c12dfca6a66  UwView-1.9.0-win-arm64.zip
cfcce7416d3ddb2457a10ba1070ff5ddd1cd869506e934f67feeba2adb2b3b06  UwView-1.9.0-win-x64.zip
（2026-10-14 作成・2026-10-15 差し替え。macOS 版は署名・公証済み。ビルド番号はすべて 26.10.14.16（日本時間）です）
```

> **配布は GitHub Releases が元です。** Homebrew・Scoop・Snap も、ここのファイルを使います（Homebrew と Scoop は SHA256 を照合します）。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 文字コード 4 つ・pbf・zip・複雑な検索まで「全域」で速く

有償版の `uvp` と UwView Pro の画面は、無料版の式に **pbf・zip**、**（UTF-8・Shift-JIS・EUC-JP・UTF-16）**、**（複雑な検索：範囲・段・集計・並べ替え・伏せ字・整形）** を掛けた全域で速くなりました。

- **Shift-JIS・EUC-JP・UTF-16 を UTF-8 の索引に変えて持つ**ので、2 回目からは UTF-8 のファイルと同じ速さで探せます。
- Word・Excel・PDF も索引にして、集計・範囲・整形まで使えます。
- 2 回目からの検索を速くする仕組み（行の骨格・ブロックの印・2 語目の突き合わせ）が入りました。

ripgrep との比較です（cold＋hot の合計で、ripgrep の時間 ÷ uvp の時間。出力は全件で ripgrep と同じ）。

| 対象（ripgrep ÷ uvp） | **Mac** | Windows（参考） | Linux（参考） |
|---|---:|---:|---:|
| 3GB の 1 本（30〜32 項目の合計） | 2.56 倍 | 2.24 倍 | 1.50 倍 |
| 10GB の 1 本（30 項目の合計） | 2.51 倍 | 3.17 倍 | 1.96 倍 |
| Shift-JIS・EUC-JP の 3GB（7 パターン・2 回目から） | 5.4〜29 倍 | 2.2〜12.5 倍 | 2.0〜13.1 倍 |
| UTF-16 の 6GB（同） | 8.1〜38.8 倍 | 3.7〜14.1 倍 | 3.0〜15.3 倍 |
| 集計・並べ替え（`-uniq`・`-sort`。3GB・10GB） | 4.2〜4.9 倍 | 2.0〜3.6 倍 | 1.4〜3.3 倍 |
| ログの 3 例（範囲＋除外＋伏せ字・近い行・CSV への収集。Linux カーネル 6 万本） | 2.35〜4.56 倍 | 1.26〜2.48 倍 | 1/2.85〜1/3.21（rg が速い） |

- ripgrep の列は、同じことを ripgrep と perl・awk・sort などを組み合わせて行った時間です。Shift-JIS などは `rg -E 文字コード` と比べました。
- 1 回目（索引の作成込み）は、Shift-JIS・EUC-JP で ripgrep の 1/1.4〜1/1.7 ほどです（Mac で 3 回測った幅）。2 回目から差が開きます。
- **Windows・Linux は、正常に動くことの確認のために流しました**（正しさのテストがすべて合格、出力は ripgrep と不一致 0）。Windows・Linux の速さは、機械も条件も Mac と違うので参考値です。参考の Linux では、1 回目（Shift-JIS などで 1/5〜1/9）とログの 3 例（6 問中 5 問）で ripgrep が速い値も出ました。
- 秒数は 2026 年 10 月 13〜14 日に、Mac（Apple M4・メモリ 32GB・外付け USB SSD）で v1.9.0 を測った値です（Windows・Linux は参考）。**秒数を環境をまたいで比べないでください。**

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.9.0 — Full Spectrum (English)

> **The downloads were replaced on 2026-10-15 (build 26.10.13.22 → 26.10.14.16; the version stays 1.9.0).** This build fixes issues found in review and adds a setting that tells you when the original of a converted file (Word, Excel, PDF and so on) changes.
> If you downloaded on Oct 14–15, check the build in About or with `uvf --version`. To reinstall: Homebrew `brew reinstall --cask uwview`; Scoop `scoop update uwview --force`.

*[日本語](#uwview-v190--full-spectrum) ｜ English*

**(Plain + Word, Excel, PDF + compressed [gz, bz2, xz, lzma, zst, lz4, br]) × (CLI + GUI) × (huge + many) files — free, and on par with ripgrep or faster.**
In v1.9.0 the free edition can **search the text inside Word, Excel and PDF files**. It also adds settings for a dark theme, fonts and key bindings (with a vi/less-style preset), bookmarks in the result list, file actions (copy path, folder, terminal, open in app), and brace patterns `{a,b}` in file patterns. A log that is truncated or rotated while open no longer crashes the app, and tail-follow keeps up.
(The previous stable release is v1.8.1.9 → [what changed in v1.8.1.9](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.8.1.9.md))

### 📄 Search Word, Excel and PDF

```bash
uvf report.docx Tokyo            # Word
uvf 'expenses/**/*.xlsx' taxi    # Excel (bundled search)
uvf 'docs/**/*.pdf' contract -i  # PDF
```

- The `uvf` command extracts the text as it searches (it builds no index). One file or many (`'**/*.pdf'`) both work. Files whose names don't say what they are (a PDF or Word file inside) are recognized by their content.
- In the window, opening one shows “Extracting text” with progress, then lets you read and search the extracted text. Nothing is written next to the original file.
- Excel becomes lines of `Sheet!row<TAB>A<TAB>B…`, PDF becomes `## page N` followed by the lines in reading order. Excel uses the displayed values; `--raw` searches the stored values.
- **They cannot be written back in their own format** (you can save or copy the extracted text). Old formats (.doc, .xls), password-protected files, PDFs without text (images only) and broken files are skipped with the reason.

### 🎨 Settings (dark theme, fonts, key bindings)

Open with Tools → Settings… (Ctrl+,) on Windows and Linux, or Settings… in the app menu (Cmd+,) on a Mac.

- **Theme**: Follow OS (default), Light, Dark. It follows the OS switch on the spot. Toolbar icons and highlighter colors are drawn for the dark background.
- **Font**: body font and size (6–48). Ctrl (Cmd)+wheel, Ctrl++ / Ctrl+– and Ctrl+0 also work.
- **Key bindings**: up to two keys per action. Presets **Standard** and **vi/less style** (`j` `k` `g` `G` `n` `N` `m` and more, with count prefixes like `5j`). Export to `.uwvkeys` and take it to another machine (Mac ↔ Windows/Linux too).

### 📑 Result list with bookmarks, predefined templates

- Above the result list: **Hits + bookmarks**, **Bookmarks only**, **Hits only**. Hits and bookmarks are merged in file-line order. Select a range of lines and press Ctrl+B to toggle them all.
- The predefined ★ list starts with AND, NOT and OR regex templates (`^(?=.*word1)(?=.*word2)` and so on).

### 📂 File actions and file patterns

- Click the file name in the status bar for the full path with **Copy**, **Folder**, **Terminal** and **App** (also on right-click in the file list).
- The file list can open a file in **Main**, a **Tab** or an **App** (the original file in the OS default app).
- Brace patterns in file patterns: `'**/*.{txt,log}'`, `'app.log{,.*.gz}'`, `'logs/2026-{09,10}/*.log'` (up to 1,000 expansions per pattern). The window's Command Line works the same.

### 📜 Tail-follow that doesn't crash and keeps up

- Fixed a crash when the open file was **truncated, recreated, rotated or deleted** from outside (reading no longer uses mmap).
- With tail-follow on, the file is reloaded and following continues. With it off, a bar such as “app.log was truncated” appears above the text with **Reload**.

### ⚠️ Notes

- Not handled by the free edition: `.zip`, `.tar.gz` and OSM `.pbf` (UwView Pro handles zip and pbf). The window opens 5 compressed formats (gz, bz2, xz, lzma, zst); the `uvf` command reads 7 (plus lz4 and br).
- The `uvf` command is larger (about 6.7 MB → 17.4 MB) because it now includes the Word/Excel/PDF reader (PdfPig, Apache 2.0). The license text is in `third-party/` in the package.

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
sudo snap install uwview                         # Linux (update: snap refresh uwview)
```

To install by hand, get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). Checksums are in `SHA256SUMS-1.9.0.txt`.

```
8896bf79d1f2a4f92a23f4e0dae74641c9663f29b8b4fe6b0da3b8e5aa030862  UwView-1.9.0-linux-aarch64.tar.gz
a37bba83ed5989ab0cbdfb19230f4edfa4cf92ca04fd23ddcac1eb2a4434fa81  UwView-1.9.0-linux-x86_64.tar.gz
ffe839aa6a9673e00e373cd4c2841851050edc559f70df17d227b87f13a10967  UwView-1.9.0-mac-arm64.dmg
22d042884819aef62be472d7356e439698bdf4a8ab2814646582b3175ee8af04  UwView-1.9.0-mac-x64.dmg
a8c77b28ac0c51e4cb58fe6c538b7c3688304a34fc45b90e4d5f8c12dfca6a66  UwView-1.9.0-win-arm64.zip
cfcce7416d3ddb2457a10ba1070ff5ddd1cd869506e934f67feeba2adb2b3b06  UwView-1.9.0-win-x64.zip
(built 2026-10-14, replaced 2026-10-15; the macOS builds are signed and notarized. Build number: 26.10.14.16 (JST) for all builds)
```

> **GitHub Releases is the source.** Homebrew, Scoop and Snap use the files here (Homebrew and Scoop check the SHA256). Instructions and news are on the blog (https://uvp.y42u.net/).

### 📣 On the UwView Pro side — four encodings, pbf, zip and complex searches, fast across the full spectrum

The paid `uvp` and the UwView Pro window take the free edition's formula and multiply it by **pbf and zip**, **(UTF-8, Shift-JIS, EUC-JP, UTF-16)** and **(complex searches: ranges, stages, tallies, sorting, masking, formatting)** — fast across all of it.

- **Shift-JIS, EUC-JP and UTF-16 are kept as a UTF-8 index**, so from the second search on they are as fast as UTF-8 files.
- Word, Excel and PDF are indexed too, with tallies, ranges and formatting.
- New machinery makes repeat searches faster (line skeleton, block marks, joining on the second word).

Compared with ripgrep (total of cold + hot, ripgrep's time ÷ uvp's time; output identical to ripgrep in every case):

| Target (ripgrep ÷ uvp) | **Mac** | Windows (reference) | Linux (reference) |
|---|---:|---:|---:|
| one 3 GB file (total of 30–32 items) | 2.56× | 2.24× | 1.50× |
| one 10 GB file (total of 30 items) | 2.51× | 3.17× | 1.96× |
| Shift-JIS and EUC-JP, 3 GB (7 patterns, from the 2nd run) | 5.4–29× | 2.2–12.5× | 2.0–13.1× |
| UTF-16, 6 GB (same) | 8.1–38.8× | 3.7–14.1× | 3.0–15.3× |
| tally and sort (`-uniq`, `-sort`; 3 GB and 10 GB) | 4.2–4.9× | 2.0–3.6× | 1.4–3.3× |
| three log examples (range + exclude + mask, nearby lines, collect into CSV; Linux kernel, 60,000 files) | 2.35–4.56× | 1.26–2.48× | 1/2.85–1/3.21 (ripgrep faster) |

- The ripgrep column is the time to do the same thing with ripgrep combined with perl, awk, sort and the like. Shift-JIS and the others were compared with `rg -E <encoding>`.
- The first run (including building the index) is about 1/1.4–1/1.7 of ripgrep for Shift-JIS and EUC-JP (the range over three runs on a Mac); the gap opens from the second.
- **Windows and Linux were run to confirm correct operation** (all correctness tests passed, zero output mismatches against ripgrep). Their speeds are for reference only, since the machines and conditions differ from the Mac. In the reference Linux run, ripgrep was faster on the first run (1/5–1/9 for Shift-JIS and the like) and on 5 of the 6 log examples.
- Times were measured on 13–14 October 2026 with v1.9.0 on a Mac (Apple M4, 32 GB, external USB SSD); Windows and Linux are for reference. **Do not compare times across environments.**

→ [UwView Pro](https://uvp.y42u.net/pro/) (one-time $129 / $9 a month, **14-day free trial**)
