# UwView

**最大2億行クラスの巨大テキストファイルを、省メモリ・高速に閲覧できるテキストビューア。**

かつて Vector で公開していた大容量テキストビューア UwView を、[Avalonia UI](https://avaloniaui.net/) で再作成するプロジェクトです。通常のエディタは100万行程度で開けなくなりますが、UwView はファイル全体をメモリに載せず、**見えている行だけを描画**することで巨大ファイルを実用的に閲覧できます。RDB/XML ダンプ等で発生する巨大行数ファイルを「とにかく速く見る」ことに特化しています。

エディタではなく **ビューア**（閲覧専用）です。

## 特長

- 🚀 **超高速な巨大ファイル表示** — 最大2億行クラスを省メモリで閲覧。ファイル本体は非常駐、索引は約6MB（2億行時）。
- 📖 **待たされないプログレッシブ・オープン** — 開いた瞬間にページモードで即表示 → 裏で索引構築 → 完了で行モードへ昇格。
- 🈁 **文字コード自動判定** — BOM＋UTF-8 / Shift-JIS / EUC-JP / UTF-16 を自動判別、手動切替も可能（索引再構築なし）。
- 🗂 **マルチファイル・タブ** — 複数ファイルをタブで切替（状態保持で即時・各タブ独立に背景索引）。ドラッグ&ドロップ・複数選択で一括追加。
- 🔎 **文字列検索・正規表現** — 索引と独立の背景スキャン。literal はバイト列 SIMD 探索（2億行 3.4秒）、regex は行デコード後マッチ（同 12.7秒）。ヒットはバイトオフセット基準なのでページモード中・文字コード切替後も一貫。可視行の**マッチ部ハイライト**＋**ミニマップ**（全体のヒット分布表示・クリックジャンプ）。
- 🧵 **行フィルタ表示** — 検索ヒット行だけを抽出表示（元ファイル不変・仮想ビュー）。元の行番号付き。
- ⭐ **ブックマーク** — 任意行をトグル・前後ジャンプ。バイトオフセット保持なので文字コード切替後も有効。ミニマップに青表示。
- 📡 **リアルタイム Tail** — 追記を検知して mmap 再マップ＋**増分索引**＋末尾自動スクロール。他プロセスが書き込み中のログも開ける（FileShare.ReadWrite）。
- 🖥 **全OS同一描画** — Avalonia 独自 Skia 描画により Windows / macOS / Linux で見た目が一致。ブラウザ版は Noto Sans JP 同梱で日本語表示。

## 動作環境

- .NET 10（`global.json`: `10.0.100` / rollForward `latestFeature`）
- Avalonia UI 12.x
- 対応OS（優先）: Windows / macOS / Linux（デスクトップ）
- おまけ: ブラウザ（WASM）。ビルド/実行には `wasm-tools` ワークロードが必要（`sudo dotnet workload install wasm-tools`）

## アーキテクチャ

巨大ファイルの鉄則「ファイル全体をメモリに載せない／全行を UI に載せない」を4層で実現しています。

```
UI 層（TextView: 自前描画の仮想テキスト面）   … 可視行だけを Render で描画
      ↓ GetPageAt(byteOffset) / GetLine(lineIndex)
ドキュメント層（LineDocument）                … オンデマンド取得＋LRUキャッシュ
      ↓
インデックス層（SparseLineIndex）             … N行ごと(既定256)のスパース索引で省メモリ
      ↓ Read(offset, length)
I/O 抽象層（IByteSource）                      … Desktop: mmap ／ Browser: Blob.slice
```

- **スパース行インデックス**: 全行のオフセットを持つと2億行で約1.6GBになるため、256行ごとに1つだけ記録（約6MB）。任意行は直近チェックポイントから改行を数え直して求める。
- **I/O抽象 `IByteSource`**: `Length` と `Read(offset, buffer)` だけの薄い抽象（＋WASM 用の `ReadAsync`）。上位3層は I/O 実装を知らない。Desktop は mmap の同期経路、Browser は `blob.slice` の非同期経路＋チャンクキャッシュ（256KB×64＝16MB上限）を使う。

## 実測（フェーズ3受入・Apple Silicon Mac / 外付けSSD）

2億行・5.1GB の UTF-8 ファイル（`UwView.Bench` で計測）:

| 項目 | 実測値 | 目標（§8） |
|------|--------|-----------|
| open＋文字コード判定 | 10 ms | 即ページモード表示 ✓ |
| ページモード表示（先頭50行＋50%位置50行） | 0 ms | 索引を待たない ✓ |
| 索引構築（1回の順次読み） | 9.7 s（538 MB/s） | 進捗・キャンセル可 ✓ |
| 総行数 | 200,000,000（正確） | 一致 ✓ |
| 索引サイズ | チェックポイント781,251件 ≒ 6.0 MB | 数MB ✓ |
| マネージヒープ増 | 9.3 MB | 数MB規模 ✓ |
| GetLine ランダム1000回 | 平均 0.005 ms / p99 0.007 ms | 体感即時（数ms） ✓ |
| 末尾（2億行目）ジャンプ | 0.003 ms | 正しい行が見える ✓ |
| 文字コード切替 | 0.9 ms・索引再構築なし | 即反映 ✓ |
| 文字列検索 literal | 3.4 s（1,521 MB/s） | 背景・キャンセル可 |
| 文字列検索 regex | 12.7 s（414 MB/s） | 同上 |

※ WorkingSet は mmap のファイルページを含むため大きく見えるが、OS が随時回収する非常駐キャッシュでありアプリの割当メモリではない。

再計測:

```bash
dotnet run --project UwView.Bench -c Release -- <巨大テキストファイル>
```

## プロジェクト構成

```
UwView/
├── UwView.slnx              ソリューション
├── global.json             .NET 10 固定
├── UwView.Core/            UI非依存のコア（テスト可能）
│   ├── IByteSource.cs           I/O抽象（Read／ReadAsync・INotifyDataArrived）
│   ├── MmapByteSource.cs        Desktop用 mmap 実装
│   ├── EncodingDetector.cs      文字コード・改行判定（同期/非同期）
│   ├── SparseLineIndex.cs       スパース行インデックス
│   ├── LineDocument.cs          行/ページのオンデマンド取得
│   └── DocumentSession.cs       1ファイル=1セッション（タブの実体）
├── UwView/                 Avalonia 共有UI
│   ├── Controls/TextView.cs     自前描画の仮想テキスト面（Activeセッションを描画）
│   ├── Services/IDocumentOpener.cs  「開く」のhead差し替え点（Desktop実装同居）
│   ├── ViewModels/              MainViewModel（タブ集合＋Active）/ DocumentTabViewModel
│   └── Views/
├── UwView.Desktop/         Win/Mac/Linux head（★優先）
│   └── macos/                   UwView.app バンドル生成（build-app.sh）
├── UwView.Browser/         WASM head（おまけ）
│   ├── BlobByteSource.cs        blob.slice ランダム読み＋チャンクキャッシュ
│   ├── BrowserDocumentOpener.cs JSファイル選択 → セッション生成
│   └── wwwroot/blobRead.js      JS 側（File保持・slice読み）
├── UwView.Bench/           フェーズ3実測ハーネス（索引時間・GetLine応答）
└── UwView.Core.Tests/      xUnit 検証
```

## ビルドと実行

```bash
# 依存復元＋ビルド
dotnet build

# デスクトップ版を起動（macOS の例）
dotnet run --project UwView.Desktop -c Debug
```

起動後、`[開く…]` からテキストファイルを選択すると閲覧できます。

- **ジャンプ**: 行モードでは行番号、ページモードでは `50%` のように割合を入力
- **文字コード**: ツールバーのドロップダウンで自動判定／手動切替
- **スクロール**: ホイール・↑↓・PageUp/Down・Home/End・縦スクロールバー

## テスト

```bash
dotnet test UwView.Core.Tests/UwView.Core.Tests.csproj
```

10万行の既知ファイルで行取得・総行数の正しさ、UTF-8 / Shift-JIS / EUC-JP / UTF-16 の判定、CRLF・末尾改行なし・空ファイルなどを検証します。

検証用の巨大ファイル生成例:

```bash
# 2億行・約5GB（空き容量に注意。まず1000万行程度で確認）
# 注意: macOS の seq は浮動小数点実装のため大きな範囲で「1e+07」表記や行数ズレを起こす。
#       awk の整数ループで生成すること。
awk 'BEGIN{for(i=1;i<=200000000;i++) printf "line %d テスト行\n", i}' > huge.txt
# Shift-JIS 版
iconv -f UTF-8 -t SHIFT_JIS huge.txt > huge_sjis.txt
```

## 実装状況

- [x] フェーズ0: 足場（Avalonia ソリューション・Desktop 素ビルド）
- [x] フェーズ1: コアエンジン（`IByteSource` / mmap / 文字コード判定 / スパース索引 / 行・ページ取得）＋ xUnit 検証
- [x] フェーズ2: UI（自前描画 `TextView`・ページモード先行・索引進捗・行モード昇格・文字コード手動切替）
- [x] フェーズ2.5: マルチファイル・タブ（`DocumentSession`・タブ集合＋Active・開く/閉じる/切替/D&D・各タブ独立索引）
- [x] フェーズ3: 仕上げ（2億行実測＝§8受入基準クリア・文字コード切替再索引なし確認・README/既知制限整備）
- [x] フェーズ4: WASM head（`BlobByteSource`・`ReadAsync` 非同期経路・チャンクキャッシュ。ブラウザ起動確認済み）
- [x] §11-①: 文字列検索（`SearchService`・背景スキャン・バイトオフセット基準・進捗/キャンセル/上限100万件）
- [x] §11-②⑥: 検索ハイライト（可視行の再マッチ描画）＋ミニマップ（ヒット分布・クリックジャンプ）
- [x] §11-③: リアルタイム Tail（ポーリング検知・mmap 再マップ・`SparseLineIndex.ExtendAsync` 増分索引・末尾追従）
- [x] §11-④: ブックマーク（バイトオフセット基準・トグル/前後ジャンプ・可視行マーカー＋ミニマップ表示）
- [x] §11-⑤: 行フィルタ表示（ヒット行だけの仮想ビュー・元行番号表示・タブごとに状態保持）
- [x] WASM 日本語フォント同梱（Noto Sans JP / OFL。豆腐解消）

## ブラウザ版（WASM・おまけ）

```bash
# 初回のみ（要管理者権限）: Skia のネイティブ再リンクに必要
sudo dotnet workload install wasm-tools

# 開発サーバーで起動（Chromium 系推奨）
dotnet run --project UwView.Browser
```

デスクトップ版と同じUI・同じコアが動きます。I/O は `blob.slice` によるランダム読み（ファイル全体はメモリに載せない）。

## 既知の制限

- 改行スタイルは LF / CRLF を対象。CR単独（旧Mac）は現状フル対応していません。
- UTF-16 は BOM 判定で認識しますが、行分割は `\n`(0x0A) バイト基準のため主対象は UTF-8 / Shift-JIS / EUC-JP です。
- mmap は読み取り専用ビューのため、閲覧中に外部からファイルが切り詰められるとアクセス時に落ちる可能性があります（ビューアとして許容）。Tail は追記のみ対応（切り詰め・ローテーションは非対応）。
- literal 検索の高速バイトパスは Shift-JIS でまれに文字境界をまたぐ誤ヒットがありえます（UTF-8 は構造上起きない）。厳密にしたい場合は正規表現モードを使ってください（デコード後にマッチ）。
- ブラウザ版: 毎回ファイル選択が必要（パス直開き・D&D・Tail 不可）、索引構築はネイティブより遅い、描画は取得済みチャンクから行われるため未取得範囲は一瞬空白になり、届き次第再描画されます。

## ライセンス

[MIT License](LICENSE)
