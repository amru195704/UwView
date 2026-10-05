*日本語 ｜ [English](#uwview-v1737--wide-field-english)*

## UwView v1.7.3.7 — Wide Field

> **これはプレリリース（試用版）です。Mac・Windows・Linux 版を、この GitHub Releases のページでだけ配布します。**
> Homebrew・Scoop・Snap は v1.7.3.6.9 のままです（`brew upgrade` などでは入りません）。安定版を使いたい方は [v1.7.3.6.9](https://github.com/amru195704/UwView/releases/tag/v1.7.3.6.9) をお使いください。

**決まった文字列を含まない正規表現（`[0-9]{4}-[0-9]{2}`・`[ぁ-ん]{3,}` など）が、2〜6 倍速くなりました。あわせて、外部のソースレビューで見つかった「特殊な正規表現で、ヒットするはずの行が出ない」不具合を直しました。**
画面の使い方と出力の形は変わりません。
（前の版は v1.7.3.6.9 です → [v1.7.3.6.9 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.9.md)）

### 🔍 決まった文字列を含まない正規表現を、1 文字の手がかりで先に絞る

```bash
uvf japan.osm '[0-9]{4}-[0-9]{2}' -E
uvf japan.osm '[ぁ-ん]{3,}' -E
```

2 文字以上の決まった文字列を含まない正規表現は、これまで全部の行に当てていました。今の版は、**1 文字の決まった文字**（上の `-`、`\d+\.\d{6,}` の `.`）や、**文字の範囲の先頭のバイト**（`[ぁ-ん]` なら UTF-8 で `E3 81` か `E3 82`）を先に探し、それがある行にだけ正規表現を当てます。

先に、まだ追いついていない点です。ripgrep と比べると、`[0-9]{4}-[0-9]{2}` は同等（1.34 対 1.33秒）ですが、`\d+\.\d{6,}` は **ripgrep の方が 1.5倍**、`[ぁ-ん]{3,}` は **ripgrep の方が 1.7倍**速いままです。

| 3GB・2回目（hot）・秒 | v1.7.3.6.9 | **v1.7.3.7** | ripgrep |
|---|---:|---:|---:|
| `[0-9]{4}-[0-9]{2}` | 3.06 | **1.34**（2.3倍） | 1.33 |
| `\d+\.\d{6,}` | 2.63 | **0.56**（4.7倍） | 0.37 |
| `[ぁ-ん]{3,}` | 2.70 | **0.45**（6.1倍） | 0.27 |

- 空白や `<` `"` `=`・数字のように、ほとんどの行にある 1 文字は手がかりにしません（絞れずに探す手間だけが増えるため）。`^ +<` のような式は前と同じ速さです。
- 小さなファイルが大量にある場所（Linux カーネルのソース 8.6万本）でも、この3つの式の2回目は 2.13〜2.28秒で、ripgrep（1.53〜1.64秒）の方が約 1.4 倍速いものの、1.5 倍未満なので同等の範囲です。
- 画面の検索と UwView Pro の検索・置換にも同じように効きます。
- 表は Mac（Apple M4・10コア）での測定です（3回の中央値）。

### 🐞 特殊な正規表現で、ヒットするはずの行が出なかった不具合を直しました

正規表現は、決まった文字列を先にバイト列のまま探して、候補の行にだけ当てています。この「先に探す文字列」の取り出し方に誤りがあり、次の式ではヒットするはずの行を捨てていました（`-v` では逆に、出ないはずの行が出ました）。

| 式の例 | 誤り | 前の版 |
|---|---|---|
| `a😀?`（絵文字に `?` `*` などを付ける） | 絵文字の半分だけを必須の文字と取り違えた | v1.7.3.6.9 まで |
| `[(?P<]`（文字クラスの中に `(?P<` という並び） | Python 形式の名前付きグループの読み替えが、文字クラスの中まで書き換えた | v1.7.3.6.9 まで |

- どちらも、ふつうの検索（日本語・英数字・記号の式）には影響しません。
- 新しい 1 文字の手がかりでも、公開前に同じ種類の誤りを2つ見つけて直しました（壊れたバイトを読んで出る置換文字 `�` を探す式、グループの中に `(?x)` などのオプションを書いた式）。
- コメントの中に `[` を書いた式（`(?# [)(?P<n>a)]` など）で、後ろの Python 形式の名前付きグループを読み替えず、「正しくない正規表現」として断っていたのも直しました（公開前の再レビューで見つかったもの）。
- 期待する結果は、.NET の正規表現をそのまま全行に当てた結果です。先に絞る処理の有無で結果が同じになることを、テストで確かめています。

### 🔧 そのほかの変更

- `uvf --version` が、ビルド番号も出すようにしました（`uvf 1.7.3.7 (build 年.月.日.時)` の形。同じファイルの About に出る番号と同じです。番号はビルドした機械の時計で付くので、OS ごとのファイルで「時」が違うことがあります）。版数は今までどおり2つ目の語なので、版数を読むスクリプトはそのまま使えます。
- mac で、`-i` を付けた正規表現を大きな入力に当てるとき、実際には全行に当てるのに、起動の速い形（NativeAOT）のまま探すことがありました（`-i -E 'foo|bar'`、`-i -E '東京'` など）。こうした式は、正規表現をコンパイルできる本体に任せるようにしました。
- 検索の下ごしらえ（正規表現の読み替えとコンパイル、先に探す文字列の取り出し、探し方の選択）を1か所にまとめ、画面の検索・`uvf`・UwView Pro の検索と置換が同じものを使うようにしました。
  - 多数のファイルを探すときは、下ごしらえを1回だけ行います（今まではファイルごとにやり直していました。1ファイルごとに確保するメモリ 7,368 → 約 200 バイト。`[ぁ-ん]{3,}` の場合）。
  - 画面の検索も、`-i` を付けた正規表現を、`uvf` と同じ手がかりで先に絞るようになりました。
  - 1 文字の手がかりがほとんどの行にあって絞れないときは、最初の 10 万行で見切って、全行に当てる探し方に切り替えます。
  - mac では、1 本の大きなファイルの先頭を少し読み、1 文字の手がかりで絞れない（全行に当てることになる）と分かったときは、正規表現をコンパイルできる本体に任せます（10G の `[0-9]{3}-[0-9]{4}"` で 2 回目 5.9 → 3.8 秒）。
  - 3GB の8種類・Linux カーネルの5種類の検索で、出力が前と1バイトも変わらないこと、速さが変わらないことを確かめています。
- 出力は1バイトも変えていません。テストはすべて通っています。

### ⚠️ 注意

- 1文字の `k` を大小無視で探すような、目印にできる部分が無い検索は速くなっていません。
- 無料版で扱えないもの：`.zip`・`.tar.gz`・OSM の `.pbf`（展開してから探してください。zip と pbf は UwView Pro が扱います）。
- 秒数は、ある1台の Mac（Apple M4・10コア・メモリ 32GB・外付け USB SSD）で 2026年10月4日に測った値です。hot は続けて走らせた2回目です。**秒数を環境をまたいで比べないでください。**

### 📥 ダウンロード（プレリリース）

このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉は `arm64`、Intel は `x64`）、Windows は zip、Linux は tar.gz です。チェックサムは `SHA256SUMS-1.7.3.7.txt` にあります。

```
8ad813986d25f8c4d7e91bd2a803e4482d297f9ff3e5c43c8ef531c760019a96  UwView-1.7.3.7-linux-aarch64.tar.gz
5c8f6b69dd119d1488db2c1360f9497acadd498d5feada97a6e5b34b8a3aed72  UwView-1.7.3.7-linux-x86_64.tar.gz
14dafdcbf777e598d7c79f7d34d98bbef95cb142f7af469b04862a48e69209f7  UwView-1.7.3.7-mac-arm64.dmg
9131427d6bcf01090cb0767d86a035f1dc300ea6f645b501e658a459e1ab42fa  UwView-1.7.3.7-mac-x64.dmg
1d6babfcb9d88bb7e0ce79b4dd275909e3c31dca1d0e3f41ee5066a6e721e9c6  UwView-1.7.3.7-win-arm64.zip
85bbadc19cc54fdff3f739b213ad2d0eedbb0fe2e0d914e7d4d32936e1d62d13  UwView-1.7.3.7-win-x64.zip
（2026-10-05 18:55 作成。macOS 版は署名・公証済み。ビルド番号はビルドした機械の時計で付くため、Mac 版は 26.10.05.18（日本時間）、Linux 版は 26.10.05.09（UTC）と、同じ時に作っても「時」が違います）
```

- Homebrew・Scoop・Snap には出しません。そちらで入れている方は v1.7.3.6.9 のままで、正式版が出たら、いつもの更新方法（`brew upgrade --cask uwview`・`scoop update uwview`・`snap refresh uwview`）で入ります。

> **このプレリリースの配布は GitHub Releases のみです。** 操作説明と最新情報は blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — ヒットの多い検索を、探しながら書き出す

有償版の `uvp` にも、上の手がかりと修正が入りました。あわせて、ヒットの多い検索を、すべて集め終わるのを待たずに、探しながらファイルの順に書き出すようにしました。

| `uvp`・2回目（hot）・秒 | v1.7.3.6.9 | **v1.7.3.7** | ripgrep |
|---|---:|---:|---:|
| 1,000 本を束ねた `.uwvz`・`[0-9]{4}-[0-9]{2}`（940万行がヒット） | 8.03 | **1.86** | 1.67 |

- ripgrep（1.67秒）の方が 1.1 倍速いものの、1.5 倍未満なので同等の範囲です。

→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.7.3.7 — Wide Field (English)

*[日本語](#uwview-v1737--wide-field) ｜ English*

> **This is a pre-release. The macOS, Windows and Linux builds are offered only on this GitHub Releases page.**
> Homebrew, Scoop and Snap stay on v1.7.3.6.9 (`brew upgrade` and the like will not install it). For the stable version, use [v1.7.3.6.9](https://github.com/amru195704/UwView/releases/tag/v1.7.3.6.9).

**Regular expressions without a fixed string (such as `[0-9]{4}-[0-9]{2}` or `[ぁ-ん]{3,}`) are now 2–6× faster. This release also fixes a bug found in an external source review: with a few unusual regular expressions, lines that should have matched were not shown.**
The window and the output format are unchanged.
(The previous release is v1.7.3.6.9 → [what changed in v1.7.3.6.9](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.7.3.6.9.md))

### 🔍 Regular expressions without a fixed string: narrowed first by a one-character clue

```bash
uvf japan.osm '[0-9]{4}-[0-9]{2}' -E
uvf japan.osm '[ぁ-ん]{3,}' -E
```

A regular expression with no fixed string of two or more characters used to be tried on every line. Now `uvf` first looks for **a single required character** (the `-` above, the `.` in `\d+\.\d{6,}`) or **the leading bytes of a character range** (`[ぁ-ん]` always starts with `E3 81` or `E3 82` in UTF-8), and runs the regular expression only on lines that have one.

First, where it is still behind: against ripgrep, `[0-9]{4}-[0-9]{2}` is on par (1.34 vs 1.33 s), but **ripgrep is still 1.5× faster** on `\d+\.\d{6,}` and **1.7× faster** on `[ぁ-ん]{3,}`.

| 3 GB, second run (hot), seconds | v1.7.3.6.9 | **v1.7.3.7** | ripgrep |
|---|---:|---:|---:|
| `[0-9]{4}-[0-9]{2}` | 3.06 | **1.34** (2.3×) | 1.33 |
| `\d+\.\d{6,}` | 2.63 | **0.56** (4.7×) | 0.37 |
| `[ぁ-ん]{3,}` | 2.70 | **0.45** (6.1×) | 0.27 |

- Characters found on almost every line — spaces, `<`, `"`, `=`, digits — are not used as clues (they would add work without narrowing anything). Expressions such as `^ +<` run at the same speed as before.
- On many small files (the Linux kernel source, 86,000 files), the second run of these three expressions takes 2.13–2.28 s. ripgrep (1.53–1.64 s) is about 1.4× faster, which we treat as on par (under 1.5×).
- The window's search and UwView Pro's search and replace benefit in the same way.
- Measured on a Mac (Apple M4, 10 cores) (median of three runs).

### 🐞 Fixed: a few unusual regular expressions missed lines that should match

Regular expressions first look for a fixed string as raw bytes and run only on the candidate lines. The way that string was extracted was wrong for the expressions below, so matching lines were dropped (and with `-v`, lines that should have been hidden were shown).

| Example | What went wrong | Affected |
|---|---|---|
| `a😀?` (an emoji with `?`, `*` and so on) | half of the emoji was taken as a required character | up to v1.7.3.6.9 |
| `[(?P<]` (the sequence `(?P<` inside a character class) | the rewrite of Python-style named groups also changed the inside of character classes | up to v1.7.3.6.9 |

- Ordinary searches (Japanese, letters, digits, symbols) are not affected.
- Two mistakes of the same kind in the new one-character clues were found and fixed before release (searching for the replacement character `�` that appears when broken bytes are read, and options such as `(?x)` written inside a group).
- An expression with `[` inside a comment (such as `(?# [)(?P<n>a)]`) no longer fails to rewrite a later Python-style named group and is no longer rejected as an invalid regular expression (found in the second review before release).
- The expected result is that of running .NET's regular expression on every line. Tests confirm that the result is the same with and without the narrowing step.

### 🔧 Other changes

- `uvf --version` now also prints the build number (as `uvf 1.7.3.7 (build yy.MM.dd.HH)`, the same number as in About of the same package; the number comes from the clock of the build machine, so the hour can differ between the files for each OS). The version is still the second word, so scripts that read it keep working.
- On macOS, some regular expressions with `-i` ran on every line of a large input but stayed in the fast-starting (NativeAOT) form (`-i -E 'foo|bar'`, `-i -E '東京'` and so on). They are now handed to the main app, which can compile regular expressions.
- The preparation of a search (rewriting and compiling the regular expression, extracting the strings to look for first, choosing how to search) now lives in one place, shared by the window's search, `uvf`, and UwView Pro's search and replace.
  - When searching many files, it is done once instead of for every file (memory allocated per file: 7,368 → about 200 bytes for `[ぁ-ん]{3,}`).
  - The window's search now also narrows regular expressions with `-i` by the same clue as `uvf`.
  - When a one-character clue is on almost every line and narrows nothing, the search gives up on it after the first 100,000 lines and tries the expression on every line.
  - On macOS, `uvf` reads the start of a large single file; when the one-character clue would not narrow anything (every line gets the expression), the search is handed to the main app, which can compile regular expressions (10 GB `[0-9]{3}-[0-9]{4}"`, second run: 5.9 → 3.8 s).
  - Output is byte-for-byte unchanged and speed is the same on eight searches on 3 GB and five on the Linux kernel source.
- Output is unchanged byte for byte. All tests pass.

### ⚠️ Notes

- Searches with nothing to anchor on, such as a single `k` ignoring case, are not faster.
- Not handled by the free edition: `.zip`, `.tar.gz` and OSM `.pbf` (extract them first; UwView Pro handles zip and pbf).
- Times were measured on one Mac (Apple M4, 10 cores, 32 GB, external USB SSD) on 4 October 2026. Hot is the second run in a row. **Do not compare times across environments.**

### 📥 Download (pre-release)

Get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows, a tar.gz for Linux. Checksums are in `SHA256SUMS-1.7.3.7.txt`.

```
8ad813986d25f8c4d7e91bd2a803e4482d297f9ff3e5c43c8ef531c760019a96  UwView-1.7.3.7-linux-aarch64.tar.gz
5c8f6b69dd119d1488db2c1360f9497acadd498d5feada97a6e5b34b8a3aed72  UwView-1.7.3.7-linux-x86_64.tar.gz
14dafdcbf777e598d7c79f7d34d98bbef95cb142f7af469b04862a48e69209f7  UwView-1.7.3.7-mac-arm64.dmg
9131427d6bcf01090cb0767d86a035f1dc300ea6f645b501e658a459e1ab42fa  UwView-1.7.3.7-mac-x64.dmg
1d6babfcb9d88bb7e0ce79b4dd275909e3c31dca1d0e3f41ee5066a6e721e9c6  UwView-1.7.3.7-win-arm64.zip
85bbadc19cc54fdff3f739b213ad2d0eedbb0fe2e0d914e7d4d32936e1d62d13  UwView-1.7.3.7-win-x64.zip
(built 2026-10-05 18:55 JST; the macOS builds are signed and notarized. Build numbers come from the clock of the build machine, so builds made at the same time differ in the hour: 26.10.05.18 (JST) for macOS, 26.10.05.09 (UTC) for Linux)
```

- Not published to Homebrew, Scoop or Snap. If you installed from one of them you stay on v1.7.3.6.9, and the final release will arrive through the usual update (`brew upgrade --cask uwview`, `scoop update uwview`, `snap refresh uwview`).

> **This pre-release is distributed through GitHub Releases only.** Instructions and news are on the blog (https://uvp.y42u.net/).

### 📣 On the UwView Pro side — writing hits out while searching

The paid `uvp` gets the same clues and fixes. It also writes out searches with many hits in file order while it searches, instead of waiting until every hit is collected.

| `uvp`, second run (hot), seconds | v1.7.3.6.9 | **v1.7.3.7** | ripgrep |
|---|---:|---:|---:|
| `.uwvz` of 1,000 files, `[0-9]{4}-[0-9]{2}` (9.4 million matching lines) | 8.03 | **1.86** | 1.67 |

- ripgrep (1.67 s) is 1.1× faster, which is on par (under 1.5×).

→ [UwView Pro](https://uvp.y42u.net/pro/) (one-time $129 / $9 a month, **14-day free trial**)
