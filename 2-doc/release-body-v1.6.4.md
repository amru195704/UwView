*日本語 ｜ [English](#english)*

## UwView V1.6.4

**`uvf … -open` が、探した結果をそのまま画面へ渡すようになりました。**
画面はもう一度探しません。ファイルを読むのが2回から1回になります。
（前回の無料版は v1.6.3 です）

### ⚡ 画面が、同じ検索をやり直さなくなりました

v1.6.3 までの `uvf ファイル '語' -open` は、**CLI が探し、画面がもう一度探す**作りでした。
CLI は結果を出すために全部読んでいるのに、その結果を渡す口が無く、画面側は検索語だけを受け取って
自分でやり直していたためです。50GB なら 50.82秒（Mac M4・コールド）の検索を、2回やっていたことになります。

v1.6.4 は、**CLI が見つけた行の位置を画面へ引き渡します。** 画面は受け取った位置から表示を組み立てるだけなので、
探し直しません。**待ち時間は CLI の1回ぶんだけです。**

| | v1.6.3 まで | v1.6.4 |
|---|---|---|
| `uvf 50GB '語' -open` | CLI が1回＋画面が1回＝**2回読む** | **1回だけ** |

「ターミナルで探して、画面で読む」を1コマンドで済ませる使い方が、そのぶん軽くなります。

### 🔍 何を渡しているか

渡しているのは**ヒットした行の行頭バイト位置だけ**です。行番号も本文も、画面が自分の索引から作れます。
検索語と `-i`／`-E`／`-v` の別も一緒に渡すので、画面の検索欄と強調表示もそのまま揃います。

- 受け渡しは一時ファイル1つ（`uvf-open-*.uvfh`）で、**画面が読んだ時点で消します**。一度きりです
- 渡す前と渡した後で**ファイルの長さを突き合わせます**。違っていれば受け取ったものを捨てて、画面が普通に検索し直します
- 一時ファイルが書けなかったときも、黙って従来どおりの動き（画面が検索）に戻ります。**失敗しても止まりません**
- 上限に当たって打ち切った場合は、その旨も一緒に渡します

Pro の `uvp -open` は以前から同じ考え方の受け渡しを持っており、そちらは変わりません。

### 🔧 その他

- **`-open` と `-i`／`-E`／`-v` の併用は、今回もできません。** 受け渡しの側は条件を運べるようになりましたが、
  画面側の受け口がまだ揃っていないためです。次の版で外す予定です。
  **Pro の `uvp -open` は併用できます**（段の内容ごと画面へ渡すため）
- gzip などの圧縮ファイルを `-open` したときは、従来どおり画面側で開きます（受け渡しは行いません）
- `uvf` の検索そのものの速さは v1.6.3 から変わっていません（3GB 3.32秒／10GB 10.48秒／50GB 50.82秒・Mac M4 コールド）

### 📥 ダウンロード

| OS | ファイル |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.4-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.4-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.4-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.4-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.4-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.4-linux-aarch64.tar.gz` |

.NET のインストールは要りません（自己完結型）。
macOS 版は **Developer ID 署名・Apple 公証済み**の DMG です（開いて `UwView.app` を「アプリケーション」へ）。
Windows 版は未署名です（SmartScreen が出たら「詳細情報」→「実行」）。

チェックサムは `SHA256SUMS-1.6.4.txt`。

```
ed8f713ae40e25396a0c8c2212a760cda4158de7bc63852f373037f0722b423d  UwView-1.6.4-linux-aarch64.tar.gz
68ecc10abc9c3d6f64fa1b797dd721378b0627f6eba9efabb087c13deb64d6e6  UwView-1.6.4-linux-x86_64.tar.gz
4e39c2ffa34cebee71130aafb4201ff3bb334a43d0e2321f7653ab7ba3445501  UwView-1.6.4-mac-arm64.dmg
43754b9027d90d9f461b329851b4e15ae2adf7d46d031ad5c8a5dc84c5472d41  UwView-1.6.4-mac-x64.dmg
93d176f8c8f4e6e06a1c1abffca9c7d89f6f7cf281f27a6616f9a451b265f10c  UwView-1.6.4-win-arm64.zip
b92cb7e7e87c999ecc384ea01c973ee626e356881c6259bfc5d493948d36ebd0  UwView-1.6.4-win-x64.zip
```

> **配布場所を1か所にしました。** これまではリポジトリの `dist/` にも同じアーカイブを置いていましたが、
> v1.6.4 からは **GitHub Releases のみ**です（clone を軽くするためと、配布数を数えられるようにするため）。

### 📣 索引を持つ側 — UwView Pro

同じ 50GB を、`.uwvz`（索引つき圧縮キャッシュ）がある状態で `uvp` が検索すると **6.34秒**です。
`uvf` の 50.63秒と比べると8倍の差で、これが「索引を作る／作らない」の差です。
絞り込み・頻度集計・順序検索・`-out .gz`・`.uwvz` の読み書きは Pro の機能です。
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9。**14日間の無料試用**あり）

---

<a name="english"></a>

## UwView v1.6.4 (English)

**`uvf … -open` now hands its results straight to the window.**
The window no longer repeats the search, so the file is read once instead of twice.
(The previous free-edition release was v1.6.3.)

### ⚡ The window stops re-running the same search

Up to v1.6.3, `uvf file 'pattern' -open` meant **the CLI searched, and then the window searched again.**
The CLI had already read the whole file to produce its output, but there was no channel to pass the result along,
so the window received only the pattern and redid the work. On a 50 GB file that meant doing a 50.82 s search
(Mac M4, cold) twice.

v1.6.4 **hands the positions of the matching lines to the window.** The window builds its display from those
positions instead of searching. **You wait for one pass, not two.**

| | up to v1.6.3 | v1.6.4 |
|---|---|---|
| `uvf 50GB 'pattern' -open` | CLI once + window once = **the file is read twice** | **once** |

If your habit is "find it in the terminal, read it in the window", that habit just got lighter.

### 🔍 What is actually passed

Only the **byte offsets of the matching lines**. Line numbers and line text are things the window can rebuild
from its own index. The pattern and the `-i` / `-E` / `-v` flags travel with it, so the window's search box and
highlighting line up too.

- The handoff is one temporary file (`uvf-open-*.uvfh`), and **the window deletes it on read**. It is one-shot
- The file's **length is compared before and after**. If it changed, the handoff is discarded and the window searches normally
- If the temporary file cannot be written, it quietly falls back to the old behaviour. **A failure never blocks you**
- If the hit limit truncated the results, that fact is passed along as well

Pro's `uvp -open` has had an equivalent handoff for a while; it is unchanged.

### 🔧 Other changes

- **`-open` still cannot be combined with `-i` / `-E` / `-v`.** The handoff can now carry those flags, but the
  window side is not ready to receive them yet. That restriction is scheduled to go in the next release.
  **Pro's `uvp -open` does allow the combination** (it hands over the whole pipeline)
- For gzip and other compressed inputs, `-open` behaves as before — the window opens the file itself, with no handoff
- Search speed itself is unchanged from v1.6.3 (3 GB 3.32 s / 10 GB 10.48 s / 50 GB 50.82 s, Mac M4, cold)

### 📥 Downloads

| OS | File |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.4-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.4-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.4-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.4-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.4-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.4-linux-aarch64.tar.gz` |

No .NET installation required (self-contained).
The macOS build is a **Developer ID signed, Apple-notarized** DMG (open it and drag `UwView.app` to Applications).
The Windows build is unsigned (if SmartScreen appears: More info → Run anyway).

Checksums are in `SHA256SUMS-1.6.4.txt`.

```
ed8f713ae40e25396a0c8c2212a760cda4158de7bc63852f373037f0722b423d  UwView-1.6.4-linux-aarch64.tar.gz
68ecc10abc9c3d6f64fa1b797dd721378b0627f6eba9efabb087c13deb64d6e6  UwView-1.6.4-linux-x86_64.tar.gz
4e39c2ffa34cebee71130aafb4201ff3bb334a43d0e2321f7653ab7ba3445501  UwView-1.6.4-mac-arm64.dmg
43754b9027d90d9f461b329851b4e15ae2adf7d46d031ad5c8a5dc84c5472d41  UwView-1.6.4-mac-x64.dmg
93d176f8c8f4e6e06a1c1abffca9c7d89f6f7cf281f27a6616f9a451b265f10c  UwView-1.6.4-win-arm64.zip
b92cb7e7e87c999ecc384ea01c973ee626e356881c6259bfc5d493948d36ebd0  UwView-1.6.4-win-x64.zip
```

> **Distribution is now in one place.** The repository used to carry the same archives under `dist/`;
> from v1.6.4 they live **only in GitHub Releases** — it keeps a clone small and makes download counts mean something.

### 📣 The side that keeps an index — UwView Pro

Searching the same 50 GB file with `uvp` and a `.uwvz` (indexed compressed cache) in place takes **6.34 s**,
against 50.63 s for `uvf`. That eight-fold gap is what building an index buys you.
Narrowing, frequency counts, ordered search, `-out .gz` and reading/writing `.uwvz` are Pro features.
→ [UwView Pro](https://uvp.y42u.net/pro/) ($129 one-time or $9/month, with a **14-day free trial**)
