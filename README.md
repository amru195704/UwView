# UwView

**最大2億行クラスの巨大テキストファイルを、省メモリ・高速に閲覧できるテキストビューア。**

かつて Vector で公開していた大容量テキストビューア UwView を、[Avalonia UI](https://avaloniaui.net/) で再作成するプロジェクトです。通常のエディタは100万行程度で開けなくなりますが、UwView はファイル全体をメモリに載せず、**見えている行だけを描画**することで巨大ファイルを実用的に閲覧できます。RDB/XML ダンプ等で発生する巨大行数ファイルを「とにかく速く見る」ことに特化しています。

エディタではなく **ビューア**（閲覧専用）です。

## 特長

- 🚀 **超高速な巨大ファイル表示** — 最大2億行クラスを省メモリで閲覧。ファイル本体は非常駐、索引は約6MB（2億行時）。
- 📖 **待たされないプログレッシブ・オープン** — 開いた瞬間にページモードで即表示 → 裏で索引構築 → 完了で行モードへ昇格。
- 🈁 **文字コード自動判定** — BOM＋UTF-8 / Shift-JIS / EUC-JP / UTF-16 を自動判別、手動切替も可能（索引再構築なし）。
- 🗂 **マルチファイル・タブ** — 複数ファイルをタブで切替（状態保持で即時・各タブ独立に背景索引）。ドラッグ&ドロップ・複数選択で一括追加。
- 🖥 **全OS同一描画** — Avalonia 独自 Skia 描画により Windows / macOS / Linux で見た目が一致。

## 動作環境

- .NET 10（`global.json`: `10.0.100` / rollForward `latestFeature`）
- Avalonia UI 12.x
- 対応OS（優先）: Windows / macOS / Linux（デスクトップ）
- 将来（おまけ）: ブラウザ（WASM）

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
- **I/O抽象 `IByteSource`**: `Length` と `Read(offset, buffer)` だけの薄い抽象。上位3層は I/O 実装を知らない。

## プロジェクト構成

```
UwView/
├── UwView.slnx              ソリューション
├── global.json             .NET 10 固定
├── UwView.Core/            UI非依存のコア（テスト可能）
│   ├── IByteSource.cs           I/O抽象
│   ├── MmapByteSource.cs        Desktop用 mmap 実装
│   ├── EncodingDetector.cs      文字コード・改行判定
│   ├── SparseLineIndex.cs       スパース行インデックス
│   ├── LineDocument.cs          行/ページのオンデマンド取得
│   └── DocumentSession.cs       1ファイル=1セッション（タブの実体）
├── UwView/                 Avalonia 共有UI
│   ├── Controls/TextView.cs     自前描画の仮想テキスト面（Activeセッションを描画）
│   ├── ViewModels/              MainViewModel（タブ集合＋Active）/ DocumentTabViewModel
│   └── Views/
├── UwView.Desktop/         Win/Mac/Linux head（★優先）
├── UwView.Browser/         WASM head（おまけ・未配線）
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
# 2億行（十数GB・空き容量に注意。まず1000万行程度で確認）
seq 1 200000000 | awk '{print "line "$1" テスト行"}' > huge.txt
# Shift-JIS 版
iconv -f UTF-8 -t SHIFT_JIS huge.txt > huge_sjis.txt
```

## 実装状況

- [x] フェーズ0: 足場（Avalonia ソリューション・Desktop 素ビルド）
- [x] フェーズ1: コアエンジン（`IByteSource` / mmap / 文字コード判定 / スパース索引 / 行・ページ取得）＋ xUnit 検証
- [x] フェーズ2: UI（自前描画 `TextView`・ページモード先行・索引進捗・行モード昇格・文字コード手動切替）
- [x] フェーズ2.5: マルチファイル・タブ（`DocumentSession`・タブ集合＋Active・開く/閉じる/切替/D&D・各タブ独立索引）
- [ ] フェーズ3: 仕上げ（2億行での体感確認・LRU調整・既知制限の明記）
- [ ] フェーズ4: WASM head（おまけ）

## 既知の制限

- 改行スタイルは LF / CRLF を対象。CR単独（旧Mac）は現状フル対応していません。
- UTF-16 は BOM 判定で認識しますが、行分割は `\n`(0x0A) バイト基準のため主対象は UTF-8 / Shift-JIS / EUC-JP です。
- mmap は読み取り専用ビューのため、閲覧中に外部からファイルが切り詰められるとアクセス時に落ちる可能性があります（ビューアとして許容）。

## ライセンス

[MIT License](LICENSE)
