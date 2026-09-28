*日本語 ｜ [English](#uwview-v1736--wide-field-english)*

## UwView v1.7.3.6 — Wide Field

**v1.7.3.5（Wide Field）に、キー操作とブックマークを残す機能を足した版です。**
（前回は v1.7.3.5 です → [v1.7.3.5 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.5.md)。
その前の v1.6.6.1 から変わったことは、下の「v1.7（Wide Field）で変わったこと」にまとめています）

### 🆕 v1.7.3.6 で変わったこと

**⌨ キー操作**（Mac は Ctrl を Cmd に読み替えます）

| キー | 動き |
|---|---|
| Ctrl+F | 検索欄へ移る（文字を全部選んだ状態） |
| F3 ／ Shift+F3 | 次の当たり／前の当たり（Mac は Cmd+G ／ Cmd+Shift+G も） |
| Ctrl+L | ジャンプ欄へ移る。**ジャンプ欄で Enter を押すと移動します** |
| Ctrl+B ／ `[` ・ `]` | ブックマークを付ける・外す／前・次のブックマークへ |
| Ctrl+Shift+F | 末尾追従のオン・オフ |
| F5 | ファイルを読み直す（同じ位置・同じタブの並びで開き直します） |
| Esc | 検索欄・ジャンプ欄から本文へ戻る |

- 検索欄・ジャンプ欄に文字を打っている間は、`[` `]` は文字として入ります。
- 複数ファイルの結果のタブ（`uvf -open`）は、F5 で読み直しません。

**🔖 ブックマークを残す**

- ブックマークをファイルごとに覚えて、同じファイルを開くと戻します（前回の続きを開かなくても戻ります）。
- 追記されただけのファイルなら戻します。中身が変わっていたら（作り直された・先頭が変わった）戻さずに、そう知らせます。見分けるのに読むのは、先頭と前回の末尾の手前の数 KB だけです。
- ファイルメニュー「**ブックマークを書き出す…**」：`名前-bookmarks.txt` に「行番号<TAB>本文」で書き出します。

---

## v1.7（Wide Field）で変わったこと

**1本ずつだったのが、まとめて探せるようになりました。**
複数のファイルを一度に探し、平文と圧縮ファイルを混ぜても1回で済みます。その結果は、そのまま画面で読めます。

> **Wide Field（広視野）**：空の広い範囲を一度に写す望遠鏡やカメラを、天文学ではこう呼びます。
> v1.6.6「First Light」で1本のファイルを待たずに見られるようになりました。v1.7 では、その視野を複数のファイルに広げます。

### 🗂 複数のファイルをまとめて探す

```bash
uvf '*.log' ERROR                    今のフォルダーの .log をまとめて
uvf '**/*.log' ERROR                 サブフォルダーも含めて
uvf 'app.log,app.log.*.gz' ERROR     平文と .gz を混ぜて、1回で
uvf '**/*.log' ERROR --files         探さずに、対象になるファイルだけを出す
```

- **指定は必ず引用符で囲みます。** シェルではなく `uvf` が広げるので、Windows でも同じ書き方で動きます。
- 出力は `ファイル名:行番号<TAB>本文` です。ファイルは指定した順に並びます。`--json`（JSON Lines）と `-H`／`-h` も使えます。
- **ワイルドカードで広げた分は、`.ignore`・`.gitignore` に当たるファイルを外します**（ripgrep と同じ規則です）。
  隠しファイルは、どの OS でも対象外です。名前を書いて指定したファイルは外しません。除外した本数は知らせます。外したくないときは `--no-ignore` を付けます。

### 🗜 展開しながら探せる形式が7つになりました

これまでの `.gz` に加えて、**`.bz2`・`.xz`・`.lzma`・`.zst`・`.lz4`・`.br`** も、展開しながらそのまま探せます。外部のコマンドは使いません。
xz のうち複数ブロックで作られたもの（xz 5.6 以降の既定）は、複数のスレッドで展開します。

| 3G 相当の OSM XML（1回目＋2回目の合計） | 大きさ | `rg -z` | **`uvf`** | 倍率 |
|---|---:|---:|---:|---:|
| gz | 318 MB | 2.62秒 | **2.02秒** | **1.30倍** |
| bz2 | 251 MB | 44.98秒 | **43.96秒** | **1.02倍** |
| xz | 253 MB | **3.41秒** | 3.66秒 | 1/1.07 |
| lzma | 254 MB | 20.90秒 | **16.83秒** | **1.24倍** |
| zst | 348 MB | 3.23秒 | **3.01秒** | **1.07倍** |
| lz4 | 533 MB | 1.86秒 | **1.67秒** | **1.11倍** |
| br | 292 MB | 4.89秒 | **4.01秒** | **1.22倍** |

**7形式のうち6形式で `uvf` が速い結果でした。** 外部のコマンドを起動せず、展開と照合を1つの処理の中で重ねられるからです。
**xz だけは ripgrep が 7% 速い**という結果でした。5回くり返しても同じだったので、測定の揺れではありません。

### 🪟 複数ファイルの結果を、画面で読む

```bash
uvf -open '*.log' ERROR
```

- 結果の窓の行番号欄は **`ファイル番号:行番号`** です（例 `3:128`）。ダブルクリックすると、そのファイルのその行へ移ります。圧縮ファイルは隣に展開してから開きます。
- **ファイル一覧**を開くと、番号と実ファイルの対応、各ファイルの当たりの件数、大きさが見られます。
- 一覧で「**タブで開く**」をオンにすると、ファイルを追加のタブで開けます。タブは最大8個（メイン1＋追加7）です。すでに開いているファイルは、そのタブに切り替えるだけです。
  - タブは1行のまま左右にスクロールします。タブ名は `番号:ファイル名` で、27文字を超える分は `...` で省きます。マウスを重ねると全体の名前が出ます。
  - 結果のメインのタブは灰色にして、ほかのタブと見分けられるようにしました。
- 無料版 `uvf` は、元のファイルを開き直してから該当の行へ移ります。大きいファイルでは少し待ちます。Pro の `uvp` は、束ねた索引の中をすぐに移ります。

### 🎛 画面のそのほかの変更

- **漢字で書いていたボタンを、アイコンにしました。** ボタンの名前は、マウスを乗せると出ます。はい／いいえ・開く／閉じる・保存・中止は、今までどおり文字のままです。
- 窓の題名が `UwView(uvf)-Wide Field(v1.7.3.6)` の形になり、版数が一目で分かるようになりました。

### 🔧 コマンド（`uvf`）のそのほかの変更

- **Mac と Linux の `uvf` を、ネイティブの1本の実行ファイルにしました。** Mac では、起動にかかる時間が 0.14秒から 0.01秒になりました。
- `uvf --tune`：この機械に合うスレッド数を実測します。`--apply` を付けると、測った値を設定に保存します。
- `uvf --help files`／`uvf --help regex`：ファイルの指定の仕方と、正規表現（.NET の書き方）のくわしい説明を出します。ripgrep で書いた正規表現は、書き換え方も案内します。
- Windows で、長いパス（260文字を超えるもの）を扱えるようにしました。

### ⚠️ 注意

- 平文を複数まとめて探すときは、**ripgrep の方が速い**という結果でした。平文5本では、`uvf` の時間が ripgrep の 1.17倍でした。gz を含むと `uvf` が速くなります（gz 7本では、ripgrep より 1.28倍速い結果でした）。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- 画面の検索欄が探すのは、メインに出ている1ファイルだけです。複数のファイルを探し直すときは、コマンドから探してください。
- 秒数は、ある1台の Mac（arm64・論理 CPU 10・キャッシュを捨ててから測定）で測った値で、uvf 1.7.2.7〜1.7.2.8、ripgrep 15.2.0 と比べたものです。**秒数を環境をまたいで比べないでください。** 条件と全データは [uvf コマンド操作マニュアル](https://github.com/amru195704/UwView/blob/main/2-doc/uvf_コマンド操作マニュアル.md) の「性能」の章にあります。

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
```

Linux の場合と、手で入れる場合は、下のファイルを使ってください。チェックサムは `SHA256SUMS-1.7.3.6.txt` にあります。

```
f7aec95d4c13851e5f27fc06db3498906c08e8d568c4a56dfc27fcf3bf032774  UwView-1.7.3.6-linux-aarch64.tar.gz
6557dffac240b00bf8135a9a63760e81a7a9d69bb7b8f2f935514ebdc74d58a5  UwView-1.7.3.6-linux-x86_64.tar.gz
d1ceadc10559906614414701e8ae09cd1040e3917a1ad7fa5208a9886945162b  UwView-1.7.3.6-mac-arm64.dmg
352db974ede04b608eb5134cd0c5ee345ecfb9602bc3405c88c61cd47b65a622  UwView-1.7.3.6-mac-x64.dmg
28414a72f3011f36c066e4f78a0e442ce34b5995a59ae7278d83fa619e22ae3c  UwView-1.7.3.6-win-arm64.zip
772c93a644216b207f0f0c4aa507cf6d657edcf69653e5430ff1119fadcdbcb4  UwView-1.7.3.6-win-x64.zip
```

> **配布は GitHub Releases のみです。** Homebrew と Scoop も、ここからファイルを取得して SHA256 を照合します。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 束ねて、2回目から速く

有償版の `uvp` は、複数のファイルを **1本の `.uwvz` に束ねます**。束ねた `.uwvz` は、2回目からは作り直しません。`.zip` の中のテキストや OSM の `.pbf` も入力にできます。

| 8本・初回と直後の再検索の合計（試験用ビルド 1.7.0.18） | ripgrep（`-z`） | `uvf` | **`uvp`** |
|---|---:|---:|---:|
| 平文4本＋`.gz` 4本 | 9.72秒 | 8.58秒 | **3.97秒** |

`japan-latest.osm.pbf`（2.46GB）は、XML にして 51.3GB 分の内容になります。`uvp` はこれを、検索できる `.uwvz` に **21.6秒**で変えます。続けて「東京」（94,979件）を探し終えるまで、キャッシュを捨てた状態から合計 **27.34秒**。2回目からは `.uwvz` を使うので **5.01秒**（キャッシュを捨てた直後でも 6.43秒）です（`uvp` 1.7.3.5・2026年9月28日）。
**v1.7.3.6 の Pro 側の変更**
- 上のキー操作とブックマークは、Pro でも同じです（束ねた索引でもブックマークを覚えます）。編集中の本文では `[` `]` は文字として入り、F5 では読み直しません。
- 結果の窓の前後 ±N の上限を、**32 行から 64 行**にしました（多段階の窓も同じ）。
- **`uvp x.osm.pbf 語 -open` で、画面に pbf のバイナリが出ていたのを直しました。** 画面には変換した `.uwvz`（OSM の XML）が開きます。`.gz` などの圧縮ファイル 1 本の `-open` も同じく直しました。変換していない pbf を画面で開くと、バイナリを出さずに「先に変換してください」と知らせます。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.7.3.6 — Wide Field (English)

**v1.7.3.5 (Wide Field) plus keyboard shortcuts and bookmarks that are remembered.**
(The previous release was v1.7.3.5 → [what changed in v1.7.3.5](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.5.md).
What changed since v1.6.6.1 is summarized under "What changed in v1.7 (Wide Field)" below.)

### 🆕 What changed in v1.7.3.6

**⌨ Keyboard shortcuts** (on a Mac, read Ctrl as Cmd)

| Key | Action |
|---|---|
| Ctrl+F | Go to the search box (with its text selected) |
| F3 / Shift+F3 | Next hit / previous hit (on a Mac, also Cmd+G / Cmd+Shift+G) |
| Ctrl+L | Go to the jump box. **Press Enter in the jump box to jump** |
| Ctrl+B / `[` · `]` | Toggle a bookmark / go to the previous · next bookmark |
| Ctrl+Shift+F | Turn follow-the-end on or off |
| F5 | Reload the file (reopens it at the same position, in the same tab order) |
| Esc | Leave the search box or jump box and return to the text |

- While typing in the search box or jump box, `[` and `]` are typed as characters.
- Tabs opened from a multi-file result (`uvf -open`) are not reloaded by F5.

**🔖 Bookmarks are remembered**

- Bookmarks are remembered per file and come back when you open the same file again (even without restoring the last session).
- If the file has only grown, they come back. If its contents changed (recreated, or the start changed), they are not restored and you are told so. Only a few KB at the start and just before the previous end are read to tell the difference.
- File menu → **Export bookmarks…** writes `name-bookmarks.txt` as `line number<TAB>text`.

---

## What changed in v1.7 (Wide Field)

**UwView used to take one file at a time. Now it searches many at once.**
You can search several files in one go and mix plain text with compressed files in the same run. The results can be read right in the window.

> **Wide Field** is what astronomers call a telescope or camera that takes in a broad patch of sky at once.
> With v1.6.6 "First Light", a single file could be viewed without waiting. v1.7 widens that view to many files.

### 🗂 Search several files at once

```bash
uvf '*.log' ERROR                    every .log in this folder
uvf '**/*.log' ERROR                 subfolders too
uvf 'app.log,app.log.*.gz' ERROR     plain text and .gz, in one run
uvf '**/*.log' ERROR --files         just list what would be searched
```

- **Always quote the pattern.** `uvf` expands it rather than the shell, so it works the same way on Windows.
- Output is `file:line<TAB>text`, with files in the order you gave them. `--json` (JSON Lines) and `-H` / `-h` are available.
- **When a wildcard is expanded, files matched by `.ignore` / `.gitignore` are left out**, using the same rules as ripgrep.
  Hidden files are skipped on every OS. A file you name explicitly is never left out. `uvf` reports how many files it skipped, and `--no-ignore` turns the rules off.

### 🗜 Seven compressed formats, searched as they decompress

On top of `.gz`, **`.bz2`, `.xz`, `.lzma`, `.zst`, `.lz4` and `.br`** can now be searched as they decompress, with no external commands.
An xz file made of several blocks (the default since xz 5.6) is decompressed on several threads.

| OSM XML, 3 GB of text (first + second run) | Size | `rg -z` | **`uvf`** | Ratio |
|---|---:|---:|---:|---:|
| gz | 318 MB | 2.62 s | **2.02 s** | **1.30×** |
| bz2 | 251 MB | 44.98 s | **43.96 s** | **1.02×** |
| xz | 253 MB | **3.41 s** | 3.66 s | 1/1.07 |
| lzma | 254 MB | 20.90 s | **16.83 s** | **1.24×** |
| zst | 348 MB | 3.23 s | **3.01 s** | **1.07×** |
| lz4 | 533 MB | 1.86 s | **1.67 s** | **1.11×** |
| br | 292 MB | 4.89 s | **4.01 s** | **1.22×** |

**`uvf` was faster in six of the seven formats.** It starts no external command, so decompression and matching overlap inside one process.
**On xz, ripgrep was 7% faster.** Five repeated runs gave the same result, so this is not measurement noise.

### 🪟 Read multi-file results in the window

```bash
uvf -open '*.log' ERROR
```

- The line column of the results window reads **`file-number:line`** (e.g. `3:128`). Double-click a hit to jump to that line of that file. A compressed file is decompressed next to the original before it opens.
- The **file list** shows which number is which file, with the hit count and size of each.
- Turn on **Open in tab** in the list to open files in extra tabs, up to 8 (the main tab plus 7). A file that is already open is switched to, not opened again.
  - The tabs stay on one row and scroll sideways. A tab is named `number:file-name` and cut to 27 characters with `...`. Hover to see the full name.
  - The main tab of a result set is gray, so it stands out from the others.
- The free `uvf` reopens the original file before jumping, so a large file takes a moment. Pro's `uvp` jumps at once inside its bundled index.

### 🎛 Other changes in the window

- **Buttons that were labeled in Japanese are now icons.** Hover to see what a button does. Yes / No, Open / Close, Save and Cancel keep their text labels.
- The window title now shows the version, as `UwView(uvf)-Wide Field(v1.7.3.6)`.

### 🔧 Other changes in the command (`uvf`)

- **On Mac and Linux, `uvf` is now a single native executable.** On a Mac, startup went from 0.14 s to 0.01 s.
- `uvf --tune` measures the best thread count for this machine. Add `--apply` to save it to the settings.
- `uvf --help files` / `uvf --help regex` explain how to name files and how to write regular expressions (.NET syntax), including how to rewrite a pattern written for ripgrep.
- On Windows, paths longer than 260 characters now work.

### ⚠️ Notes

- When searching several plain-text files, **ripgrep was faster**: on five plain files, `uvf` took 1.17× as long as ripgrep. Once gz files are included, `uvf` is ahead (1.28× faster than ripgrep on seven gz files).
- The free edition does not read `.zip`, `.tar.gz` or OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- The search box in the window searches only the file in the main tab. To search several files again, use the command.
- All timings come from one Mac (arm64, 10 logical CPUs, cache dropped before each run), comparing uvf 1.7.2.7–1.7.2.8 with ripgrep 15.2.0. **Do not compare seconds across machines.** Conditions and full data are in the "性能" (performance) chapter of the [uvf command manual](https://github.com/amru195704/UwView/blob/main/2-doc/uvf_コマンド操作マニュアル.md) (Japanese).

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
```

On Linux, or to install by hand, use the files below. Checksums are in `SHA256SUMS-1.7.3.6.txt`.

```
f7aec95d4c13851e5f27fc06db3498906c08e8d568c4a56dfc27fcf3bf032774  UwView-1.7.3.6-linux-aarch64.tar.gz
6557dffac240b00bf8135a9a63760e81a7a9d69bb7b8f2f935514ebdc74d58a5  UwView-1.7.3.6-linux-x86_64.tar.gz
d1ceadc10559906614414701e8ae09cd1040e3917a1ad7fa5208a9886945162b  UwView-1.7.3.6-mac-arm64.dmg
352db974ede04b608eb5134cd0c5ee345ecfb9602bc3405c88c61cd47b65a622  UwView-1.7.3.6-mac-x64.dmg
28414a72f3011f36c066e4f78a0e442ce34b5995a59ae7278d83fa619e22ae3c  UwView-1.7.3.6-win-arm64.zip
772c93a644216b207f0f0c4aa507cf6d657edcf69653e5430ff1119fadcdbcb4  UwView-1.7.3.6-win-x64.zip
```

> **Distribution is GitHub Releases only.** Homebrew and Scoop download from here and check the SHA256. Guides and news are on the blog (https://uvp.y42u.net/en/).

### 📣 On the UwView Pro side — bundle once, fast from the second search

The paid `uvp` bundles several files into **one `.uwvz`**, and does not rebuild it on later searches. It also reads text inside `.zip` files and OSM `.pbf`.

| 8 files, first search + immediate repeat (test build 1.7.0.18) | ripgrep (`-z`) | `uvf` | **`uvp`** |
|---|---:|---:|---:|
| 4 plain + 4 `.gz` | 9.72 s | 8.58 s | **3.97 s** |

`japan-latest.osm.pbf` (2.46 GB) holds 51.3 GB of content as XML. `uvp` turns it into a searchable `.uwvz` in **21.6 s**. Finding 東京 (94,979 hits) takes **27.34 s** in total from a dropped cache; from the second search on it uses the `.uwvz` and takes **5.01 s** (6.43 s right after dropping the cache) (`uvp` 1.7.3.5, 28 September 2026).
**Pro changes in v1.7.3.6**
- The shortcuts and remembered bookmarks above work the same in Pro (bookmarks are remembered in a bundled index too). While editing, `[` and `]` are typed as characters and F5 does not reload.
- The ±N context limit in the results window went from **32 to 64 lines** (the drill-down window too).
- **Fixed `uvp x.osm.pbf term -open` showing the pbf's binary in the window.** The window now opens the converted `.uwvz` (the OSM XML). A single compressed file such as `.gz` with `-open` is fixed the same way. Opening an unconverted pbf in the window no longer shows binary; it asks you to convert it first.

→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)
