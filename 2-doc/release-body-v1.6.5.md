*日本語 ｜ [English](#english)*

## UwView V1.6.5

**`uvf … -open` が、索引の完成を待たなくなりました。**
そして **`-open` を `-i`／`-E`／`-v` と一緒に使える**ようになりました。
（前回の無料版は v1.6.4 です）

### ⚡ 待ちが、もう一段減りました

`uvf ファイル '語' -open` の待ち時間は、3つの版でこう変わりました。

| | v1.6.3 まで | v1.6.4 | **v1.6.5** |
|---|---|---|---|
| ファイルを読む回数 | CLI が1回＋画面が1回＝**2回** | **1回** | **1回** |
| 索引ができるまでの待ち | あり | あり | **なし** |
| 結果一覧の行番号 | 索引ができてから | 索引ができてから | **最初から** |

v1.6.4 では、CLI が見つけた行の位置を画面へ渡すようにしました。それでも画面は、
**結果一覧に行番号を出すために索引の完成を待って**いました。50GB なら、探し終えたあとにもう一度待つことになります。

v1.6.5 は、**CLI が検索のついでに索引の目印も集めて、一緒に渡します。** どのみち全部読んでいるので、
数えるものを1つ増やしても値段はほとんど変わりません。画面は受け取った目印から索引を**組み立てるだけ**で、
ファイルを読み直しません。行番号も一緒に渡るので、**結果一覧は最初から行番号つきで出ます。**

「探し終わったのに、まだ待たされる」「最初は行番号がなくて、あとから出てくる」——その2つが無くなりました。

### 🔤 `-open` と `-i`／`-E`／`-v` を併用できます

v1.6.4 まで、`-open` は素の文字列検索としか組み合わせられませんでした。**画面側に条件を受け取る口が無かった**ためです。

v1.6.5 は、**CLI 側で条件を解決してから結果を渡し、どの種類の検索だったかも一緒に伝えます。**
受け渡しが使えなかったときでも、画面が**違う条件で検索し直すことがありません。**

```bash
uvf app.log 'FATAL|PANIC' -E -open      # 正規表現の結果を、そのまま画面で
uvf app.log 'debug' -i -v -open         # 大小無視で「含まない」行を、そのまま画面で
```

Pro の `uvp -open` は以前から併用できます（段の内容ごと画面へ渡すため）。

### 🔍 何を渡しているか

受け渡しの中身が増えました（形式 v2）。

- ヒットした行の**行頭バイト位置**（v1.6.4 から）
- **ヒット行の行番号**（v1.6.5 で追加。結果一覧が索引を待たずに行番号を出せる）
- **N 行ごとの行頭位置＝索引の目印**と、ファイル全体の改行の数（v1.6.5 で追加。画面が索引を読み直さずに組み立てられる）
- 検索語と `-i`／`-E`／`-v` の別（画面の検索欄・強調表示・再検索の条件に使う）

安全側の作りは変わりません。

- 受け渡しは一時ファイル1つ（`uvf-open-*.uvfh`）で、**画面が読んだ時点で消します**。一度きりです
- 渡す前と渡した後で**ファイルの長さを突き合わせます**。違っていれば受け取ったものを捨てて、画面が普通に検索し直します
- 一時ファイルが書けなかったときも、黙って従来どおりの動き（画面が検索し、索引を作る）に戻ります。**失敗しても止まりません**

### 🔧 その他

- `uvf` の検索そのものの速さは v1.6.3 から変わっていません（3GB 3.32秒／10GB 10.48秒／50GB 50.82秒・Mac M4 コールド）
- gzip などの圧縮ファイルを `-open` したときは、従来どおり画面側で開きます（受け渡しは行いません）
- 名前だけで打てるようにするには、ヘルプの**「コマンドライン設定…」**で登録してください

### 📥 ダウンロード

| OS | ファイル |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.5-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.5-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.5-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.5-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.5-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.5-linux-aarch64.tar.gz` |

.NET のインストールは要りません（自己完結型）。
macOS 版は **Developer ID 署名・Apple 公証済み**の DMG です（開いて `UwView.app` を「アプリケーション」へ）。
Windows 版は未署名です（SmartScreen が出たら「詳細情報」→「実行」）。

チェックサムは `SHA256SUMS-1.6.5.txt`。

```
780e88c07531d6f37929bae6d81ff5f29427c7352b0089f3e1158f9949163c71  UwView-1.6.5-linux-aarch64.tar.gz
e8dce975f1f49f0dbb5bb0bbd0759b266ebf1d0b5488868bf2d2ae2cef9f3794  UwView-1.6.5-linux-x86_64.tar.gz
2d098ee5d7a76e22de3bbfdca3225a9317835057a4db61649249288e281958be  UwView-1.6.5-mac-arm64.dmg
c4a0e920413dc7593275e41a19ce9cc26dde4a937e0673cce3c3d84493ec7052  UwView-1.6.5-mac-x64.dmg
46c12b98fc2b3ac4c9aa42e771ef3d40e32c969f954384285a8028c2ec21a528  UwView-1.6.5-win-arm64.zip
93c3a92749d2a6ee3ba4e89d1b13d81095cbf6cdbd6e2b0d72a583b4056c9b88  UwView-1.6.5-win-x64.zip
```

> **配布は GitHub Releases のみ**です（v1.6.4 から一本化しました。clone を軽くするためと、配布数を数えられるようにするため）。

### 📣 索引を持つ側 — UwView Pro

同じ 50GB を、`.uwvz`（索引つき圧縮キャッシュ）がある状態で `uvp` が検索すると **6.34秒**です。
`uvf` の 50.63秒と比べると8倍の差で、これが「索引を作る／作らない」の差です。
絞り込み・頻度集計・順序検索・`-out .gz`・`.uwvz` の読み書きは Pro の機能です。
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9。**14日間の無料試用**あり）

---

<a name="english"></a>

## UwView v1.6.5 (English)

**`uvf … -open` no longer waits for the index to finish**, and **`-open` can now be combined with `-i` / `-E` / `-v`.**
(The previous free-edition release was v1.6.4.)

### ⚡ One more wait removed

Here is what `uvf file 'pattern' -open` has cost you across three releases.

| | up to v1.6.3 | v1.6.4 | **v1.6.5** |
|---|---|---|---|
| Passes over the file | CLI once + window once = **twice** | **once** | **once** |
| Waiting for the index | yes | yes | **no** |
| Line numbers in the results list | after the index | after the index | **immediately** |

v1.6.4 made the CLI hand the positions of its matches to the window. The window still **waited for the index to
finish** before it could show line numbers in the results list — on a 50 GB file, that is a second wait after the
search has already succeeded.

v1.6.5 has the **CLI collect the index markers while it is searching** and pass those along too. It is reading the
whole file anyway, so counting one more thing costs almost nothing. The window **assembles** the index from what it
received instead of re-reading the file, and because the line numbers travel with it, **the results list has line
numbers from the first frame.**

"It found it, and I am still waiting" and "the line numbers show up later" are both gone.

### 🔤 `-open` with `-i` / `-E` / `-v`

Up to v1.6.4, `-open` only worked with a plain string search, because **the window had no way to receive the flags.**

In v1.6.5 the **CLI resolves the flags itself before handing over the result, and says which kind of search it was.**
Even when the handoff cannot be used, the window will **not fall back to searching with different conditions.**

```bash
uvf app.log 'FATAL|PANIC' -E -open      # a regex result, straight into the window
uvf app.log 'debug' -i -v -open         # case-insensitive "not matching", straight into the window
```

Pro's `uvp -open` has always allowed the combination (it hands over the whole pipeline).

### 🔍 What is passed

The handoff now carries more (format v2):

- the **byte offset of each matching line** (since v1.6.4)
- **the line number of each match** (new in v1.6.5 — so the results list does not need the index)
- **a line-start offset every N lines — the index markers** — and the total newline count (new in v1.6.5, so the window assembles the index instead of re-reading)
- the pattern and which of `-i` / `-E` / `-v` applied (for the search box, the highlighting, and any fallback search)

The safety behaviour is unchanged:

- the handoff is one temporary file (`uvf-open-*.uvfh`), and **the window deletes it on read** — one-shot
- the file's **length is compared before and after**; if it changed, the handoff is discarded and the window searches normally
- if the temporary file cannot be written, it quietly falls back to the old path (window searches, window indexes). **A failure never blocks you**

### 🔧 Other changes

- Search speed itself is unchanged from v1.6.3 (3 GB 3.32 s / 10 GB 10.48 s / 50 GB 50.82 s, Mac M4, cold)
- For gzip and other compressed inputs, `-open` behaves as before — the window opens the file itself, with no handoff
- To call it by name alone, register it from **"Command line setup…"** in the app's Help menu

### 📥 Downloads

| OS | File |
|---|---|
| macOS (Apple Silicon) | `UwView-1.6.5-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.6.5-mac-x64.dmg` |
| Windows (x64) | `UwView-1.6.5-win-x64.zip` |
| Windows (ARM64) | `UwView-1.6.5-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.6.5-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.6.5-linux-aarch64.tar.gz` |

No .NET installation required (self-contained).
The macOS build is a **Developer ID signed, Apple-notarized** DMG (open it and drag `UwView.app` to Applications).
The Windows build is unsigned (if SmartScreen appears: More info → Run anyway).

Checksums are in `SHA256SUMS-1.6.5.txt`.

```
780e88c07531d6f37929bae6d81ff5f29427c7352b0089f3e1158f9949163c71  UwView-1.6.5-linux-aarch64.tar.gz
e8dce975f1f49f0dbb5bb0bbd0759b266ebf1d0b5488868bf2d2ae2cef9f3794  UwView-1.6.5-linux-x86_64.tar.gz
2d098ee5d7a76e22de3bbfdca3225a9317835057a4db61649249288e281958be  UwView-1.6.5-mac-arm64.dmg
c4a0e920413dc7593275e41a19ce9cc26dde4a937e0673cce3c3d84493ec7052  UwView-1.6.5-mac-x64.dmg
46c12b98fc2b3ac4c9aa42e771ef3d40e32c969f954384285a8028c2ec21a528  UwView-1.6.5-win-arm64.zip
93c3a92749d2a6ee3ba4e89d1b13d81095cbf6cdbd6e2b0d72a583b4056c9b88  UwView-1.6.5-win-x64.zip
```

> **Distribution is GitHub Releases only** (consolidated in v1.6.4 — it keeps a clone small and makes download counts mean something).

### 📣 The side that keeps an index — UwView Pro

Searching the same 50 GB file with `uvp` and a `.uwvz` (indexed compressed cache) in place takes **6.34 s**,
against 50.63 s for `uvf`. That eight-fold gap is what building an index buys you.
Narrowing, frequency counts, ordered search, `-out .gz` and reading/writing `.uwvz` are Pro features.
→ [UwView Pro](https://uvp.y42u.net/pro/) ($129 one-time or $9/month, with a **14-day free trial**)
