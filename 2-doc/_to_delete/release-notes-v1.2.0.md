<!--
GitHub Release 用リリースノート（UwView v1.2.0）
- タグ: v1.2.0 ／ タイトル: UwView v1.2.0
- 下の本文（--- 以下）をそのまま GitHub のリリース説明に貼り付けてください。
- 添付アセット: dist/ の各OS向け zip / tar.gz を Release に添付。
- 注: サイト側（download ページの SHA256・バージョン表記）はビルド後に確定するハッシュで更新すること。
-->

---

## UwView v1.2.0

巨大テキストビューア **UwView** の v1.2.0 です。今回は新機能ではなく、**ツールバーの全面アイコン化による操作性の底上げ**と、**不具合修正**が中心の「使い勝手の改善版」です。とくに、**特定の閉じ方でセッション復元が前回位置に戻らなかった問題**を修正しました。

### 🎨 UI 刷新 — ツールバーをアイコン化

- テキストボタン主体だったツールバーを、**フラットアイコン＋ツールチップ**に統一しました（Google Material Icons・Apache License 2.0）。
- ラベル（「検索:」「文字コード:」等）を撤去し、`[入力][操作]` を角丸ボーダーで1つの部品としてまとめ、見た目を整理。
- ツールチップは 250ms で表示。**無効状態のボタンでも説明が出る**ので、アイコンだけでも機能が分かります。
- ハイライタのアイコンだけは「色を設定する機能」と分かるよう色バーに着色しています。

### 🛠 不具合修正

**セッション復元が特定の閉じ方で効かなかった問題を修正（重要）**
終了→再起動しても前回の表示位置に戻らず、毎回ファイル先頭に戻ってしまうことがありました。原因は、× で閉じた際に表示位置が保存されていなかったことと、起動直後（索引が未完成）に行番号での復元ができていなかったことの2点です。

- 表示位置を**バイトオフセット基準**で保存・復元する方式に変更。索引を待たずページモードのまま**即座に前回位置付近を表示**し、索引完成後に自動で行番号へ引き継ぎます。
- **ウィンドウを閉じる操作（×／Cmd+Q／OS シャットダウン）でも位置を保存**するようにしました。
- 旧設定（行番号のみ）からの復元にもフォールバック対応。

**検索「次へ」の起点を修正**
検索ヒットへジャンプした後に行番号ジャンプや手動スクロールで別の場所へ移動しても、「次へ」が元のヒット位置を起点に進んでしまう問題を修正しました。**画面内の直近ジャンプ先（無ければ現在表示位置）**を起点にし、行番号ジャンプ・スクロール・ミニマップ移動すべてに追従します。

### ✨ 表示の改善

- 検索件数を **`現在/総数` 形式**で表示（例: `3/8,739 件`）。ジャンプ・次へ／前へで追従し、新規検索でリセット。
- ページモードの位置表示を **KB 単位**に（例: `100.0% / 50,053,247 KB`）。
- ステータスバーの表示重なり（索引進捗と左側情報）を解消。
- 起動ウィンドウを **1200×720**（最小幅1080）に拡大。従来サイズで右端が見切れていた項目が収まります。
- **日本語／English の実行時切替**を整備。ブラウザ版（WASM）で UI が英語に固定されていた問題も解消しました（日本語リソースを本体に同梱）。

### 📦 ダウンロード

お使いの OS・アーキテクチャに合ったファイルを、このリリースの Assets からダウンロードしてください。

| ファイル | 対象 |
|---|---|
| `UwView-macos-arm64.zip` | macOS（Apple Silicon） |
| `UwView-macos-x64.zip` | macOS（Intel） |
| `UwView-win-arm64.zip` | Windows（ARM64） |
| `UwView-win-x64.zip` | Windows（x64） |
| `UwView-linux-arm64.tar.gz` | Linux（ARM64） |
| `UwView-linux-x64.tar.gz` | Linux（x64） |

- **macOS**: 展開して `UwView.app` を起動（未署名のため初回は右クリック →「開く」）。
- **Windows**: 展開して `UwView.exe` を実行（SmartScreen が出たら「詳細情報」→「実行」）。
- **Linux**: 展開して `chmod +x UwView` → `./UwView`。
- インストール不要で試すなら **[ブラウザ版（WASMデモ）](https://amru195704.github.io/UwView/)**。

### 📄 ライセンス

**PolyForm Internal Use License 1.0.0** — 個人利用および企業の**社内業務利用は無料**です。再配布・製品/サービスへの組込み・転売・第三者提供には別途の商用（再配布）ライセンスが必要です（[Issues](https://github.com/amru195704/UwView/issues) までお問い合わせください）。

### 🔗 リンク

- 公式サイト: https://uvp.y42u.net/ ・ [使い方（ヘルプ）](https://uvp.y42u.net/help/) ・ [お問い合わせ](https://uvp.y42u.net/support/)
- klogg との機能比較（完全版）: [無料版UwViewと上位版Proを全項目で](https://uvp.y42u.net/blog/uwview-klogg-feature-comparison-v111/)

---

### English summary

**UwView v1.2.0** is a usability-and-reliability release rather than a feature release.

- **Toolbar is now fully icon-based** (Google Material Icons, Apache-2.0) with tooltips that appear even on disabled buttons; labels removed and input+action grouped into a single rounded control.
- **Fixed: session restore** didn't return to the previous position for certain ways of closing. Position is now saved on window close (×/Cmd+Q/shutdown) and stored as a **byte offset**, so the previous location is shown immediately in page mode without waiting for indexing.
- **Fixed: search "Next"** now starts from your latest on-screen jump target (or the current view), following line jumps, scrolling, and minimap moves.
- **Improvements**: search count shown as `current/total` (e.g. `3/8,739`); page position in KB; status-bar overlap resolved; startup window enlarged to 1200×720; runtime JA/EN switching (also fixes the WASM build being stuck in English).

Download the build for your OS from the Assets below, or try the **[browser demo](https://amru195704.github.io/UwView/)**. License: PolyForm Internal Use License 1.0.0 (free for personal & internal business use).
