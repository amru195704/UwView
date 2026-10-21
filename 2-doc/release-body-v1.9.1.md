<!--
パス: UwView/2-doc/release-body-v1.9.1.md（GitHub Releases の本文。下書き・開発部 UwView開発課・2026-10-18）
- 中身: UwView 998f342（new v1.9.0）→ 4844e07（187233a の取り消し・NewlineCounter・版数）。e7c2207・e224dc2 はマニュアルだけ。
- 根拠: 2-ops/管理部/報告_開発部.md 10/16「v1.9.1 の作業開始」、検査 2-ops/報告/2026-10/1018-検査_v1.9.1.md。
- 速さの数字は書かない（uvf では測っていない。uvp の hot 2〜13% は 1.5 倍未満なので「同等」）。
- 「注意」の VMware の共有フォルダーの行は、オーナー了承（2026-10-18 20:51「公開して」）。187233a で 1.9.0 の直しを取り消したため（検査 §3 軽微 6）。
- 公開リポなので、非公開リポの名前・パスは書かない。金額も書かない（社内規程 4-1）。
- dist: 2026-10-21 に 6 本とも作り直し（オーナー・Mac・build/publish.sh）。build 26.10.21.09。チェックサムは dist/SHA256SUMS-1.9.1.txt と照合済み 6/6。
-->
*日本語 ｜ [English](#uwview-v191-english)*

## UwView v1.9.1

v1.9.1 は小さな更新です。検索で行番号を出すための改行の数え方を、速い方法に替えました（結果は前と同じです）。
（前の版は v1.9.0 です → [v1.9.0 で何が変わったか](https://github.com/amru195704/UwView/blob/main/2-doc/release-body-v1.9.0.md)）

### ⚡ 行番号の数え方

- ヒットした行の行番号を出すための改行の数えを、速い方法に替えました。出る結果は v1.9.0 と同じです。

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
d378779916d2798ca23efe92823bed83ebcdba12e8de2ba1b4cb1b6f5c6913b7  UwView-1.9.1-linux-aarch64.tar.gz
7e86ba9860b77ac350f2c07939bf8207bceae07de5f8b072ddd9c7bb11d33043  UwView-1.9.1-linux-x86_64.tar.gz
d1d13abf36cb804635c91c1dc59ed07e57e2131ade24c9d91673e0967bcef747  UwView-1.9.1-mac-arm64.dmg
86ce6ca2d6e05d754a5770b65dd6694b64c52874c25d0d9037cedf3b7cbf1604  UwView-1.9.1-mac-x64.dmg
25ab0fda63ca97fb4b69fc4c90492690b8bc2373b3ac661b2840176b6a2dd2b0  UwView-1.9.1-win-arm64.zip
13fdf7db3594b44f916a0a28165fd9f0df2e7a88a8333b665e1df466a01d8979  UwView-1.9.1-win-x64.zip
（2026-10-21 作成。macOS 版は署名・公証済み。ビルド番号はすべて 26.10.21.09（日本時間）です）
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

- The newline counting used to print line numbers of hits now uses a faster method. Output is identical to v1.9.0.

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
d378779916d2798ca23efe92823bed83ebcdba12e8de2ba1b4cb1b6f5c6913b7  UwView-1.9.1-linux-aarch64.tar.gz
7e86ba9860b77ac350f2c07939bf8207bceae07de5f8b072ddd9c7bb11d33043  UwView-1.9.1-linux-x86_64.tar.gz
d1d13abf36cb804635c91c1dc59ed07e57e2131ade24c9d91673e0967bcef747  UwView-1.9.1-mac-arm64.dmg
86ce6ca2d6e05d754a5770b65dd6694b64c52874c25d0d9037cedf3b7cbf1604  UwView-1.9.1-mac-x64.dmg
25ab0fda63ca97fb4b69fc4c90492690b8bc2373b3ac661b2840176b6a2dd2b0  UwView-1.9.1-win-arm64.zip
13fdf7db3594b44f916a0a28165fd9f0df2e7a88a8333b665e1df466a01d8979  UwView-1.9.1-win-x64.zip
(built 2026-10-21; the macOS builds are signed and notarized. Build number: 26.10.21.09 (JST) for all builds)
```

> **GitHub Releases is the source.** Homebrew, Scoop and Snap use the files here (Homebrew and Scoop check the SHA256). Instructions and news are on the blog (https://uvp.y42u.net/).

### 📣 On the UwView Pro side — the index is now UTF-8 throughout

The paid `uvp` and the UwView Pro window are also at v1.9.1.

- When Shift-JIS, EUC-JP and UTF-16 files are converted into the UTF-8 index, line endings become LF and BOMs are not kept in the index (they are put back when restoring).
- UTF-32 files are no longer handled.
- Index blocks are now 256 KB (was 1 MB). The index format is new (v5), so indexes made by earlier versions are rebuilt on first use.

→ [UwView Pro](https://uvp.y42u.net/pro/)
