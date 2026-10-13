*日本語 ｜ [English](#uwview-v190--full-spectrum-english)*

## UwView v1.9.0 — Full Spectrum

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

- 結果一覧の上に［当たり＋ブックマーク］［ブックマークだけ］［当たりだけ］の切り替え。当たりとブックマークをファイルの行の順に混ぜて出します。行の範囲を選んで Ctrl＋B で、まとめて付け外しできます。
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
ce190359416b46c84e21898c456f75e35f87402f81ce53e176a2922ff447bc86  UwView-1.9.0-linux-aarch64.tar.gz
b09c50f2e8f09ddd655712595a706e501021a7f02ea300cc6fc64cd59d11a025  UwView-1.9.0-linux-x86_64.tar.gz
55bc456a37940d35ad34cc3ae665620bbe13b74025f0cf34fb3af6353e574c49  UwView-1.9.0-mac-arm64.dmg
dbc162d73ec59afc460827c61e36f47261ff937934ccce1074a713afc6f87ebd  UwView-1.9.0-mac-x64.dmg
0dc610df1408f386adc3e1e8fd883009a6f50bf39c126cb62e509ac5cd7a687e  UwView-1.9.0-win-arm64.zip
85cc6274211b2aa7be09bc436082398af3ecde883eeedf399afce91442d26cf9  UwView-1.9.0-win-x64.zip
（2026-10-13 作成。macOS 版は署名・公証済み。ビルド番号はすべて 26.10.13.13（日本時間）です）
```

> **配布は GitHub Releases が元です。** Homebrew・Scoop・Snap も、ここのファイルを使います（Homebrew と Scoop は SHA256 を照合します）。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 文字コード 4 つ・pbf・zip・複雑な検索まで「全域」で速く

有償版の `uvp` と UwView Pro の画面は、無料版の式に **pbf・zip**、**（UTF-8・Shift-JIS・EUC-JP・UTF-16）**、**（複雑な検索：範囲・段・集計・並べ替え・伏せ字・整形）** を掛けた全域で速くなりました。

- **Shift-JIS・EUC-JP・UTF-16 を UTF-8 の索引に変えて持つ**ので、2 回目からは UTF-8 のファイルと同じ速さで探せます。
- Word・Excel・PDF も索引にして、集計・範囲・整形まで使えます。
- 2 回目からの検索を速くする仕組み（行の骨格・ブロックの印・2 語目の突き合わせ）が入りました。

ripgrep との比較です（cold＋hot の合計で、ripgrep の時間 ÷ uvp の時間。出力は全件で ripgrep と同じ）。

| 対象 | ripgrep ÷ uvp |
|---|---:|
| 3GB・10GB の 1 本（30 項目の合計） | 2.47 倍・2.42 倍 |
| Shift-JIS・EUC-JP の 3GB（7 パターン） | 5.2〜23 倍 |
| UTF-16 の 6GB（7 パターン） | 8.1〜37.6 倍 |
| 集計・並べ替え（`-uniq`・`-sort`。3GB・10GB） | 4.3〜5.4 倍 |
| ログの 3 例（範囲＋除外＋伏せ字・近い行・CSV への収集。Linux カーネル 6 万本） | 2.32〜3.71 倍 |

- ripgrep の列は、同じことを ripgrep と perl・awk・sort などを組み合わせて行った時間です。Shift-JIS などは `rg -E 文字コード` と比べました。
- 1 回目（索引を作る分を含む）は ripgrep と同じくらいで、2 回目から差が開きます。
- 秒数は、ある 1 台の Mac（Apple M4・メモリ 32GB・外付け USB SSD）で 2026 年 10 月 13 日に測った値です。**秒数を環境をまたいで比べないでください。**

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.9.0 — Full Spectrum (English)

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
ce190359416b46c84e21898c456f75e35f87402f81ce53e176a2922ff447bc86  UwView-1.9.0-linux-aarch64.tar.gz
b09c50f2e8f09ddd655712595a706e501021a7f02ea300cc6fc64cd59d11a025  UwView-1.9.0-linux-x86_64.tar.gz
55bc456a37940d35ad34cc3ae665620bbe13b74025f0cf34fb3af6353e574c49  UwView-1.9.0-mac-arm64.dmg
dbc162d73ec59afc460827c61e36f47261ff937934ccce1074a713afc6f87ebd  UwView-1.9.0-mac-x64.dmg
0dc610df1408f386adc3e1e8fd883009a6f50bf39c126cb62e509ac5cd7a687e  UwView-1.9.0-win-arm64.zip
85cc6274211b2aa7be09bc436082398af3ecde883eeedf399afce91442d26cf9  UwView-1.9.0-win-x64.zip
(built 2026-10-13; the macOS builds are signed and notarized. Build number: 26.10.13.13 (JST) for all builds)
```

> **GitHub Releases is the source.** Homebrew, Scoop and Snap use the files here (Homebrew and Scoop check the SHA256). Instructions and news are on the blog (https://uvp.y42u.net/).

### 📣 On the UwView Pro side — four encodings, pbf, zip and complex searches, fast across the full spectrum

The paid `uvp` and the UwView Pro window take the free edition's formula and multiply it by **pbf and zip**, **(UTF-8, Shift-JIS, EUC-JP, UTF-16)** and **(complex searches: ranges, stages, tallies, sorting, masking, formatting)** — fast across all of it.

- **Shift-JIS, EUC-JP and UTF-16 are kept as a UTF-8 index**, so from the second search on they are as fast as UTF-8 files.
- Word, Excel and PDF are indexed too, with tallies, ranges and formatting.
- New machinery makes repeat searches faster (line skeleton, block marks, joining on the second word).

Compared with ripgrep (total of cold + hot, ripgrep's time ÷ uvp's time; output identical to ripgrep in every case):

| Target | ripgrep ÷ uvp |
|---|---:|
| one 3 GB / 10 GB file (total of 30 items) | 2.47× / 2.42× |
| Shift-JIS and EUC-JP, 3 GB (7 patterns) | 5.2–23× |
| UTF-16, 6 GB (7 patterns) | 8.1–37.6× |
| tally and sort (`-uniq`, `-sort`; 3 GB and 10 GB) | 4.3–5.4× |
| three log examples (range + exclude + mask, nearby lines, collect into CSV; Linux kernel, 60,000 files) | 2.32–3.71× |

- The ripgrep column is the time to do the same thing with ripgrep combined with perl, awk, sort and the like. Shift-JIS and the others were compared with `rg -E <encoding>`.
- The first search (which builds the index) takes about as long as ripgrep; the gap opens from the second search on.
- Times were measured on one Mac (Apple M4, 32 GB, external USB SSD) on 13 October 2026. **Do not compare times across environments.**

→ [UwView Pro](https://uvp.y42u.net/pro/) (one-time $129 / $9 a month, **14-day free trial**)
