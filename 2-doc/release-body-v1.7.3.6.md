*日本語 ｜ [English](#uwview-v1736--wide-field-english)*

## UwView v1.7.3.6 — Wide Field

**1本ずつだったのが、まとめて探せるようになりました。**
複数のファイルを一度に探し、平文と圧縮ファイルを混ぜても1回で済みます。その結果は、そのまま画面で読めます。
（前回の無料版は v1.6.6.1 です → [v1.6.6 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.6.6.md)。
直前に出した v1.7.3.5 は取り下げ、この版に置き換えました。v1.7.3.5 から足したのは、下の「キー操作」「ブックマークを残す」と Pro 側の修正です）

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

| 979MB の OSM XML を各形式で圧縮（hot・秒） | 標準の grep | ugrep | ripgrep | **`uvf`** |
|---|---:|---:|---:|---:|
| gz | 0.90（zgrep） | 0.45 | 0.43 | **0.33** |
| bz2 | 8.03（bzgrep） | 7.62 | 7.89 | **7.43** |
| xz | 4.01（xzgrep） | 3.64 | 4.21 | **3.37** |
| lzma | 3.21（xzgrep） | 2.83 | 3.40 | **2.57** |
| zst | 0.63（zstdgrep） | 0.65 | 0.54 | **0.50** |
| lz4 | 0.65（lz4 -dc｜grep） | 0.33 | 0.31 | **0.28** |
| br | 1.33（brotli -dc｜grep） | 0.91 | 0.87 | **0.74** |

**7形式とも、ugrep・ripgrep と同等でした**（差は 1.03〜1.36倍。1.5倍未満は差と呼びません）。差がつくのは各形式の標準の grep で、gz 2.73倍・lz4 2.32倍・br 1.80倍です。
`rg -z` は形式ごとに外部のコマンドを呼び、そのコマンドが無いと黙って飛ばしますが、`uvf` は外部のコマンドを使いません。

複数のファイル（gz 7本＋平文5本＝12本・展開後 約60GB・hot）では、`uvf` 17.60秒・ripgrep 24.62秒・ugrep 25.38秒・zgrep 89.35秒でした。ripgrep・ugrep とは同等（1.40倍・1.44倍）、zgrep より 5.08倍速い結果です。

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

### ⌨ キー操作

Mac は Ctrl を Cmd に読み替えます。

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
- 複数ファイルの結果のタブ（`uvf -open`）は、F5 で読み直しません（「このタブは読み直せません」と出ます）。
- キーの割り当ては、今の版では変えられません。

### 🔖 ブックマークを残す

- ブックマークをファイルごとに覚えて、同じファイルを開くと戻します（前回の続きを開かなくても戻ります）。
- 追記されただけのファイルなら戻します。中身が変わっていたら（作り直された・先頭が変わった）戻さずに、そう知らせます。見分けるのに読むのは、先頭と前回の末尾の手前の数 KB だけです。
- ブックマークを全部外すと、そのファイルの記録も消えます。覚えるのは最近の 500 ファイルまでです。
- ファイルメニュー「**ブックマークを書き出す…**」：`名前-bookmarks.txt` に「行番号<TAB>本文」で書き出します（行番号を数え終わってから使えます）。

### 🎛 画面のそのほかの変更

- **漢字で書いていたボタンを、アイコンにしました。** ボタンの名前は、マウスを乗せると出ます。はい／いいえ・開く／閉じる・保存・中止は、今までどおり文字のままです。
- 窓の題名が `UwView(uvf)-Wide Field(v1.7.3.6)` の形になり、版数が一目で分かるようになりました。

### 🔧 コマンド（`uvf`）のそのほかの変更

- **Mac と Linux の `uvf` を、ネイティブの1本の実行ファイルにしました。** Mac では、起動にかかる時間が 0.14秒から 0.01秒になりました。
- `uvf --tune`：この機械に合うスレッド数を実測します。`--apply` を付けると、測った値を設定に保存します。
- `uvf --help files`／`uvf --help regex`：ファイルの指定の仕方と、正規表現（.NET の書き方）のくわしい説明を出します。ripgrep で書いた正規表現は、書き換え方も案内します。
- Windows で、長いパス（260文字を超えるもの）を扱えるようにしました。

### ⚠️ 注意

- 平文を複数まとめて探すときは、**ripgrep の方が少し速い**という結果でした（平文5本で、`uvf` の時間が ripgrep の 1.17倍。1.5倍未満なので同等の範囲です。uvf 1.7.2.7・cold＋hot）。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- 画面の検索欄が探すのは、メインに出ている1ファイルだけです。複数のファイルを探し直すときは、コマンドから探してください。
- 秒数は、ある1台の Mac（Apple M4・10コア・外付け USB SSD）で 2026年9月28日に測った値です。圧縮7形式と12本は hot（各2回の速い方）で、uvf 1.7.3.4・uvp 1.7.3.5（検索の処理は 1.7.3.6 と同じ）、ugrep 7.8.5、ripgrep 15.2.0、macOS 標準の zgrep などと比べました。**秒数を環境をまたいで比べないでください。** 条件と全データは [uvf コマンド操作マニュアル](https://github.com/amru195704/UwView/blob/main/2-doc/uvf_コマンド操作マニュアル.md) の「性能」の章にあります。

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
```

Linux の場合と、手で入れる場合は、下のファイルを使ってください。チェックサムは `SHA256SUMS-1.7.3.6.txt` にあります。

```
16b36fa7008d1deef191c1f350497ed27f7809d71f3b706f4cfef72ddce7dab0  UwView-1.7.3.6-linux-aarch64.tar.gz
2d3cf0d2d29b0c95b73487c2073362b109907cb552763b8f91b542af0fcb6d4d  UwView-1.7.3.6-linux-x86_64.tar.gz
b36eba75e25cc73ba20d2302437418e00e9e1a33875b1b94ec34876d932b3612  UwView-1.7.3.6-mac-arm64.dmg
bc306b356a6d49f874212ed66ea69a0d845cf8ddcf25d45daf763006e7bef375  UwView-1.7.3.6-mac-x64.dmg
f5e3c505209586a327ecb841a0d8fb41a610824d85d25689e73c4a71ca475a29  UwView-1.7.3.6-win-arm64.zip
dce228dda2d82b2235080c73ec009524afa90ba329c4507b0dee35e61641caab  UwView-1.7.3.6-win-x64.zip
```

> **配布は GitHub Releases のみです。** Homebrew と Scoop も、ここからファイルを取得して SHA256 を照合します。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 束ねて、2回目から速く

有償版の `uvp` は、複数のファイルを **1本の `.uwvz` に束ねます**。束ねた `.uwvz` は、2回目からは作り直しません。`.zip` の中のテキストや OSM の `.pbf` も入力にできます。

| gz 7本＋平文5本＝12本（hot） | ripgrep（`-z`） | `uvf` | `uvp` 1回目（束ねる） | **`uvp` 2回目** |
|---|---:|---:|---:|---:|
| 秒 | 24.62秒 | 17.60秒 | 41.23秒 | **9.52秒** |

1回目は束ねた索引を書くので `uvf` の 2.34倍かかりますが、2回目からは `uvf` の 1.85倍速く、合計4回でほぼ並び、5回目から差がつきます。圧縮ファイル1本なら、2回目は形式によらず 0.11〜0.12秒です（bz2 で `uvf` より 62倍速い）。

`japan-latest.osm.pbf`（2.46GB）は、XML にして 51.3GB 分の内容になります。`uvp` はこれを、検索できる `.uwvz` に **21.6秒**で変えます。続けて「東京」（94,979件）を探し終えるまで、キャッシュを捨てた状態から合計 **27.34秒**。2回目からは `.uwvz` を使うので **5.01秒**（キャッシュを捨てた直後でも 6.43秒）です（2026年9月28日の実測・`uvp` 1.7.3.5。変換と検索の処理は 1.7.3.6 と同じ）。

- `uvp x.osm.pbf 語 -open` では、画面に変換した `.uwvz`（OSM の XML）が開きます。`.gz` などの圧縮ファイル 1 本も同じです。変換していない pbf を画面で開くと、「先に変換してください」と知らせます（v1.7.3.5 では、画面に元の pbf のバイナリが出ていました）。
- 結果の窓の前後 ±N は、**最大 64 行**です（多段階の窓も同じ。無料版は ±1 行）。
- 上のキー操作とブックマークは、Pro でも同じです（束ねた索引でもブックマークを覚えます）。編集中の本文では `[` `]` は文字として入り、F5 では読み直しません（束ねた索引とそこから開いたタブも読み直しません）。Pro には末尾追従が無いので、Ctrl+Shift+F は何もしません。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.7.3.6 — Wide Field (English)

**UwView used to take one file at a time. Now it searches many at once.**
You can search several files in one go and mix plain text with compressed files in the same run. The results can be read right in the window.
(The previous free release was v1.6.6.1 → [what changed in v1.6.6](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.6.6.md).
v1.7.3.5, released just before, has been withdrawn and replaced by this release. What was added since v1.7.3.5: "Keyboard shortcuts", "Bookmarks are remembered" and the Pro fixes below.)

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

| 979 MB of OSM XML in each format (hot, seconds) | Standard grep | ugrep | ripgrep | **`uvf`** |
|---|---:|---:|---:|---:|
| gz | 0.90 (zgrep) | 0.45 | 0.43 | **0.33** |
| bz2 | 8.03 (bzgrep) | 7.62 | 7.89 | **7.43** |
| xz | 4.01 (xzgrep) | 3.64 | 4.21 | **3.37** |
| lzma | 3.21 (xzgrep) | 2.83 | 3.40 | **2.57** |
| zst | 0.63 (zstdgrep) | 0.65 | 0.54 | **0.50** |
| lz4 | 0.65 (lz4 -dc \| grep) | 0.33 | 0.31 | **0.28** |
| br | 1.33 (brotli -dc \| grep) | 0.91 | 0.87 | **0.74** |

**In all seven formats `uvf` is on par with ugrep and ripgrep** (1.03–1.36× apart; under 1.5× is not called a difference). The gap is with each format's standard grep: 2.73× on gz, 2.32× on lz4, 1.80× on br.
`rg -z` calls an external command for each format and silently skips the file when that command is missing; `uvf` uses no external commands.

On many files (7 gz + 5 plain = 12 files, about 60 GB expanded, hot): `uvf` 17.60 s, ripgrep 24.62 s, ugrep 25.38 s, zgrep 89.35 s — on par with ripgrep and ugrep (1.40×, 1.44×), 5.08× faster than zgrep.

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

### ⌨ Keyboard shortcuts

On a Mac, read Ctrl as Cmd.

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
- Tabs opened from a multi-file result (`uvf -open`) are not reloaded by F5 ("This tab cannot be reloaded").
- Key assignments cannot be changed in this release.

### 🔖 Bookmarks are remembered

- Bookmarks are remembered per file and come back when you open the same file again (even without restoring the last session).
- If the file has only grown, they come back. If its contents changed (recreated, or the start changed), they are not restored and you are told so. Only a few KB at the start and just before the previous end are read to tell the difference.
- Removing every bookmark from a file also removes its record. The last 500 files are remembered.
- File menu → **Export bookmarks…** writes `name-bookmarks.txt` as `line number<TAB>text` (available once line numbering has finished).

### 🎛 Other changes in the window

- **Buttons that were labeled in Japanese are now icons.** Hover to see what a button does. Yes / No, Open / Close, Save and Cancel keep their text labels.
- The window title now shows the version, as `UwView(uvf)-Wide Field(v1.7.3.6)`.

### 🔧 Other changes in the command (`uvf`)

- **On Mac and Linux, `uvf` is now a single native executable.** On a Mac, startup went from 0.14 s to 0.01 s.
- `uvf --tune` measures the best thread count for this machine. Add `--apply` to save it to the settings.
- `uvf --help files` / `uvf --help regex` explain how to name files and how to write regular expressions (.NET syntax), including how to rewrite a pattern written for ripgrep.
- On Windows, paths longer than 260 characters now work.

### ⚠️ Notes

- When searching several plain-text files, **ripgrep was slightly faster**: on five plain files, `uvf` took 1.17× as long as ripgrep — under 1.5×, so on par (uvf 1.7.2.7, cold + hot).
- The free edition does not read `.zip`, `.tar.gz` or OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- The search box in the window searches only the file in the main tab. To search several files again, use the command.
- All timings come from one Mac (Apple M4, 10 cores, external USB SSD), measured on 28 September 2026. The seven formats and the 12 files are hot (faster of two runs), with uvf 1.7.3.4 and uvp 1.7.3.5 (same search code as 1.7.3.6) against ugrep 7.8.5, ripgrep 15.2.0 and the macOS standard zgrep and friends. **Do not compare seconds across machines.** Conditions and full data are in the "性能" (performance) chapter of the [uvf command manual](https://github.com/amru195704/UwView/blob/main/2-doc/uvf_コマンド操作マニュアル.md) (Japanese).

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
```

On Linux, or to install by hand, use the files below. Checksums are in `SHA256SUMS-1.7.3.6.txt`.

```
16b36fa7008d1deef191c1f350497ed27f7809d71f3b706f4cfef72ddce7dab0  UwView-1.7.3.6-linux-aarch64.tar.gz
2d3cf0d2d29b0c95b73487c2073362b109907cb552763b8f91b542af0fcb6d4d  UwView-1.7.3.6-linux-x86_64.tar.gz
b36eba75e25cc73ba20d2302437418e00e9e1a33875b1b94ec34876d932b3612  UwView-1.7.3.6-mac-arm64.dmg
bc306b356a6d49f874212ed66ea69a0d845cf8ddcf25d45daf763006e7bef375  UwView-1.7.3.6-mac-x64.dmg
f5e3c505209586a327ecb841a0d8fb41a610824d85d25689e73c4a71ca475a29  UwView-1.7.3.6-win-arm64.zip
dce228dda2d82b2235080c73ec009524afa90ba329c4507b0dee35e61641caab  UwView-1.7.3.6-win-x64.zip
```

> **Distribution is GitHub Releases only.** Homebrew and Scoop download from here and check the SHA256. Guides and news are on the blog (https://uvp.y42u.net/en/).

### 📣 On the UwView Pro side — bundle once, fast from the second search

The paid `uvp` bundles several files into **one `.uwvz`**, and does not rebuild it on later searches. It also reads text inside `.zip` files and OSM `.pbf`.

| 7 gz + 5 plain = 12 files (hot) | ripgrep (`-z`) | `uvf` | `uvp` 1st (bundling) | **`uvp` 2nd** |
|---|---:|---:|---:|---:|
| Seconds | 24.62 s | 17.60 s | 41.23 s | **9.52 s** |

The first run writes the bundled index, so it takes 2.34× as long as `uvf`; from the second run it is 1.85× faster than `uvf`, level at four searches in total and ahead from the fifth. For a single compressed file, the second search takes 0.11–0.12 s whatever the format (62× faster than `uvf` on bz2).

`japan-latest.osm.pbf` (2.46 GB) holds 51.3 GB of content as XML. `uvp` turns it into a searchable `.uwvz` in **21.6 s**. Finding 東京 (94,979 hits) takes **27.34 s** in total from a dropped cache; from the second search on it uses the `.uwvz` and takes **5.01 s** (6.43 s right after dropping the cache) (measured 28 September 2026 with `uvp` 1.7.3.5; conversion and search code as in 1.7.3.6).

- `uvp x.osm.pbf term -open` opens the converted `.uwvz` (the OSM XML) in the window. A single compressed file such as `.gz` works the same way. Opening an unconverted pbf in the window asks you to convert it first (in v1.7.3.5 the window showed the pbf's binary).
- The ±N context in the results window goes up to **64 lines** (the drill-down window too; the free edition allows ±1).
- The shortcuts and remembered bookmarks above work the same in Pro (bookmarks are remembered in a bundled index too). While editing, `[` and `]` are typed as characters and F5 does not reload (nor does it reload a bundled index or the tabs opened from it). Pro has no follow-the-end, so Ctrl+Shift+F does nothing.

→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)
