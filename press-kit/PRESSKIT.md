# UwView — Press Kit / プレスキット

*One place with everything needed to write about or list UwView. / UwView を紹介・掲載するための素材一式。*

Last updated: 2026-07 ・ Maintainer: **y4u (amru195704)**

---

## 1. One-liner / 一行キャッチ

- **EN:** Open gigantic text files — hundreds of millions of lines — instantly, with a tiny memory footprint.
- **JA:** 数億行クラスの巨大テキストを、省メモリで一瞬に開くビューア。

## 2. Elevator pitch / 概要

- **EN:** UwView is a cross-platform text **viewer** (not an editor) that opens files ordinary editors choke on. It never loads the whole file into memory and renders only the lines on screen, so it opens instantly and stays light — verified on a **51 GB / 892-million-line** OpenStreetMap XML file. Encoding auto-detection, full-text/regex search with highlight & minimap, multi-file tabs, real-time tail, and a bilingual (EN/JA) UI. Windows / macOS / Linux, plus a browser (WASM) build.
- **JA:** UwView は、通常のエディタが開けない巨大テキストを閲覧する**ビューア**（編集不可）。ファイル全体をメモリに載せず可視行だけを描くので、開いた瞬間から軽快に動きます。**51GB・8.9億行**のOpenStreetMap XMLで実証済み。文字コード自動判定、全文/正規表現検索（ハイライト＋ミニマップ）、複数タブ、リアルタイムTail、日英2言語UI。Windows / macOS / Linux＋ブラウザ(WASM)版。

## 3. Fact sheet / ファクトシート

| 項目 | 内容 |
|------|------|
| 名称 / Name | UwView |
| 種別 / Category | 大容量テキストビューア（閲覧専用）/ Large-text viewer (read-only) |
| 対応OS / Platforms | Windows, macOS, Linux (desktop) ＋ Browser (WASM) |
| 技術 / Tech | .NET 10, Avalonia UI 12 (custom Skia rendering) |
| 言語 / UI languages | 日本語 / English（実行時切替）|
| ライセンス / License | PolyForm Internal Use License 1.0.0（個人・社内業務利用は無料 / free for personal & internal business use；再配布・組込みは別ライセンス / redistribution & embedding require a separate commercial license） |
| 価格 / Price | 本体無料 / Free（将来 Pro 版・GitHub Sponsors を予定） |
| 開発者 / Author | y4u（丸山 — GitHub: amru195704）|
| 起源 / Origin | Windows95 時代に Vector で公開していたビューアを Avalonia で再構築 |

## 4. Key benchmark / 主要実測（差別化の核）

OpenStreetMap 日本全域を XML 化した実データでの計測（Apple Silicon Mac・外付けSSD）。

| Metric / 指標 | Result / 実測値 |
|------|--------|
| File size / サイズ | 51,254,526,392 bytes (~48 GiB) |
| Lines / 行数 | **892,239,125**（`wc -l` と完全一致 / exact match） |
| Open + encoding detect / 起動＋判定 | 12 ms |
| Page-mode display / ページ表示 | 3 ms（索引を待たない / no index needed） |
| Index build / 索引構築 | 172.8 s (283 MB/s, storage-bound) |
| Index size / 索引サイズ | 26.6 MB |
| Managed heap / ヒープ増 | 33.3 MB |
| Jump to last line / 末尾ジャンプ | 0.006 ms |

> 「行数よりストレージ容量が先に効く」— The file body stays non-resident; in practice storage capacity matters before line count does.

Synthetic 200 M lines / 5.1 GB: index build 9.7 s, random GetLine avg 0.005 ms, literal search 3.4 s (1,521 MB/s).

## 5. Links / リンク

| | URL |
|--|-----|
| Repository / リポジトリ | https://github.com/amru195704/UwView |
| Downloads / ダウンロード | https://github.com/amru195704/UwView/releases |
| **Live demo (WASM)** / デモ | https://amru195704.github.io/UwView/ *(公開後に有効 / live once Pages deploys)* |
| README (EN) | https://github.com/amru195704/UwView/blob/main/README.en.md |
| README (JA) | https://github.com/amru195704/UwView/blob/main/README.md |
| Author / 作者 | https://github.com/amru195704 |
| Contact / 連絡先 | https://github.com/amru195704/UwView/issues |

## 6. Screenshots / スクリーンショット

`press-kit/screenshots/`（実データ OSM 日本 51GB・8.9億行を開いた画面）:

| ファイル | 内容 / Caption |
|---------|------|
| `line-mode.png` | ★ヒーロー: 索引完了＝行モード。総行数 892,239,125・行番号表示（English UI）/ Line mode after indexing: 892 M lines |
| `page-mode-indexing.png` | 開いた直後＝ページモードで即表示、裏で索引構築（進捗バー）/ Instant page-mode view while indexing in the background |
| `page-mode-ja.png` | ページモード・日本語UI（% / KB 表示）/ Page mode, Japanese UI |
| `open-dialog.png` | ファイル選択（51.25 GB の japan-latest.osm）/ Opening a 51.25 GB file |

> 日英どちらのUIも実行時に切替可能（ヒーロー=English / `page-mode-ja.png`=日本語）。

## 7. Icon / アイコン

`press-kit/assets/` に UwView アイコン（PNG）。

## 8. Boilerplate / 定型文（そのまま引用可）

> **EN:** UwView is a cross-platform, memory-thrifty viewer for gigantic text files, verified to open a 51 GB / 892-million-line file. Free for personal and internal business use under the PolyForm Internal Use License. Windows / macOS / Linux and a browser build. https://github.com/amru195704/UwView

> **JA:** UwView は、51GB・8.9億行のファイルを開けることを実証した、省メモリな巨大テキストビューア。PolyForm Internal Use License のもと個人・社内業務利用は無料。Windows / macOS / Linux とブラウザ版。https://github.com/amru195704/UwView

---

*This press kit is free to quote and reproduce for coverage of UwView.*
