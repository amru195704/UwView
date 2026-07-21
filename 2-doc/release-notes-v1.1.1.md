<!--
GitHub Release 用リリースノート（UwView v1.1.1）
- タグ: v1.1.1 ／ タイトル: UwView v1.1.1
- 下の本文（--- 以下）をそのまま GitHub のリリース説明に貼り付けてください。
- 添付アセット: dist/ の各OS向け zip / tar.gz を Release に添付。
-->

---

## UwView v1.1.1

巨大テキストビューア **UwView** の v1.1.1 です。v1.1 で入れた「色分けハイライタ」を**実戦速度**で使うための右クリック着色と、**開き直せば前回の続きから**始められるセッション復元を追加しました。

### 🆕 新機能

**選択語の右クリック着色（クイックカラーラベル）**
語を選んで**右クリック → 色スウォッチ**で、その語を即座に着色できます。同じ語は可視行が再評価されすべて着色されます。

- ダブルクリックは3段階で対象範囲を選択：**最短の語**（`req-8f2a` の `req` だけ）→ もう一度で**区切りの良いトークン**（`req-8f2a` 全体）→ もう一度で**解除**。
- 選択が空のときはキャレット位置の語が対象。
- 右クリックメニューには色スウォッチのほか「コピー」「解除」「管理ダイアログ…」。
- 着色した規則は**ハイライタ管理ダイアログ**にも現れ、色変更・名前付きセット保存・`.uwvhl` 書き出しの対象になります。
- ハイライタは**タブごと**に保持されます。

**セッション復元・最近使ったファイル・お気に入り**
作業の続きから、すぐ再開できます。

- 通常起動時に「**前回のファイル（n件）を復元しますか？**」を確認し、OK で前回のタブ群を**前回のスクロール位置付近**まで復元します。
- ファイルを指定して起動したとき（ダブルクリック／ドラッグ&ドロップ／CLI 引数）は、確認なしでそのファイルだけを開きます。
- **最近使ったファイル**（上限15・新しい順）と**お気に入り**（★トグル）を、ファイル未オープン時の**スタート画面**から開けます。
- 設定は原子的書き込みで保存し、破損に強くしています。

### ✨ 改善

- 同梱ハイライタ・プリセットに **GeoJSON・KML を追加**（汎用ログ／syslog・Linux／Web access／JSON ログ／GNSS NMEA と合わせて**計7種**）。
- 色設定（ハイライタ管理）ダイアログのボタンを **「保存・設定・キャンセル」** に整理。
- 既存機能（検索・ミニマップ・Tail・ブックマーク）との干渉なし。単体テストは全緑です。

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
- v1.1.1 の紹介記事: [右クリックで即色分け、開き直せば前回の続きから](https://uvp.y42u.net/blog/uwview-v111-quick-colorize-restore/)
- klogg との機能比較（完全版）: [無料版UwViewと上位版Proを全項目で](https://uvp.y42u.net/blog/uwview-klogg-feature-comparison-v111/)

---

### English summary

**UwView v1.1.1** adds two everyday-use features on top of v1.1's multi-keyword highlighter:

- **Right-click colorize (Quick Color Labels)** — select a word and right-click to color it instantly; all occurrences on visible lines are highlighted. Double-click cycles through *shortest word → token → clear*. Colored rules appear in the Highlighter manager and can be saved to `.uwvhl`. Highlighters are kept **per tab**.
- **Session restore / Recent files / Favorites** — on normal launch, UwView asks to restore the previous file set and scroll positions. Recent files (up to 15) and favorites are available from the start screen. Launching with a specific file opens just that file, without a prompt.
- **Bundled presets** now include **GeoJSON and KML** (7 presets total). Highlighter dialog buttons reorganized (Save / Settings / Cancel).

Download the build for your OS from the Assets below, or try the **[browser demo](https://amru195704.github.io/UwView/)**. License: PolyForm Internal Use License 1.0.0 (free for personal & internal business use).
