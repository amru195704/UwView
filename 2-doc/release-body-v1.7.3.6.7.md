*日本語 ｜ [English](#uwview-v17367--wide-field-english)*

## UwView v1.7.3.6.7 — Wide Field

**小さなファイルが大量にある場所の検索が、6〜8倍速くなりました。**
Linux カーネルのソース（8.6万ファイル）で、`uvf` の検索が hot で 26.58秒から 3.40秒になりました。あわせて、正規表現と大小無視の併用（`-E -i`）が ripgrep と同等の速さになりました。画面の使い方と出力の形は変わりません。
（前回の無料版は v1.7.3.6 です → [v1.7.3.6 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.md)）

### 📂 小さなファイルが大量にある場所を、速く探す

```bash
uvf 'src/linux/**' PageTransHuge
```

| Linux カーネルのソース 8.6万本・`PageTransHuge`（5行）・秒 | 1回目（cold） | 2回目（hot） |
|---|---:|---:|
| `uvf` 前の版（1.7.3.6.5） | 38.65 | 26.58 |
| **`uvf` v1.7.3.6.7** | **6.69** | **3.40** |
| 速くなった倍率 | 5.8倍 | 7.8倍 |
| （参考）ripgrep 15.2.0 | 5.50 | 1.44 |

**遅かった原因は、検索ではなく「ファイルを開く回数」でした。** 前の版は、検索の前に「OSM の pbf ではないか」「圧縮ファイルか」を確かめるために、8.6万本を1本ずつ順に何度も開いていました。開いて閉じるだけで、1回あたり約7.7秒かかります。
今の版は、名前で形式が分かるもの（`.gz`・`.zip`・`.pbf` など）だけを先に確かめます。それ以外は、検索のために開いたときに先頭を見て決めます。名前は普通でも中身が圧縮のもの（zstd で圧縮した `app.log.1` など）は、今までどおり展開して探します。

くわしい経緯は Zenn の記事にまとめました → [Linuxカーネル8.6万ファイルでripgrepの19倍遅かった自作grepを、「開く回数」を数えて7倍速くした](https://zenn.dev/amru195704/articles/5f2a6203ec7f61)

### 🔎 正規表現と大小無視の併用（`-E -i`）が、ripgrep と同等に

```bash
uvf japan.osm 'K="NAME[^"]*"' -E -i
```

| OSM の XML（hot・秒） | ripgrep | `uvf` 前の版（1.7.3.6.6） | **`uvf` v1.7.3.6.7** | ripgrep との差 |
|---|---:|---:|---:|---|
| 3GB | 0.55 | 1.09 | **0.61** | 1.91倍 → **1.11倍（同等）** |
| 10GB | 1.81 | 2.83 | **1.94** | 1.68倍 → **1.07倍（同等）** |

（ripgrep との差は、`uvf` の時間が ripgrep の何倍かです。1.5倍未満は差と呼びません）

- 大小無視の手がかり（`="name`）から目印にする文字を選ぶとき、記号の `=` と `"` を選んでいました。XML では、この2文字の並びがほぼすべての属性にあるので、候補が3GBで1億2千万か所を超えていました。今の版は、手がかりに英字があれば必ず英字から目印を選びます。
- 結果は ripgrep と完全に一致します（3GB で 491,643行、10GB で 906,747行）。
- 50GB はメモリより大きく、ディスクの速さで決まるので変わりません（もともと ripgrep と同等です）。

### 📊 巨大な1本のファイルでは、7パターンすべてが ripgrep と同等以上

3GB・10GB・50GB の OSM XML で、7種類の検索（素の検索・`-i`・`-E`・行頭つき・`-E -i`・`-v`・`-E -v`）を、1回目（cold）と2回目（hot）の合計で比べました。

| 7パターンの合計（cold＋hot・秒） | ripgrep | **`uvf` v1.7.3.6.7** |
|---|---:|---:|
| 3GB | 31.33 | **31.22** |
| 10GB | 95.44 | **84.60** |
| 50GB | 833.96 | **757.25** |

1.5倍を超えて ripgrep に負けた項目は、ありませんでした。結果はすべて ripgrep と一致しています。

### 🔧 そのほかの変更

- **大小無視（`-i`）で、目印を2つにしました。** 16バイトずつ、2つの目印が両方合う位置だけを拾います。ソースコードのように、探す語のどの文字もよく出る場合に速くなります。
- ワイルドカードで広げたときは、シンボリックリンクを対象から外します（ripgrep と同じ扱いです）。
- 名前は普通でも中身が pbf や zip のファイルは、「扱えません」という案内が**検索結果の後**に出るようになりました。名前で分かるもの（`.pbf`・`.zip`）は、今までどおり検索の前に出ます。
- 出力は1バイトも変えていません。テストは uvf 541件・画面 64件がすべて通っています。

### ⚠️ 注意

- **小さなファイルが大量にある場所では、まだ ripgrep の方が速いです。** Linux カーネルのソースで5種類の検索を cold＋hot で合計すると、ripgrep 31.50秒・`uvf` 52.18秒で、`uvf` の時間は ripgrep の 1.66倍でした。cold だけなら 1.2〜1.8倍ですが、hot では 2.1〜2.4倍です。同じ場所を何度も探すなら、索引を持つ UwView Pro（下）が向いています。
- 1文字の `k` を大小無視で探すような、目印にできる部分が無い検索は速くなっていません（8.6万本で約7秒）。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- 秒数は、ある1台の Mac（Apple M4・10コア・メモリ 32GB・外付け USB SSD）で 2026年10月3日に測った値です。cold はキャッシュを捨てて（`sudo purge`）10秒待ってからの1回目、hot は続けて走らせた2回目です。前の版の値は、同じ機械・同じスクリプトで 10月2〜3日に測りました。**秒数を環境をまたいで比べないでください。**

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
```

Linux の場合と、手で入れる場合は、下のファイルを使ってください。チェックサムは `SHA256SUMS-1.7.3.6.7.txt` にあります。

```
692f1e15e6c38379ec1b4d5ba88ef7114b9b7465cf194461569d0d26d1c5c768  UwView-1.7.3.6.7-linux-aarch64.tar.gz
5cfe043fd8f52649ea8d1592f77835aba82cd066d509235d378698e7e62f7332  UwView-1.7.3.6.7-linux-x86_64.tar.gz
e851ab4f98c38e6419d77ca2a3f90aeca9cfdb33b64660d2a9989db9882917b9  UwView-1.7.3.6.7-mac-arm64.dmg
9fd63368d3985019a4f5dd831cd679c38d4521e15b55c237015952101ab22e75  UwView-1.7.3.6.7-mac-x64.dmg
8d13094f164eb90464a6eba067aeb79415799a9b429cd69f2b58d5dd7678e870  UwView-1.7.3.6.7-win-arm64.zip
7c9671e0d5b96d99ae312cf04f859d8c396d1748db56ee4c0b27ea4349f47145  UwView-1.7.3.6.7-win-x64.zip
```

> **配布は GitHub Releases のみです。** Homebrew と Scoop も、ここからファイルを取得して SHA256 を照合します。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 8.6万本を束ねて、2回目から ripgrep より速く

有償版の `uvp` は、8.6万本を **1本の `.uwvz` に束ねられる**ようになりました（v1.7.3.6 では、索引に書く「どのファイルから来たか」の一覧が上限を超えて止まっていました）。

| Linux カーネルのソース 8.6万本 | 前 | **`uvp` v1.7.3.6.7** |
|---|---:|---:|
| 束ねた索引を作る（1問目の検索込み・cold） | 56.31秒（1.7.3.6.4） | **8.58秒** |
| 2回目以降・元の指定で（変わっていないかの確認込み・hot） | 約27秒（1.7.3.6.1） | **0.74〜0.78秒** |
| 2回目以降・`.uwvz` を直接（hot） | — | **0.28〜0.32秒** |

- 5種類の検索を cold＋hot で合計すると、ripgrep 31.50秒・`uvp` 11.65秒（索引作りを含む）で、**ripgrep の 2.7倍速い**結果でした。
- 大小無視（`-i`）は、`spinlock -i` で 1.27秒から 0.30秒になりました（4.2倍。1.7.3.6.2 → 1.7.3.6.6）。
- 巨大な1本のファイル（上の7パターン、cold＋hot の合計）では、3GB で ripgrep の 2.07倍、10GB で 3.19倍、50GB で 3.54倍速い結果でした。
- `uvp convert -out` で、指定した場所のほかに、元のファイルの隣にも `.uwvz` ができていた点を直しました。
- 1本ものの `.uwvz` を直接 `-H` で探すと、`….uwvz` ではなく元のファイル名で出るようにしました（束ねた `.uwvz` と同じです）。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.7.3.6.7 — Wide Field (English)

**Searching a folder of many small files is now 6–8× faster.**
On the Linux kernel source (86,000 files), a `uvf` search went from 26.58 s to 3.40 s (hot). Regex plus ignore-case (`-E -i`) is now on par with ripgrep too. Nothing changes in how you use the window, and the output format is the same.
(The previous free release was v1.7.3.6 → [what changed in v1.7.3.6](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.md))

### 📂 Fast search over many small files

```bash
uvf 'src/linux/**' PageTransHuge
```

| Linux kernel source, 86,000 files, `PageTransHuge` (5 lines), seconds | 1st run (cold) | 2nd run (hot) |
|---|---:|---:|
| `uvf` previous (1.7.3.6.5) | 38.65 | 26.58 |
| **`uvf` v1.7.3.6.7** | **6.69** | **3.40** |
| Speed-up | 5.8× | 7.8× |
| (For reference) ripgrep 15.2.0 | 5.50 | 1.44 |

**The slow part was not the search. It was how many times each file was opened.** Before searching, the previous release opened all 86,000 files one by one, several times over, to check "is this an OSM pbf?" and "is this compressed?". Just opening and closing them all takes about 7.7 s per pass.
Now only files whose name settles the format (`.gz`, `.zip`, `.pbf` and so on) are checked up front. Everything else is decided from the first bytes when the file is opened for the search itself. A file with an ordinary name but compressed content (such as a zstd-compressed `app.log.1`) is still decompressed and searched, as before.

The full story is in a Zenn article (Japanese) → [Linuxカーネル8.6万ファイルでripgrepの19倍遅かった自作grepを、「開く回数」を数えて7倍速くした](https://zenn.dev/amru195704/articles/5f2a6203ec7f61)

### 🔎 Regex plus ignore-case (`-E -i`) is now on par with ripgrep

```bash
uvf japan.osm 'K="NAME[^"]*"' -E -i
```

| OSM XML (hot, seconds) | ripgrep | `uvf` previous (1.7.3.6.6) | **`uvf` v1.7.3.6.7** | Gap to ripgrep |
|---|---:|---:|---:|---|
| 3 GB | 0.55 | 1.09 | **0.61** | 1.91× → **1.11× (on par)** |
| 10 GB | 1.81 | 2.83 | **1.94** | 1.68× → **1.07× (on par)** |

(The gap is how many times as long `uvf` takes as ripgrep. Under 1.5× is not called a difference.)

- When picking anchor characters from the ignore-case clue (`="name`), `uvf` chose the symbols `=` and `"`. In XML that pair appears in almost every attribute, so a 3 GB file produced over 120 million candidates. Now, if the clue contains letters, the anchors are always letters.
- Results match ripgrep exactly (491,643 lines on 3 GB, 906,747 lines on 10 GB).
- 50 GB is larger than memory and limited by the disk, so it does not change (it was already on par with ripgrep).

### 📊 On one huge file, all seven patterns are on par with ripgrep or better

On 3 GB, 10 GB and 50 GB OSM XML, seven searches (plain, `-i`, `-E`, anchored, `-E -i`, `-v`, `-E -v`) were compared on the first (cold) plus second (hot) run.

| Seven patterns, cold + hot (seconds) | ripgrep | **`uvf` v1.7.3.6.7** |
|---|---:|---:|
| 3 GB | 31.33 | **31.22** |
| 10 GB | 95.44 | **84.60** |
| 50 GB | 833.96 | **757.25** |

No item lost to ripgrep by more than 1.5×. Every result matched ripgrep.

### 🔧 Other changes

- **Ignore-case (`-i`) now uses two anchors.** It scans 16 bytes at a time and keeps only the positions where both anchors match. This helps when every character of the word is common, as in source code.
- When a wildcard is expanded, symbolic links are left out (the same as ripgrep).
- For a file with an ordinary name whose content is a pbf or zip, the "cannot read" message now comes **after the search results**. Files whose name gives it away (`.pbf`, `.zip`) are still reported before the search.
- The output has not changed by a single byte. All tests pass: 541 for uvf and 64 for the window.

### ⚠️ Notes

- **On many small files, ripgrep is still faster.** Five searches on the Linux kernel source, cold + hot combined: ripgrep 31.50 s, `uvf` 52.18 s — `uvf` takes 1.66× as long. Cold alone it is 1.2–1.8×, hot alone 2.1–2.4×. If you search the same place again and again, UwView Pro with its index (below) is the better fit.
- Ignore-case searches with nothing to anchor on, such as the single letter `k` with `-i`, are not faster (about 7 s on 86,000 files).
- The free edition does not read `.zip`, `.tar.gz` or OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- All timings come from one Mac (Apple M4, 10 cores, 32 GB memory, external USB SSD), measured on 3 October 2026. Cold is the first run after dropping the cache (`sudo purge`) and waiting 10 s; hot is the run straight after. Previous-version numbers were measured on the same machine with the same scripts on 2–3 October. **Do not compare seconds across machines.**

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
```

On Linux, or to install by hand, use the files below. Checksums are in `SHA256SUMS-1.7.3.6.7.txt`.

```
692f1e15e6c38379ec1b4d5ba88ef7114b9b7465cf194461569d0d26d1c5c768  UwView-1.7.3.6.7-linux-aarch64.tar.gz
5cfe043fd8f52649ea8d1592f77835aba82cd066d509235d378698e7e62f7332  UwView-1.7.3.6.7-linux-x86_64.tar.gz
e851ab4f98c38e6419d77ca2a3f90aeca9cfdb33b64660d2a9989db9882917b9  UwView-1.7.3.6.7-mac-arm64.dmg
9fd63368d3985019a4f5dd831cd679c38d4521e15b55c237015952101ab22e75  UwView-1.7.3.6.7-mac-x64.dmg
8d13094f164eb90464a6eba067aeb79415799a9b429cd69f2b58d5dd7678e870  UwView-1.7.3.6.7-win-arm64.zip
7c9671e0d5b96d99ae312cf04f859d8c396d1748db56ee4c0b27ea4349f47145  UwView-1.7.3.6.7-win-x64.zip
```

> **Distribution is GitHub Releases only.** Homebrew and Scoop download from here and check the SHA256. Guides and news are on the blog (https://uvp.y42u.net/en/).

### 📣 On the UwView Pro side — bundle 86,000 files, faster than ripgrep from the second search

The paid `uvp` can now **bundle 86,000 files into one `.uwvz`** (in v1.7.3.6 the list of source files written into the index went over its limit and the run stopped).

| Linux kernel source, 86,000 files | Before | **`uvp` v1.7.3.6.7** |
|---|---:|---:|
| Build the bundled index (including the first search, cold) | 56.31 s (1.7.3.6.4) | **8.58 s** |
| Later searches, same file pattern (including the change check, hot) | about 27 s (1.7.3.6.1) | **0.74–0.78 s** |
| Later searches, the `.uwvz` directly (hot) | — | **0.28–0.32 s** |

- Five searches, cold + hot combined: ripgrep 31.50 s, `uvp` 11.65 s (including building the index) — **2.7× faster than ripgrep**.
- Ignore-case (`-i`): `spinlock -i` went from 1.27 s to 0.30 s (4.2×, 1.7.3.6.2 → 1.7.3.6.6).
- On one huge file (the seven patterns above, cold + hot): 2.07× faster than ripgrep on 3 GB, 3.19× on 10 GB and 3.54× on 50 GB.
- Fixed `uvp convert -out` also writing a `.uwvz` next to the original file, besides the place you named.
- Searching a single-file `.uwvz` directly with `-H` now shows the original file name instead of `….uwvz` (the same as a bundled `.uwvz`).

→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)
