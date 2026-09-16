*日本語 ｜ [English](#english)*

## UwView V1.6.0

**ターミナルから使えるようになりました。**
無料版に `uvf` コマンドが付き、`.gz` をそのまま開けるようになります。
（前回の無料版は v1.5.1 です。v1.5.0 は Pro のみの版でした）

### 🧰 `uvf` — ターミナルで探して、当たったら画面に渡す

受け付ける形は **2つだけ**です。

```bash
uvf -open [ファイル] [検索語]     # 画面を起動。ファイルがあれば開き、検索語があれば検索まで
uvf ファイル 検索語 [-open]       # 検索して結果を出す。-open を付けると結果を画面で表示
```

| | 内容 |
|---|---|
| 出力 | **`行番号<TAB>本文`**。1行は省略せず全部出します（画面表示の8,192文字打ち切りは適用しません） |
| 終了コード | **grep と同じ**。`0`＝見つかった／`1`＝見つからない／`2`＝エラー |
| 検索の種類 | 画面と同じ**普通の文字列検索**（大文字小文字を区別・正規表現ではありません） |
| 上限 | **100万件で打ち切り、`2` を返します**。出力が不完全なことをスクリプトが成功と取り違えないためです |
| 圧縮ファイル | `.gz` は `uvf` の検索では受け付けません（`uvf -open file.gz` で画面から開いてください） |

```bash
if uvf app.log 'FATAL'; then
  uvf -open app.log 'FATAL'      # 当たったときだけ、人が見る画面を出す
fi
```

`-open` で画面に渡したときは、画面側で**同じ検索をやり直す**ので結果は一致します。

**名前だけで打てるようにするには、ヘルプの「コマンドライン設定…」**（英語版は *Command line setup…*）で登録してください。
同じ画面から解除もできます。`uvf` は本体を `--uvf` 付きで起動するだけの小さな起動用スクリプトで、
**.NET をもう1組抱えないので、配布物のサイズは増えていません**。

#### 速さの道具ではありません

無料版は索引を持たないので、**50GB の検索は `uvf` で 205秒**（開く＋検索＋出力の合計）、
**ripgrep なら 56秒**です。ripgrep があるなら、速さを求める場面では ripgrep を使ってください。

**`uvf` の値打ちは、終了コードと `-open`** です。スクリプトで拾って、当たったときだけ人が画面で見る——
その受け渡しのためにあります。絞り込み（2語）・正規表現・大小無視・頻度集計・順序検索・`-out .gz`・
`.uwvz` の読み書きは **Pro の `uvp`** の機能です。

### 🗜 `.gz` をそのまま開く

`.gz` を開くと「どう開きますか？」と聞きます。**「テキストに展開して開く」**を選ぶと、
同じフォルダに展開したファイルを作ってから開きます（`gunzip` してから開くのと同じ結果）。
Pro が入っていれば「**.uwvz に変換して開く**」も選べます（平文を作らず、ディスク約1/9、次回から瞬時）。

**黙って失敗させません。**次のものは、理由を出して受け付けません。

| 受け付けないもの | 出るメッセージの主旨 |
|---|---|
| 途中で切れている `.gz` | 展開後に gzip の照合値（CRC）が合わない → **「途中までのファイルは残していません」** |
| `.tar.gz` ／ `.tgz`（中身が tar） | 「tar は対応していません（先に展開してください）」 |
| 二重に gzip されたもの | 「gzip が二重にかかっています」 |
| 拡張子だけ `.gz` のファイル | 「gzip ではありません（先頭の目印が違います）」 |
| `.zip` | **「準備中です（今後の版で対応します）」** — エントリ選択を次の版で入れます |

判定は拡張子ではなく**中身の先頭**を見ています。展開後のサイズが空き容量を超えそうなときは、
先に「最大◯GB になる見込み、空きは◯GB」と出して確認します（続行は止めません）。

### 🔎 正規表現の検索を速くしました

これまで正規表現の検索は、**当たりようのない行まで含めて全行を文字に変換**してから判定していました。
v1.6.0 では、パターンから**必ず含まれる文字列**を取り出し、**まずその文字列をバイト列のまま探して候補行を絞り**、
候補行だけを変換して確定します（ripgrep が memchr／SIMD でやっていることの自前版）。
必須文字列が取り出せないパターン（`^\d+` など）は、これまでどおりの経路に落とします。

実測は Pro の CLI で取っています（50GB・`.uwvz` 再利用・2回目）。**13.68秒 → 7.75秒**、
固定文字列の 7.15秒とほぼ並びました。**無料版と Pro はこの検索コードを共有しています**が、
無料版は索引を持たないため絶対値は異なります（無料版単体での再測定はまだしていません）。

### 🔧 その他

- **シンボリックリンクで指定したファイルが開けなかったのを直しました。**リンク自身ではなくリンク先の
  大きさを見るようにしています（以前は mmap の容量不足で落ちていました）
- `uvf` の表示言語は、アプリの言語設定（日本語／English）に従います

### 📥 ダウンロード

| OS | ファイル |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.0-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.0-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.0-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.0-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.0-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.0-linux-aarch64.tar.gz` |

.NET のインストールは要りません（自己完結型）。
macOS 版は **Developer ID 署名・Apple 公証済み**の DMG です（開いて `UwView.app` を「アプリケーション」へ）。
Windows 版は未署名です（SmartScreen が出たら「詳細情報」→「実行」）。

チェックサムは `SHA256SUMS-1.6.0.txt`。同じアーカイブはリポジトリの
[`dist/`](https://github.com/amru195704/UwView/tree/main/dist) にも同梱しています。

```
9a47dc0809053217da94ce5cf39227cfed63b64bf295bb84486929e3f9a4197a  UwView-1.6.0-linux-aarch64.tar.gz
7663e9e19c254dddc103e247839dadf0fdd73865598fd4cd974189ae548f3154  UwView-1.6.0-linux-x86_64.tar.gz
1cbb0ac8abaad4c24ebd98e4a5007e8c7658bd30512d566f745f7a01370ff8e7  UwView-1.6.0-mac-arm64.dmg
dc7a146de123d491460e888dad6e7fb588aff329a8cae91ebc13b453a95036ff  UwView-1.6.0-mac-x64.dmg
d32c68a0f68ac7b0bc8d9b8d62b9bc47c64dd44f37376e87943692e6f8d18d85  UwView-1.6.0-win-arm64.zip
b36692c8e1529b8613e12f07c2e8c3e40d65c2dde6a43c91493444c84b5277c6  UwView-1.6.0-win-x64.zip
```

### 📣 有料版 UwView Pro も v1.6.0 になりました

Pro には CLI **`uvp`** が付きました。段を並べて絞り込み・集計・順序検索まで書けます。
ripgrep と同じ行が出ることを確認済みで（81組み合わせで出力一致）、**50GB では ripgrep の約7倍**
（2回目・`.uwvz` 再利用）。`-replace` は `sed -E`（後方参照つき）の**約5.8倍**です。
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9。**14日間の無料試用**あり）

---

<a name="english"></a>

## UwView v1.6.0 (English)

**This release brings UwView to the terminal.**
The free build now has a `uvf` command, and it opens `.gz` files directly.
(The previous free release was v1.5.1; v1.5.0 was a Pro-only version.)

### 🧰 `uvf` — search from the terminal, hand the hit to the window

**Only two forms** are accepted.

```bash
uvf -open [file] [pattern]       # launch the app; open the file and search if given
uvf file pattern [-open]         # search and print the results (-open shows them in the app)
```

| | |
|---|---|
| Output | **`line number<TAB>text`**. Lines are printed in full (the 8,192-character display truncation does not apply) |
| Exit codes | **The same as grep**: `0` = found, `1` = not found, `2` = error |
| Kind of search | The same **plain string search** as the window (case-sensitive, not a regular expression) |
| Limit | **Cut off at 1,000,000 hits, returning `2`**, so a script cannot mistake an incomplete result for success |
| Compressed files | `uvf` will not search a `.gz` — use `uvf -open file.gz` to open it in the window |

```bash
if uvf app.log 'FATAL'; then
  uvf -open app.log 'FATAL'      # only when there is something to look at
fi
```

When `-open` hands over to the window, the window **runs the same search again**, so the results match.

**To call it by name, register it from Help → "Command line setup…"** (日本語版は「コマンドライン設定…」).
The same dialog removes it again. `uvf` is a tiny launcher that starts the app with `--uvf`, so
**it adds no size to the download** — a separate CLI executable would have carried a second copy of .NET.

#### It is not a speed tool

The free build keeps no index, so **searching 50 GB takes `uvf` 205 seconds** (open + search + output)
against **56 seconds for ripgrep**. If you have ripgrep and you want speed, use ripgrep.

**What `uvf` is for is the exit code and `-open`** — letting a script decide, and putting a person in
front of the window only when there is something to see. Narrowing (two terms), regular expressions,
case-insensitive search, tallies, sequence search, `-out .gz` and reading/writing `.uwvz` belong to
**`uvp`** in UwView Pro.

### 🗜 Opening `.gz` directly

Opening a `.gz` asks how you want it opened. **"Expand to text and open"** writes the expanded file
next to the original and opens that (the same result as `gunzip` then open). With Pro installed,
**"Convert to .uwvz and open"** is also offered (no plain text is ever written, about 1/9 the disk,
instant to reopen).

**Nothing fails silently.** These are refused, with the reason shown:

| Refused | What it says |
|---|---|
| A truncated `.gz` | The gzip checksum (CRC) does not match after expanding → **"no partial file was left behind"** |
| `.tar.gz` / `.tgz` (a tar inside) | "tar is not supported — please extract it first" |
| Doubly gzip-compressed data | "gzip-compressed twice, which is not supported" |
| A file that is only named `.gz` | "not a gzip file (wrong magic bytes)" |
| `.zip` | **"not available yet (coming in a later version)"** — entry selection lands in the next release |

The decision is made from **the bytes at the head of the file**, not the extension. If the expanded
file looks likely to exceed the free space, you are told first ("may need up to X GB, only Y GB free")
and can still continue.

### 🔎 Regular-expression search is faster

A regex search used to **decode every line to text**, including lines that could not possibly match,
and then test it. v1.6.0 extracts the **literal that the pattern must contain**, finds candidate lines
**by scanning for those bytes directly**, and decodes only the candidates to confirm — our own version
of what ripgrep does with memchr and SIMD. Patterns with no extractable literal (`^\d+` and the like)
fall back to the previous path.

The measurement comes from the Pro CLI (50 GB, second run, reusing `.uwvz`): **13.68 s → 7.75 s**,
essentially level with a fixed-string search at 7.15 s. **The free build shares this search code**,
though its absolute times differ because it keeps no index (the free build has not been re-measured
on its own yet).

### 🔧 Other changes

- **Fixed: a file given through a symbolic link would not open.** The size is now taken from the link's
  target rather than the link itself (previously this crashed with an mmap capacity error)
- `uvf` follows the app's language setting (Japanese / English) for its messages

### 📥 Downloads

| OS | File |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.0-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.0-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.0-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.0-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.0-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.0-linux-aarch64.tar.gz` |

No .NET installation is required (self-contained).
The macOS build is a DMG **signed with a Developer ID and notarized by Apple** — open it and drag
`UwView.app` to Applications. The Windows build is unsigned (if SmartScreen appears, choose
**More info → Run anyway**).

Checksums: `SHA256SUMS-1.6.0.txt`. The same archives are also in
[`dist/`](https://github.com/amru195704/UwView/tree/main/dist).

```
9a47dc0809053217da94ce5cf39227cfed63b64bf295bb84486929e3f9a4197a  UwView-1.6.0-linux-aarch64.tar.gz
7663e9e19c254dddc103e247839dadf0fdd73865598fd4cd974189ae548f3154  UwView-1.6.0-linux-x86_64.tar.gz
1cbb0ac8abaad4c24ebd98e4a5007e8c7658bd30512d566f745f7a01370ff8e7  UwView-1.6.0-mac-arm64.dmg
dc7a146de123d491460e888dad6e7fb588aff329a8cae91ebc13b453a95036ff  UwView-1.6.0-mac-x64.dmg
d32c68a0f68ac7b0bc8d9b8d62b9bc47c64dd44f37376e87943692e6f8d18d85  UwView-1.6.0-win-arm64.zip
b36692c8e1529b8613e12f07c2e8c3e40d65c2dde6a43c91493444c84b5277c6  UwView-1.6.0-win-x64.zip
```

### 📣 UwView Pro is also at v1.6.0

Pro gained the **`uvp`** CLI — stages you can chain for narrowing, tallies and sequence search.
Its output has been verified line-for-line against ripgrep (81 combinations matched), and at 50 GB it
runs about **7× faster than ripgrep** (second run, reusing `.uwvz`). `-replace` is about **5.8× faster
than `sed -E`** with back-references.
→ [UwView Pro](https://uvp.y42u.net/pro/) ($129 one-time / $9 per month, with a **14-day free trial**)
