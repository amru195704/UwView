*日本語 ｜ [English](#english)*

## UwView V1.5.1

**「開くまでの手数」と「どれだけ待たされたか」を直したリリースです。**
有料版 UwView Pro と版番号を揃えました（v1.4.0 からの更新です）。

### 📂 Finder から開けるようにしました

これまでは**アプリを起動してからファイルを選ぶ**しかありませんでした。次の3つを追加します。

| 入口 | 動作 |
|---|---|
| `open -a UwView file.log` | 引数のファイルを開いて起動 |
| Finder の「このアプリケーションで開く」 | 一覧に UwView が出ます |
| アプリのアイコンへドラッグ&ドロップ | 落としたファイルを開きます |

**起動中でも受け取ります**（新しいタブが増えます）。複数ファイルの同時指定にも対応しました。

### ⏱ 検索の状況をダイアログに出し、処理時間を残す

検索中に何も出ず、終わると結果一覧がすぐ前に出るので、**件数も所要時間も読む間がありません**でした。
有料版 UwView Pro と同じ出し方に揃えます。

- 検索を始めると**状況ダイアログ**が出ます。進み具合（％）と、そこまでに見つかった件数が動きます
- **「中止」でいつでも止められます**（それまでに見つかった分は残ります）
- 終わると `2,000 件見つかりました` と経過時間を出したまま残り、
  **「閉じる」を押してから結果一覧が出ます**（件数と時間を読んでから次へ進みます）
- ブラウザ版はウィンドウを作れないので、同じ内容を画面に重ねて出します

所要時間はこれまでどこにも残らず、「さっきの検索は何秒だったか」を後から確かめられませんでした。

- ステータスバー右側に **`直前: 検索 8.32 秒（1,396,454 件）`** の形で1件だけ出します
- その場所に**マウスを置くと、直近10件**が時刻つきで読めます
- 対象は**開く（行索引の作成まで）・検索・抽出保存**。補足に行数やヒット件数が付きます
- 単位は値の大きさで変えます（`5:28.3` / `16.83 秒` / `340 ミリ秒` / `0.42 ミリ秒`）。
  一瞬で終わる処理が「0.0 秒」になって意味を失わないようにするためです

表示言語を切り替えると記録は消えます（記録は切り替え前の言語の文字列のため）。

### 💾 検索結果の保存に指定を追加

結果一覧の保存で選べる項目が増え、**次に開いたときも同じ指定で始まります**。

- **行番号を付ける** … 保存する各行の先頭に元ファイルの行番号を置きます
- **1行目をヘッダーに** … 元ファイルの1行目を出力の先頭に置きます（CSV/TSV 用）
- **前後±N行も保存** … ヒット行だけでなく文脈行も一緒に書き出します

### 🌐 ブラウザ版に起動時のご案内

ブラウザ版（WASM）は開いたときに、できること・向かないことを先に出すようにしました。

- **3GB（1億行）程度までは問題なく動きます**
- それを超える場合は **UwView アプリ**（このページのダウンロード版）をお使いください
- 高度な検索が必要な場合は **UwView Pro** をご検討ください（編集できるアップグレードもあります）

**日本語 / English の切り替え**もこの画面から行えます。「OK」を押すと操作を始められます。

### 🍎 macOS 版を DMG にしました

配布形式を zip から **DMG** に変えました。開いて `UwView.app` を Applications へドラッグするだけです。
あわせて **Developer ID 署名と Apple 公証**を行いました（有料版と同じ扱い）。
これまで必要だった「右クリック →『開く』」の回避手順は要らなくなります。

### 🔧 その他

- ボタンにマウスを置くと説明が出るようにしました（全ボタン）
- 処理時間の記録は有料版 UwView Pro と**共通のコード**です（両版で同じ表示になります）

### 📥 ダウンロード

| OS | ファイル |
|---|---|
| macOS (Apple Silicon) | `UwView-1.5.1-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.5.1-mac-x64.dmg` |
| Windows (x64) | `UwView-1.5.1-win-x64.zip` |
| Windows (ARM64) | `UwView-1.5.1-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.5.1-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.5.1-linux-aarch64.tar.gz` |

macOS 版は **DMG になりました**（従来は zip）。**v1.5.1 から Developer ID 署名・Apple 公証済み**で、
DMG 自体も署名・公証・staple しているため、警告なしに開けます。
Windows 版は未署名です（SmartScreen が出たら「詳細情報」→「実行」）。

チェックサムは `SHA256SUMS-1.5.1.txt` で確認できます。同じアーカイブはリポジトリの [`dist/`](https://github.com/amru195704/UwView/tree/main/dist) にも同梱しています。

---

<a name="english"></a>

## UwView v1.5.1 (English)

**This release is about the steps it takes to open a file, and about knowing how long you waited.**
The version number now matches the paid UwView Pro (the previous free release was v1.4.0).

### 📂 You can now open files from Finder / Explorer

Until now the only way in was to launch the app and then pick a file. Three new entry points:

| Entry point | Behaviour |
|---|---|
| `open -a UwView file.log` | Opens the file given as an argument |
| "Open With" in Finder | UwView now appears in the list |
| Drag & drop onto the app icon | Opens whatever you drop |

**It also accepts files while already running** (they arrive as new tabs), and multiple files at once.

### ⏱ A progress dialog for search, and a record of how long things took

Previously nothing appeared during a search, and the moment it finished the results window jumped in front — **leaving no time to read either the hit count or the elapsed time**. This now works the same way as UwView Pro.

- Starting a search opens a **progress dialog**: percentage complete, and the number of hits so far
- **Cancel at any time** (whatever was found so far is kept)
- When it finishes, the dialog stays up showing `2,000 matches found` and the elapsed time. **The results window opens only after you close it**
- The browser build cannot create a window, so it shows the same thing as an overlay

Elapsed times were not recorded anywhere, so "how long did that search take?" could not be answered afterwards.

- The right of the status bar now shows one line: **`Last: search 8.32 s (1,396,454 matches)`**
- **Hover over it to see the last 10**, with timestamps
- Covered operations: **open (through line-index construction), search, and save-extracted**. Line counts and hit counts are included
- Units scale with the value (`5:28.3` / `16.83 s` / `340 ms` / `0.42 ms`), so an operation that finishes instantly does not collapse into a meaningless "0.0 s"

Switching the display language clears the record (the entries are strings in the previous language).

### 💾 More options when saving search results

The save dialog for the results window has more options, and **they are remembered for next time**.

- **Include line numbers** — prefixes each saved line with its line number in the original file
- **Use the first line as a header** — puts the original file's first line at the top of the output (for CSV/TSV)
- **Include ±N lines of context** — writes the surrounding lines as well as the matches

### 🌐 A short notice on first launch of the browser build

The browser (WASM) build now says up front what it is good for and what it is not.

- **Up to about 3 GB (100 million lines) it works without trouble**
- Beyond that, use the **UwView desktop app** (the downloads below)
- If you need advanced search, consider **UwView Pro** (an editing upgrade is available too)

**Japanese / English** can be switched from this screen. Press OK to continue.

### 🍎 The macOS build is now a DMG

The distribution format has changed from zip to **DMG** — open it and drag `UwView.app` to Applications.
It is now **signed with a Developer ID and notarized by Apple**, the same as the paid build.
The old workaround of "right-click → Open" is no longer needed.

### 🔧 Other changes

- Tooltips on every button
- The timing record shares its code with UwView Pro, so both builds display it identically

### 📥 Downloads

| OS | File |
|---|---|
| macOS (Apple Silicon) | `UwView-1.5.1-mac-arm64.dmg` |
| macOS (Intel) | `UwView-1.5.1-mac-x64.dmg` |
| Windows (x64) | `UwView-1.5.1-win-x64.zip` |
| Windows (ARM64) | `UwView-1.5.1-win-arm64.zip` |
| Linux (x86_64) | `UwView-1.5.1-linux-x86_64.tar.gz` |
| Linux (ARM64) | `UwView-1.5.1-linux-aarch64.tar.gz` |

No .NET installation is required (self-contained).

**macOS is a DMG from this release on** (it used to be a zip). **From v1.5.1 it is signed with a Developer ID and notarized by Apple**, and the DMG itself is signed, notarized and stapled, so it opens without any warning.
The Windows build is unsigned — if SmartScreen appears, choose **More info → Run anyway**.

Checksums: `SHA256SUMS-1.5.1.txt`, also in [`dist/`](https://github.com/amru195704/UwView/tree/main/dist) along with the same archives.
