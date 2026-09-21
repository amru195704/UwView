*日本語 ｜ [English](#uwview-v1661-english)*

## UwView v1.6.6.1

**v1.6.6「First Light」の修正版です。** 画面の使い方は変わりません。
（前回の無料版は v1.6.6 です → [v1.6.6 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.6.6.md)）

### 🔧 変わったこと（無料版）

- **`uvf --version`／`uvf --help` を追加しました。** UwView Pro の `uvp` と同じ書き方で、版数と使い方を表示します。
- **版数が「1.6.6.1」と正しく表示されるようになりました。** これまでは4桁目が切れて「1.6.6」と表示されていました。
- **圧縮ファイルを読めないときの案内を、理由ごとに分けました。** tar でまとめたもの・gzip が二重にかかったもの・
  名前は .zip でも中身が zip でないもの・途中で切れているもの、を区別して伝えます。
  **「壊れています」とは言い切らず、「元のファイルは消さないでください」と添えます。**
  別の形式や作り方の違いであることが多く、ファイル自体は正しいことがほとんどだからです。
- 画面の検索結果が、まれにファイルの順に並ばないことがあった点を直しました。

### 📣 UwView Pro 側の変更 — 1問目が「作りながら探す」に

v1.6.6.1 のいちばん大きな変更は、有償版の `uvp` コマンドです。
これまで1問目は「`.uwvz` を作る → できた `.uwvz` を読んで探し直す」の2段でしたが、**作りながら探す1段**になりました。

| 50GB・1問目（`.uwvz` を消してから・キャッシュ無し） | 時間 |
|---|---:|
| `uvp` v1.6.6 | 65.7秒 |
| **`uvp` v1.6.6.1** | **59.04秒** |

ripgrep と比べた1問目の遅れは **約6%** まで縮みました。残っているのは `.uwvz` を書く分です。
**2問目からは 50GB を 6.6秒で探します**（ripgrep の 8.4倍。この値は変わりません）。
→ [UwView Pro](https://uvp.y42u.net/pro/)（買い切り $129 ／ 月額 $9・**14日間の無料試用**つき）

### ⚠️ 注意

- 速さの比較（ripgrep・klogg との比較、3GB／10GB／50GB）は [README](https://github.com/amru195704/UwView#readme) と
  [実測まとめ](https://uvp.y42u.net/benchmarks/) にまとめています。**秒数を環境またぎで比べないでください。**
- `.gz` の中に1行が 64MiB を超える行があり、その行が検索に一致した場合、「最後まで読めませんでした」という案内が出て
  検索が止まることがあります。ファイル自体は壊れていません。展開してから開いてください（対応予定はありません）。

### 📥 ダウンロード

チェックサムは `SHA256SUMS-1.6.6.1.txt` にあります。

```
dbece86da7b08950f291b18a8d2089504f850bc640719dda718ce6b5b2e8af2d  UwView-1.6.6.1-linux-aarch64.tar.gz
30adc0122c36917fd989b0a140fe2d92d1d4889b5517c49547f931fd95eba3fb  UwView-1.6.6.1-linux-x86_64.tar.gz
987c7dbd7cda539cdc804da39f76929b58842574e083ba39173957b2742e436f  UwView-1.6.6.1-mac-arm64.dmg
9a7a1e06cb838ab25d5a7af099444fa34bc1dd993d48a743e56dcb47a005d2a4  UwView-1.6.6.1-mac-x64.dmg
493efb94756154618ac8380c1684e42bc4fd24f1406320224cb7bf0a1d285f12  UwView-1.6.6.1-win-arm64.zip
846fa8586f6d6f3efcfa6704f121874a5c9bf2ed1f0eb737e0365779e86c6000  UwView-1.6.6.1-win-x64.zip
```

> **配布は GitHub Releases のみ**です。操作説明と最新情報は blog サイト（https://uvp.y42u.net/）で。

---

## UwView v1.6.6.1 (English)

**A fix-up release of v1.6.6 "First Light".** Nothing changes in how you use the window.
(The previous free release was v1.6.6 → [what changed in v1.6.6](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.6.6.md))

### 🔧 What changed (free edition)

- **Added `uvf --version` and `uvf --help`**, spelled the same way as UwView Pro's `uvp`.
- **The version now shows as "1.6.6.1".** The fourth digit used to be cut off, so it showed "1.6.6".
- **When a compressed file cannot be read, the message now says why.** A tar archive, a doubly gzipped file,
  a file named .zip that is not a zip, and a file that is cut short are told apart.
  **It no longer says the file is "damaged", and it asks you to keep the original** — most of the time the file
  is fine and was simply made in a different way.
- Fixed a rare case where the window's search results were not listed in file order.

### 📣 On the UwView Pro side — the first question now searches while building

The biggest change in v1.6.6.1 is in the paid `uvp` command.
Its first question used to take two steps — build the `.uwvz`, then read that `.uwvz` to search again.
**It now searches while building, in one step.**

| 50 GB, first question (`.uwvz` removed, no cache) | Time |
|---|---:|
| `uvp` v1.6.6 | 65.7 s |
| **`uvp` v1.6.6.1** | **59.04 s** |

The first question's gap to ripgrep is down to **about 6%**; what remains is writing the `.uwvz`.
**From the second question on it searches 50 GB in 6.6 s** (8.4× ripgrep — unchanged).
→ [UwView Pro](https://uvp.y42u.net/en/pro-en/) ($129 one-time or $9/month, with a **14-day free trial**)

### ⚠️ Notes

- Speed comparisons (against ripgrep and klogg, at 3 GB / 10 GB / 50 GB) are in the [README](https://github.com/amru195704/UwView#readme)
  and the [benchmarks](https://uvp.y42u.net/en/benchmarks-en/). **Do not compare seconds across machines.**
- If a `.gz` contains a single line longer than 64 MiB and a search matches it, the search may stop with a message that
  the file "could not be read to the end". The file is not damaged. Please extract it first (this will not be fixed).

### 📥 Downloads

Checksums are in `SHA256SUMS-1.6.6.1.txt`.

```
dbece86da7b08950f291b18a8d2089504f850bc640719dda718ce6b5b2e8af2d  UwView-1.6.6.1-linux-aarch64.tar.gz
30adc0122c36917fd989b0a140fe2d92d1d4889b5517c49547f931fd95eba3fb  UwView-1.6.6.1-linux-x86_64.tar.gz
987c7dbd7cda539cdc804da39f76929b58842574e083ba39173957b2742e436f  UwView-1.6.6.1-mac-arm64.dmg
9a7a1e06cb838ab25d5a7af099444fa34bc1dd993d48a743e56dcb47a005d2a4  UwView-1.6.6.1-mac-x64.dmg
493efb94756154618ac8380c1684e42bc4fd24f1406320224cb7bf0a1d285f12  UwView-1.6.6.1-win-arm64.zip
846fa8586f6d6f3efcfa6704f121874a5c9bf2ed1f0eb737e0365779e86c6000  UwView-1.6.6.1-win-x64.zip
```

> **Distribution is GitHub Releases only.** How-to guides and news are on the blog: https://uvp.y42u.net/
