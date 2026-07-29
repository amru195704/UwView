## UwView v1.2.1

**v1.2.0 の不具合修正版**です。v1.2.0（ツールバーの全面アイコン化）の内容をすべて含んだうえで、**ブラウザ版（WASM）の表示不具合**と、**検索結果一覧のスクロールの重さ**を解消しました。デスクトップ版にも影響する修正が 2 件あります。

> v1.2.0 は配布前に本バージョンへ差し替えたため、**v1.2.0 → v1.2.1 の差分だけでなく v1.1.1 からの変更**が入っています。v1.2.0 の内容（ツールバーのアイコン化・セッション復元の修正・検索「次へ」の起点修正ほか）は末尾の「v1.2.0 の内容」にまとめてあります。

### 🛠 不具合修正（デスクトップ版にも影響）

**スクロールバーを末尾までドラッグすると画面が真っ白になる問題を修正**
縦スクロールバーを最下端までドラッグすると、1 行も表示されなくなることがありました。スクロールバーの上限値に総行数をそのまま設定していたため、表示開始行がファイル末尾を越えてしまうのが原因です（40 万行のファイルで「先頭 400,001 行目」になる状態）。1 画面分を差し引いた値を上限とするよう修正しました。

**検索結果一覧のスクロールが重い問題を解消**
一覧の行の作り方が UI の仮想化（表示中の行だけを扱う仕組み）に適合しておらず、**ヒット件数分の行を毎回すべて生成**していました。ヒットが多いほど顕著に重くなります。メイン画面と同じ**自前描画**方式へ置き換え、可視行だけを描くようにしました。

あわせて、一覧の**左ドラッグでスクロールバーのつまみを掴めなかった**問題も解消しています（一覧側がマウス左ボタンの入力を先に受け取っていたため）。メイン画面と同じ操作感になりました。

### 🌐 ブラウザ版（WASM）の修正

ブラウザ版はファイルを分割して遅延取得する仕組みのため、デスクトップ版には出ない不具合がありました。

**本文が表示されない／行番号がずれる問題を修正**
行番号だけが表示されて本文が空になる、`…（省略）` が大量に出る、検索結果一覧が空欄だらけになる、といった症状がありました。**「データがまだ届いていない」状態を「ファイルの終端」と誤判定**し、空の内容をそのまま記憶してしまうのが原因です。未到着を正しく区別し、届いた時点で描き直すようにしました。検索結果の行番号がずれて表示される問題（例: 5,000 行目が `4,865` と表示）も同じ原因で解消しています。

**日本語入力でローマ字が残る問題を修正**
検索欄に「東京」と入力すると `toukyou東京` のようにローマ字が混ざっていました。変換確定した文字だけが入るようにしました。

**前後±N 表示でフリーズする問題を修正**
「前後 ± 1」に切り替えるとブラウザのタブが固まることがありました。行番号の算出が未取得データに当たると処理がループしてしまうのが原因です。算出を非同期化し、結果を保持して繰り返さないようにしました。切り替え直後はヒット行だけを表示し、算出が済んだ時点で前後行が追加されます。

**語の色設定で対象語がずれる問題を修正**
語をダブルクリックして右クリックすると、選択した語とは別の語（手前の語）が色設定の対象になっていました。ブラウザ版では等幅フォントが使えず文字幅が一定でないため、座標計算がずれていたのが原因です。

**読み込み・検索を高速化**
ブラウザとプログラム間のデータ受け渡しが 1 バイトずつ変換する方式になっており、検索処理そのものより重くなっていました。一括コピー方式へ変更しています。

### ✨ 操作性の改善

- **検索結果一覧を閉じて開き直しても、表示位置と「前後 ± N」を引き継ぐ**ようになりました（従来は毎回初期化）。
- **「行番号を含める」チェックが全ての出力に効く**よう統一しました。従来は「一覧全体の保存」だけが従い、行選択のコピー/保存は常に行番号付き、Pro の矩形選択は常に行番号なしと、ばらついていました。
- カラー設定画面の「キャンセル」を「**閉じる**」に変更（表示のみ。挙動は従来どおり未確定の編集を破棄）。
- 最小ウィンドウ幅を 930 → **780** に縮小。狭い画面でも使えます。

### ℹ️ ブラウザ版の制約について

ブラウザ版は**タブを背面にすると検索・読み込みがほぼ停止**します。ブラウザが非表示タブの処理を抑制するためで、大きなファイルを扱う間はタブを前面にしておいてください。デスクトップ版にこの制約はありません。

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

### v1.2.0 の内容（本リリースに含まれます）

- **ツールバーを全面アイコン化**（Google Material Icons・Apache License 2.0）。ラベルを撤去し `[入力][操作]` を角丸ボーダーで 1 つの部品にまとめ、ツールチップは 250ms で表示（無効状態のボタンでも表示）。
- **セッション復元が特定の閉じ方で効かなかった問題を修正**。表示位置をバイトオフセット基準で保存し、× / Cmd+Q / OS シャットダウンでも保存するようにしました。索引を待たず前回位置付近を即表示します。
- **検索「次へ」の起点を修正**。画面内の直近ジャンプ先（無ければ現在表示位置）を起点にし、行番号ジャンプ・スクロール・ミニマップ移動に追従します。
- 検索件数を `現在/総数` 形式に（例: `3/8,739 件`）。ページモードの位置表示を KB 単位に。ステータスバーの表示重なりを解消。
- **日本語／English の実行時切替**を整備（ブラウザ版が英語固定だった問題も解消）。

---

### English summary

**UwView v1.2.1** is a bug-fix release that supersedes v1.2.0 (which was not published). It contains everything from v1.2.0 plus the following fixes.

**Affects the desktop build too**

- **Fixed: blank screen when dragging the scrollbar to the bottom.** The scrollbar maximum was set to the total line count, so the top line could go past the end of the file. It is now capped one screen short.
- **Fixed: slow scrolling in the search-results list.** UI virtualisation was not taking effect, so every hit row was created on each rebuild. The list is now custom-drawn like the main view, rendering only visible rows. This also restores left-drag on its scrollbar, which the list had been intercepting.

**Browser (WASM) build**

- **Fixed: missing body text and wrong line numbers.** Not-yet-fetched data was mistaken for end-of-file and the empty result was cached. Incomplete reads are no longer cached and are redrawn once the data arrives.
- **Fixed: IME composition leaking romaji** into the search box (typing 東京 produced `toukyou東京`).
- **Fixed: freeze when switching to ±N context lines.** Line-number resolution looped on unfetched data; it is now asynchronous and cached.
- **Fixed: word colour-labelling targeted the wrong word,** caused by fixed-width coordinate maths under a proportional font.
- **Faster loading and search** — byte transfer between the browser and the program was converting one byte at a time; it now copies in bulk.

**Usability**

- The results popup now **keeps its scroll position and ±N setting** when closed and reopened.
- **"Include line numbers" now applies to every output** (list save, selected-rows copy/save, and Pro's rectangular selection), which previously behaved inconsistently.
- Colour settings dialog: "Cancel" renamed to "Close". Minimum window width reduced from 930 to 780.

**Note on the browser build**: processing is throttled almost to a stop while the tab is in the background — keep it in the foreground when working with large files. The desktop build is unaffected.

Download the build for your OS from the Assets below, or try the **[browser demo](https://amru195704.github.io/UwView/)**. License: PolyForm Internal Use License 1.0.0 (free for personal & internal business use).
