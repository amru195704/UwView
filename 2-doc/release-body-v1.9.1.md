<!--
パス: UwView/2-doc/release-body-v1.9.1.md（GitHub Releases の本文。下書き・開発部 UwView開発課・2026-10-18）
- 中身: UwView 998f342（new v1.9.0）→ 4844e07（187233a の取り消し・NewlineCounter・版数）。e7c2207・e224dc2 はマニュアルだけ。
- 根拠: 2-ops/管理部/報告_開発部.md 10/16「v1.9.1 の作業開始」、検査 2-ops/報告/2026-10/1018-検査_v1.9.1.md。
- 速さの数字は書かない（uvf では測っていない。uvp の hot 2〜13% は 1.5 倍未満なので「同等」）。
- 要判断: 「注意」の VMware の共有フォルダーの行（187233a で 1.9.0 の直しを取り消したため。検査 §3 軽微 6）を載せるか。
- 公開リポなので、非公開リポの名前・パスは書かない。金額も書かない（社内規程 4-1）。
- 【dist が揃ったら差し込む】チェックサム。今あるのは mac-arm64 だけ（build 26.10.16.09・cd3160bc…2c3265）。
-->
*日本語 ｜ [English](#uwview-v191-english)*

## UwView v1.9.1

v1.9.1 は小さな更新です。検索で行番号を出すための改行の数え方を、速い方法に替えました（結果は前と同じです）。
（前の版は v1.9.0 です → [v1.9.0 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.9.0.md)）

### ⚡ 行番号の数え方

- 当たった行の行番号を出すための改行の数えを、速い方法に替えました。Apple シリコンなどの ARM の機械で効きます。出る結果は v1.9.0 と同じです。

### ⚠️ 注意

- Linux の VMware の共有フォルダー（vmhgfs-fuse）の上で開いているファイルを外で切り詰めると、直後に「作り直されました」と出ることがあります（その場合も［読み直す］で読み直せます）。

### 📥 インストール・ダウンロード

```bash
brew install --cask amru195704/uwview/uwview     # Mac（更新は brew upgrade --cask uwview）
scoop install uwview                             # Windows（更新は scoop update uwview）
sudo snap install uwview                         # Linux（更新は snap refresh uwview）
```

手で入れる場合は、このページの下の **Assets** から取ってください。Mac は dmg（Apple シリコン〈M1〜M4〉用の `arm64`・Intel 用の `x64`）、Windows は zip（`x64`・`arm64`）、Linux は tar.gz（`x86_64`・`aarch64`）です。チェックサムは `SHA256SUMS-1.9.1.txt` にあります。

```
【dist が揃ったら SHA256SUMS-1.9.1.txt の 6 行を差し込む】
（2026-10-◯◯ 作成。macOS 版は署名・公証済み。ビルド番号は 26.10.◯◯.◯◯（日本時間）です）
```

> **配布は GitHub Releases が元です。** Homebrew・Scoop・Snap も、ここのファイルを使います（Homebrew と Scoop は SHA256 を照合します）。操作説明とお知らせは blog サイト（https://uvp.y42u.net/）に載せています。

### 📣 UwView Pro 側 — 索引の中身を UTF-8 にそろえる

有償版の `uvp` と UwView Pro の画面も v1.9.1 になりました。

- Shift-JIS・EUC-JP・UTF-16 を UTF-8 の索引に変えるとき、改行を LF にそろえ、BOM を索引の中に持たないようにしました（元に戻すときは付け直します）。
- UTF-32 のファイルは扱わないことにしました。
- 索引のブロックを 1MB から 256KB にしました。索引の形が新しくなったので（版 5）、前の版で作った索引は初めて使うときに作り直します。

→ [UwView Pro](https://uvp.y42u.net/pro/)

---

## UwView v1.9.1 (English)

*[日本語](#uwview-v191) ｜ English*

v1.9.1 is a small update. Line numbers in search results are now counted with a faster method (the results are the same as before).
(The previous release is v1.9.0 → [what changed in v1.9.0](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.9.0.md))

### ⚡ Counting line numbers

- The newline counting used to print line numbers of hits now uses a faster method. It helps most on ARM machines such as Apple silicon. Output is identical to v1.9.0.

### ⚠️ Notes

- On a VMware shared folder on Linux (vmhgfs-fuse), truncating an open file from outside may briefly be reported as “recreated”. **Reload** still reloads it.

### 📥 Install / download

```bash
brew install --cask amru195704/uwview/uwview     # Mac (update: brew upgrade --cask uwview)
scoop install uwview                             # Windows (update: scoop update uwview)
sudo snap install uwview                         # Linux (update: snap refresh uwview)
```

To install by hand, get the files from **Assets** below: a dmg for the Mac (`arm64` for Apple silicon M1–M4, `x64` for Intel), a zip for Windows (`x64`, `arm64`), a tar.gz for Linux (`x86_64`, `aarch64`). Checksums are in `SHA256SUMS-1.9.1.txt`.

```
[insert the 6 lines of SHA256SUMS-1.9.1.txt once all builds exist]
(built 2026-10-xx; the macOS builds are signed and notarized. Build number: 26.10.xx.xx (JST))
```

> **GitHub Releases is the source.** Homebrew, Scoop and Snap use the files here (Homebrew and Scoop check the SHA256). Instructions and news are on the blog (https://uvp.y42u.net/).

### 📣 On the UwView Pro side — the index is now UTF-8 throughout

The paid `uvp` and the UwView Pro window are also at v1.9.1.

- When Shift-JIS, EUC-JP and UTF-16 files are converted into the UTF-8 index, line endings become LF and BOMs are not kept in the index (they are put back when restoring).
- UTF-32 files are no longer handled.
- Index blocks are now 256 KB (was 1 MB). The index format is new (v5), so indexes made by earlier versions are rebuilt on first use.

→ [UwView Pro](https://uvp.y42u.net/pro/)
