*日本語 ｜ [English](#uwview-v166-english)*

## UwView V1.6.6

**画面が、コマンドに追いつきました。**
GUI が**索引の完成を待たずに検索を始められる**ようになり、**開くのも klogg より速く**なりました。
（前回の無料版は v1.6.5 です）

### ⚡ 50GB の「開いて、探して、画面に出す」が 189.5秒 → 53.7秒

v1.6.5 まで、画面の手順はこうでした。

1. ファイルを開く。裏で索引を作る（50GB で 100.6秒）
2. 索引ができてから、検索する（88.9秒）
3. 合計 189.50秒

**問題は「2回読んでいる」ことでした。** 1で全部読み、2でまた全部読む。
一方 `uvf … -open` は、**1回読むあいだに探して、索引を作って、画面へ渡す**——だから 53.69秒で済んでいました。

**v1.6.6 では、画面を同じ形にしました。** 開いている最中に検索を始められるので、ファイルを読むのは1回きりです。

| 50GB・「探して、画面で読む」まで | 時間 | v1.6.6 との比 |
|---|---:|---:|
| UwView 無料版 GUI **v1.6.5** | 189.50秒 | **3.53倍** |
| klogg 24.11.0 | 108.14秒 | **2.01倍** |
| `uvf … -open`（コマンド） | 53.69秒 | **1.00倍** |
| **UwView 無料版 GUI v1.6.6** | **53.7秒** | — |

**53.7秒。51.25GB ÷ 53.7秒 ＝ 910 MB/s で、このディスクの素読みそのものです。**
そして **`uvf … -open` の 53.69秒と 0.01秒しか違いません**——偶然ではなく、**どちらもファイルを1回読む時間**だからです。

### 📂 「ただ開くだけ」も速くなりました

| 開くだけ（`sudo purge` 後のコールド） | 3GB | 10GB | 50GB |
|---|---:|---:|---:|
| klogg 24.11.0 | 3.65秒 | 10.98秒 | 52.55秒 |
| UwView 無料版 GUI **v1.6.5** | 5.27秒 | 19.62秒 | 100.6秒 |
| **UwView 無料版 GUI v1.6.6** | **2.99秒** | **10.13秒** | **50.44秒** |
| **対 klogg** | **1.22倍** | **1.08倍** | **1.04倍** |

読み出しに直すと **967／966／969 MB/s**。**3サイズとも同じ値**で、これは媒体の速度そのものです
（3GB はメモリに載る大きさですが 967 止まりなので、キャッシュが効いた値ではありません）。**v1.6.5 の 486 MB/s の 1.99倍です。**

v1.6.5 の公開時、私たちは「**開くのは klogg に負けています**」と書きました。**その宿題を返した版です。**

### 🧮 3GB・10GB の通しは、秒数を出しません

正直に書きます。**3GB と 10GB は、手で測れる精度を超えてしまいました。**
**3GB に至っては、そもそも測れません。検索語を打ち込んでいる間に、開き終わってしまうからです。**
「待ってから探す」という手順が消えたので、「待ち時間」を測る操作自体が成立しません。

**測れないものに数字を付けたら、それは実測ではなく作文です。** なので書きません。
言えるのはここまでです——**開く処理と検索処理が同時に走る。3GB では、探し始める前に開き終わっている。**

### 🗜 `.gz` をそのまま検索できるようになりました（無料版）— **`zgrep` より速い**

これまで `uvf ファイル.gz '語'` は受け付けず、`-open` で画面に渡すしかありませんでした。
**v1.6.6 から、コマンドでそのまま検索できます。**

```bash
uvf app.log.gz 'ERROR'          # パイプを書かずに済む
uvf app.log.gz 'ERROR' -open    # 当たりをそのまま画面へ
```

あわせて**展開処理を OS 標準の zlib に載せ替え**ました。**2.0〜2.1倍**速くなり、
`gzip -dc | rg`（＝`zgrep` 相当）を**追い抜きました**。

| `.gz` を検索（Mac・コールド／ホット） | `gzip -dc \| rg` | **`uvf`** | 倍率 |
|---|---:|---:|---:|
| 3GB 相当（301MB の gz） | 1.18秒 / 1.16秒 | **1.16秒 / 1.00秒** | **1.02／1.16倍** |
| 10GB 相当（1.12GB の gz） | 4.38秒 / 4.29秒 | **3.66秒 / 3.38秒** | **1.20／1.27倍** |
| 50GB 相当（5.75GB の gz） | 22.06秒 / 21.71秒 | **17.20秒 / 17.04秒** | **1.28／1.27倍** |

**`gzip` コマンドそのものより速い**のは、プロセス間のパイプを経由せず、
展開と照合を同じプロセスの中で重ねられるからです。

> **`.gz` は「展開が律速」です。** コールドとホットがほぼ同じ秒数なのがその証拠で、
> ディスクではなく CPU（展開）で決まっています。だから展開を速くした分がそのまま効きました。

### ⚠️ 既知の制限：圧縮ファイル内の極端に長い行

`.gz` の中に **1行が 64MiB（ASCII で約6,700万文字）を超える行**があり、その行が検索に一致した場合、
「ファイルが壊れている」という趣旨のエラーになることがあります。**ファイル自体は壊れていません。**

**対応予定はありません。** ログや XML ダンプで1行が 64MiB を超えることは通常なく、
一方でこの制限を外すには行の長さに上限を置かない作りが必要で、**通常のファイルの処理まで遅くなります。**
UwView は「巨大なファイルを速く見る」ことに全振りした道具なので、速度を優先します。
該当するファイルは `gunzip` で展開してから開いてください。

### 🔧 CLI（`uvf`）に退行はありません

3GB・10GB・50GB の全テスト（検索7種・コールド＋hot）を v1.6.3 と比べ、**全項目が ±6% 以内**であることを確認しました。
公表している ripgrep との比（3GB は rg の勝ち／10GB 1.08倍／50GB 1.10倍）は、そのまま有効です。

### ⚠️ 注意

- **「開く」と「検索」の秒数を足しても、通しにはなりません。** v1.6.6 は同時に走ります（50GB なら 50.44＋88.9 ではなく、実測の通しは 53.7秒）
- 上の数字は Mac M4／メモリ32GB／外付け USB SSD（素読み 950〜970MB/s）での実測です。**秒数を環境またぎで比べないでください**
- 条件と全データ → [実測まとめ](https://uvp.y42u.net/benchmarks/)

### 📥 ダウンロード

チェックサムは `SHA256SUMS-1.6.6.txt` にあります。

```
7181d8c0d92a1b471af97aba86288c7aeec5d9bb9431687b012d36029a5732d4  UwView-1.6.6-linux-aarch64.tar.gz
fbf68f13526519dbdd7e7ab24efce9da2f5a66f86f56c707aa1487307b99c03f  UwView-1.6.6-linux-x86_64.tar.gz
94f841a1004d3711d419188bbe1186ee70026aaa3501d432a6f72ffaa2c53a50  UwView-1.6.6-mac-arm64.dmg
a93b030da32a8190991cd3735c3d88f0b706948cb716e160de0c1fba4d5cd47f  UwView-1.6.6-mac-x64.dmg
d764b74bf11afb5aae9b4309f15c154d483b3864e56362834fce52708972153f  UwView-1.6.6-win-arm64.zip
9130b8173e01d5eb280c917beb06219bee356fb07b1ea8f457f2f9cb8b9b02ea  UwView-1.6.6-win-x64.zip
```

> **配布は GitHub Releases のみ**です（v1.6.4 で一本化しました）。

### 📣 索引を持つ側 — UwView Pro

v1.6.6 で速くなったのは**無料版の1問目**です。**同じファイルに2問目・3問目を投げるなら、`.uwvz` を持つ Pro が別格**で、
50GB の検索が **6.34秒**、2回目に開くのが **0.01〜0.07秒**。**klogg の 16.9倍、無料版 `uvf` の 8.4倍**です。
**「1回だけ調べる」なら無料版で足ります。「何度も戻る」なら Pro です。**
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

---

## UwView v1.6.6 (English)

**The window caught up with the command.**
The GUI can now **start searching without waiting for the index to finish**, and it **opens faster than klogg**.
(The previous free release was v1.6.5.)

### ⚡ 50 GB, open → search → hits on screen: 189.5 s → 53.7 s

Through v1.6.5 the window worked like this:

1. Open the file; build the index in the background (100.6 s at 50 GB)
2. Once the index is done, search (88.9 s)
3. Total: 189.50 s

**The problem was reading it twice.** Step 1 reads everything; step 2 reads everything again.
Meanwhile `uvf … -open` **searches, builds the index and hands it to the window during a single read** — hence 53.69 s.

**v1.6.6 gives the window the same shape.** Because you can search while it is still opening, the file is read once.

| 50 GB, find it and read it | Time | vs v1.6.6 |
|---|---:|---:|
| UwView free GUI, **v1.6.5** | 189.50 s | **3.53×** |
| klogg 24.11.0 | 108.14 s | **2.01×** |
| `uvf … -open` (the command) | 53.69 s | **1.00×** |
| **UwView free GUI, v1.6.6** | **53.7 s** | — |

**53.7 s. 51.25 GB ÷ 53.7 s = 910 MB/s — this drive's raw read speed.**
It lands **0.01 s away from `uvf … -open`'s 53.69 s**. Not a coincidence: **both are the time it takes to read the file once.**

### 📂 "Just opening" got faster too

| Just opening (cold, after `sudo purge`) | 3 GB | 10 GB | 50 GB |
|---|---:|---:|---:|
| klogg 24.11.0 | 3.65 s | 10.98 s | 52.55 s |
| UwView free GUI, **v1.6.5** | 5.27 s | 19.62 s | 100.6 s |
| **UwView free GUI, v1.6.6** | **2.99 s** | **10.13 s** | **50.44 s** |
| **Against klogg** | **1.22×** | **1.08×** | **1.04×** |

That is **967 / 966 / 969 MB/s** — **the same figure at all three sizes**, which is the speed of the medium itself
(3 GB fits in RAM and still reads at 967, so this is not a cached number). **1.99× the 486 MB/s of v1.6.5.**

When v1.6.5 shipped we wrote that **we lose to klogg on opening.** This is the release that pays that back.

### 🧮 No seconds for 3 GB or 10 GB end to end

Honestly: **3 GB and 10 GB are past what we can resolve by hand.**
**At 3 GB it cannot be measured at all — the file finishes opening while you are still typing the search term.**
The "wait, then search" step is gone, so there is no waiting left to time.

**Putting a number on something you cannot measure is not a measurement, it is a sentence.** So we are not doing it.
What we can say is this: **opening and searching run at the same time, and at 3 GB the file is open before you start looking.**

### 🗜 You can now search a `.gz` directly (free edition) — **faster than `zgrep`**

Until now `uvf file.gz 'term'` was refused; you had to hand it to the window with `-open`.
**From v1.6.6 the command searches it directly.**

```bash
uvf app.log.gz 'ERROR'          # no pipe to write
uvf app.log.gz 'ERROR' -open    # hand the hits straight to the window
```

The decompressor was also moved onto the OS's own zlib. That made it **2.0–2.1× faster**
and pushed it **past `gzip -dc | rg`** (i.e. `zgrep`).

| Searching a `.gz` (Mac, cold / hot) | `gzip -dc \| rg` | **`uvf`** | Ratio |
|---|---:|---:|---:|
| 3 GB of text (301 MB gz) | 1.18 s / 1.16 s | **1.16 s / 1.00 s** | **1.02× / 1.16×** |
| 10 GB of text (1.12 GB gz) | 4.38 s / 4.29 s | **3.66 s / 3.38 s** | **1.20× / 1.27×** |
| 50 GB of text (5.75 GB gz) | 22.06 s / 21.71 s | **17.20 s / 17.04 s** | **1.28× / 1.27×** |

Beating the `gzip` command itself comes from not going through a pipe between processes:
decompression and matching overlap inside one process.

> **`.gz` is decompression-bound.** Cold and hot land on nearly the same seconds, which shows the
> limit is CPU, not the disk. That is why making decompression faster showed up one-for-one.

### ⚠️ Known limitation: extremely long lines inside a compressed file

If a `.gz` contains **a single line longer than 64 MiB** (roughly 67 million ASCII characters) and a search
matches that line, you may get an error saying the file is damaged. **The file is not actually damaged.**

**This will not be fixed.** Logs and XML dumps essentially never contain a 64 MiB line, and removing the limit
would require a design with no bound on line length — which slows down every ordinary file as well.
UwView exists to look at huge files fast, so speed wins. If you have such a file, `gunzip` it first.

### 🔧 No regression in the CLI (`uvf`)

The full test set at 3 GB, 10 GB and 50 GB (seven searches, cold plus hot) was compared against v1.6.3:
**every item is within ±6%.** The published ratios against ripgrep (ripgrep wins at 3 GB; 1.08× at 10 GB; 1.10× at 50 GB) still stand.

### ⚠️ Notes

- **Do not add "open" and "search" together to get the end-to-end time.** In v1.6.6 they run at the same time (50 GB is not 50.44 + 88.9; the measured run is 53.7 s)
- Measured on a Mac M4 / 32 GB / external USB SSD (raw read 950–970 MB/s). **Do not compare seconds across machines**
- Conditions and full data → [benchmarks](https://uvp.y42u.net/en/benchmarks-en/)

### 📥 Downloads

Checksums are in `SHA256SUMS-1.6.6.txt`.

```
7181d8c0d92a1b471af97aba86288c7aeec5d9bb9431687b012d36029a5732d4  UwView-1.6.6-linux-aarch64.tar.gz
fbf68f13526519dbdd7e7ab24efce9da2f5a66f86f56c707aa1487307b99c03f  UwView-1.6.6-linux-x86_64.tar.gz
94f841a1004d3711d419188bbe1186ee70026aaa3501d432a6f72ffaa2c53a50  UwView-1.6.6-mac-arm64.dmg
a93b030da32a8190991cd3735c3d88f0b706948cb716e160de0c1fba4d5cd47f  UwView-1.6.6-mac-x64.dmg
d764b74bf11afb5aae9b4309f15c154d483b3864e56362834fce52708972153f  UwView-1.6.6-win-arm64.zip
9130b8173e01d5eb280c917beb06219bee356fb07b1ea8f457f2f9cb8b9b02ea  UwView-1.6.6-win-x64.zip
```

> **Distribution is GitHub Releases only** (consolidated in v1.6.4).

### 📣 The side that keeps an index — UwView Pro

What got faster in v1.6.6 is **the free edition's first question.** Ask the same file a second and third question and
Pro, with its `.uwvz`, is in another class — **6.34 s** to search 50 GB and **0.01–0.07 s** to open it again:
**16.9× klogg and 8.4× our own free `uvf`.**
**Looking once: the free edition is enough. Coming back: Pro.**
→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)
