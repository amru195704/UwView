*日本語 ｜ [English](#english)*

## UwView V1.6.3

**`uvf` が ripgrep と同じ速さになりました。**
無料版のコマンド `uvf` を作り直し、`-i`／`-E`／`-v` を追加しています。
（前回の無料版は v1.6.0 です。v1.6.1・v1.6.2 は配布していません）

### ⚡ 1回読みに作り直しました

v1.6.2 までの `uvf` は、①索引を作る（全体を読む）②検索する（もう一度読む）③ヒット行の本文を読み直す、と
**2回半**ファイルを読んでいました。50GB の 203 秒はその合計です（索引 約100秒＋検索 約82秒＋出力 約20秒）。

v1.6.3 は通しの**1回読み**の最中に、改行を数える（＝行番号）・一致を判定する・その行の本文を渡す、を同時に行います。
読み出しは `pread` の連続読みなので、**媒体の帯域がそのまま速度になります**。メモリは 50MB 程度しか使いません。

**速さ**（Mac M4・外付け USB SSD・OSM 日本・固定文字列「東京」・コールドは `sudo purge` 直後、ホットは続けて2回目）:

| | ripgrep 15.2.0 コールド／ホット | `uvf` コールド／ホット | `uvf` の読み出し速度 |
|---|---:|---:|---:|
| 3GB | 3.26秒／0.33秒 | 3.32秒／0.55秒 | 871／5,259 MB/s |
| 10GB | 10.96秒／10.89秒 | **10.48秒／10.25秒** | 933／954 MB/s |
| 50GB | 54.76秒／55.15秒 | **50.82秒／50.63秒** | 962／965 MB/s |

**負けるところも書いておきます。** 3GB がメモリに載っているとき（ホット）は ripgrep が速く、0.33秒 対 0.55秒です。
10GB の `-v`（当てはまらない行）のホットにも、ripgrep が速い組み合わせが1つあります。
それ以外は同じか、`uvf` がわずかに速い、という結果でした。

v1.6.0 のリリースで「速さの道具ではありません」と書きました。**その前提は無くなりました。**

### 🔤 `-i` ／ `-E` ／ `-v` を追加しました

v1.6.2 までの `uvf` は、素の文字列検索だけでした。`uvp`（Pro）と同じ綴りで3つ足しています。

| | 意味 | grep でいうと |
|---|---|---|
| `-i` | 大文字小文字を区別しない | `grep -i` |
| `-E` | 検索語を正規表現として扱う | `grep -E` |
| `-v` | 当てはまら**ない**行を出す | `grep -v` |

出力（`行番号<TAB>本文`）と終了コード（`0`／`1`／`2`）はこれまでどおりです。
`-i` は ASCII の英字だけを畳む経路を通るので、素の文字列検索とほぼ同じ速さで動きます。
`-E` は、パターンから**必ず含まれる文字列**を取り出して候補行をバイト列のまま絞り、候補だけを文字に変換して確定します。

**50GB での秒数**（コールド／ホット・`uvf` 対 ripgrep）:

| 検索 | `uvf` | ripgrep |
|---|---:|---:|
| `-i` 大小無視 | 51.58／51.40秒 | 56.64／56.73秒 |
| `-E` 正規表現 | 51.70／51.55秒 | 55.35／55.90秒 |
| `-v` 当てはまらない | 52.15／51.96秒 | 57.89／58.04秒 |

出力が ripgrep と**同じ行になること**を、3サイズ（3／10／50GB）× 7パターンで確認しています。

### 🔢 打ち切りの既定を「無制限」にしました

`uvf` は 100万件で打ち切る既定でしたが、**既定を無制限**に変えました。
上限（設定で変えられます）に当たって打ち切った場合は、これまでどおり `2` を返します——
出力が不完全なことを、スクリプトが成功と取り違えないためです。

### 🔧 その他

- `-open` は `-i`／`-E`／`-v` とは併用できません（画面側は同じ検索をやり直す作りのため）
- 名前だけで打てるようにするには、ヘルプの**「コマンドライン設定…」**（英語版は *Command line setup…*）で登録してください

### 📥 ダウンロード

| OS | ファイル |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.3-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.3-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.3-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.3-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.3-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.3-linux-aarch64.tar.gz` |

.NET のインストールは要りません（自己完結型）。
macOS 版は **Developer ID 署名・Apple 公証済み**の DMG です（開いて `UwView.app` を「アプリケーション」へ）。
Windows 版は未署名です（SmartScreen が出たら「詳細情報」→「実行」）。

チェックサムは `SHA256SUMS-1.6.3.txt`。同じアーカイブはリポジトリの
[`dist/`](https://github.com/amru195704/UwView/tree/main/dist) にも同梱しています。

```
65cd06cfe4e646bf3d4daa425f768204dcc46a260bf6fcc776ec78cb1ae08481  UwView-1.6.3-linux-aarch64.tar.gz
25a214f105c46aec862d1f2377bbcc925dde32d4ab3140753706aa9065a6a758  UwView-1.6.3-linux-x86_64.tar.gz
533b5364e1661cda89a794e14656c21618ee9eebf1d9f6ad202d8d94b7d590d8  UwView-1.6.3-mac-arm64.dmg
6bc60bc581dabed8d09bb1d47a1aeae8153e043ce3a98917d7434053ae51ed01  UwView-1.6.3-mac-x64.dmg
c2b15b7b58aeb054b0565f4d1876c86766b7a53aab0e2c09f9291d7a35984ac3  UwView-1.6.3-win-arm64.zip
b34a687c3b532618e77c2e380227c6667692f1a6755867fec1364fc50e978245  UwView-1.6.3-win-x64.zip
```

### 📣 索引を持つ側 — UwView Pro

同じ 50GB を、`.uwvz`（索引つき圧縮キャッシュ）がある状態で `uvp` が検索すると **6.34秒**です（ripgrep 55.15秒）。
`uvf` の 50.63秒と比べると8倍の差で、これが「索引を作る／作らない」の差です。
絞り込み・頻度集計・順序検索・`-out .gz`・`.uwvz` の読み書きは Pro の機能です。
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9。**14日間の無料試用**あり）

---

<a name="english"></a>

## UwView v1.6.3 (English)

**`uvf` now runs at ripgrep speed.**
The free edition's command has been rebuilt, and `-i` / `-E` / `-v` are new.
(The previous free release was v1.6.0; v1.6.1 and v1.6.2 were not distributed.)

### ⚡ Rebuilt as a single pass

Up to v1.6.2, `uvf` read the file **two and a half times**: once to build a line index, once to search,
and once more per hit to fetch the line text. The 203 seconds at 50 GB was that sum
(about 100 s indexing, 82 s searching, 20 s output).

v1.6.3 does all three in **one pass**: counting newlines (the line number), testing the match, and handing
over the text of that line. Reads go through sequential `pread`, so **the speed of the drive is the speed of
the search**. It uses about 50 MB of memory.

**Speed** (Mac M4, external USB SSD, OSM Japan, fixed string, cold = right after `sudo purge`, hot = the run after it):

| | ripgrep 15.2.0 cold / hot | `uvf` cold / hot | `uvf` read rate |
|---|---:|---:|---:|
| 3 GB | 3.26 s / 0.33 s | 3.32 s / 0.55 s | 871 / 5,259 MB/s |
| 10 GB | 10.96 s / 10.89 s | **10.48 s / 10.25 s** | 933 / 954 MB/s |
| 50 GB | 54.76 s / 55.15 s | **50.82 s / 50.63 s** | 962 / 965 MB/s |

**Where it loses, stated plainly.** With 3 GB already in RAM, ripgrep wins: 0.33 s against 0.55 s.
There is also one `-v` case at 10 GB, hot, where ripgrep is faster. Everywhere else the two are level,
or `uvf` is a little ahead.

The v1.6.0 release notes said `uvf` "is not a speed tool". **That is no longer true.**

### 🔤 `-i`, `-E` and `-v` added

Up to v1.6.2 `uvf` only did plain string search. Three options are now accepted, spelled as in Pro's `uvp`:

| | Meaning | grep equivalent |
|---|---|---|
| `-i` | ignore case | `grep -i` |
| `-E` | treat the pattern as a regular expression | `grep -E` |
| `-v` | print the lines that do **not** match | `grep -v` |

Output (`line<TAB>text`) and exit codes (`0` / `1` / `2`) are unchanged.
`-i` takes an ASCII-only case-folding path, so it costs about the same as a plain search. `-E` extracts the
**literal the pattern must contain**, narrows to candidate lines by scanning bytes, and decodes only those to confirm.

**At 50 GB** (cold / hot, `uvf` against ripgrep):

| Search | `uvf` | ripgrep |
|---|---:|---:|
| `-i` ignore case | 51.58 / 51.40 s | 56.64 / 56.73 s |
| `-E` regular expression | 51.70 / 51.55 s | 55.35 / 55.90 s |
| `-v` non-matching lines | 52.15 / 51.96 s | 57.89 / 58.04 s |

The output was checked against ripgrep across 3 sizes (3 / 10 / 50 GB) and 7 patterns.

### 🔢 The hit limit now defaults to unlimited

`uvf` used to stop at 1,000,000 hits. **The default is now unlimited.** If a limit (configurable) does cut the
output short, `uvf` still returns `2`, so a script cannot mistake truncated output for success.

### 🔧 Other changes

- `-open` cannot be combined with `-i` / `-E` / `-v` (the window re-runs the search itself)
- To call it by name, register it from Help → **"Command line setup…"**

### 📥 Downloads

| OS | File |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.3-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.3-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.3-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.3-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.3-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.3-linux-aarch64.tar.gz` |

No .NET installation is needed (self-contained).
The macOS build is a **Developer ID signed, Apple notarized** DMG — open it and drag `UwView.app` to Applications.
The Windows build is unsigned (if SmartScreen appears, choose **More info** → **Run anyway**).

Checksums are in `SHA256SUMS-1.6.3.txt`; the same archives are also committed under
[`dist/`](https://github.com/amru195704/UwView/tree/main/dist).

### 📣 The side that keeps an index — UwView Pro

On the same 50 GB file, with the `.uwvz` indexed cache in place, Pro's `uvp` answers in **6.34 s**
(ripgrep: 55.15 s). Against `uvf`'s 50.63 s that is an eightfold gap — the difference between keeping an index
and not keeping one. Narrowing, tallies, ordered search, `-out .gz` and reading/writing `.uwvz` are Pro features.
→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) (one-time $129 / $9 per month, with a **14-day free trial**)
