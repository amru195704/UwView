## UwView v1.4.0

**有料版 [UwView Pro](https://uvp.y42u.net/pro/) との版数統一リリースです。** 無料版の機能追加はありません（機能は v1.2.2 と同一）。不具合修正が1件入っています。

### 🛠 不具合修正

**行モードで、ステータス表示とスクロールバーの位置が常に 0% になっていた問題を修正**

表示位置の割合を、モードにかかわらずバイト位置（`TopByteOffset`）から計算していました。行モードでは先頭位置を行番号側が保持しており、バイト位置は据え置かれるため、どこまでスクロールしても 0% のままになっていました。モードに応じた現在位置を基準に算出するよう修正しています。

### 📦 内部変更（無料版の動作に影響なし）

設定ファイルの移行処理に、ライセンス情報を引き継がない選択肢を追加しました（UwView Pro 側で、無料版と設定を共有していた状態から専用ファイルへ分離する際に使います）。検索履歴・お気に入り・言語などの道具設定の引き継ぎは従来どおりです。

### 📥 ダウンロード

.NET のインストールは不要です（自己完結型）。

| ファイル | 対象 |
| --- | --- |
| `UwView-1.4.0-win-x64.zip` | Windows（x64） |
| `UwView-1.4.0-win-arm64.zip` | Windows（ARM64） |
| `UwView-1.4.0-mac-arm64.zip` | macOS（Apple Silicon） |
| `UwView-1.4.0-mac-x64.zip` | macOS（Intel） |
| `UwView-1.4.0-linux-x86_64.tar.gz` | Linux（x86_64） |
| `UwView-1.4.0-linux-aarch64.tar.gz` | Linux（ARM64） |

展開後、macOS 版は `UwView.app` をそのまま起動、Windows / Linux 版は同梱の実行ファイル（`UwView.exe` / `UwView`）を実行してください。Windows 版は現在未署名のため、SmartScreen が出た場合は〔詳細情報〕→〔実行〕でお進みください。

同じアーカイブはリポジトリの [`dist/`](https://github.com/amru195704/UwView/tree/main/dist) にも同梱しています。

<details>
<summary>SHA256SUMS</summary>

```
13a4de5bfae73cb26dde8aab1a2e7dbce3824cf1ee03d84d5622cea0cd31881a  UwView-1.4.0-linux-aarch64.tar.gz
f4331115096bde82dcd8f83d09adb3969896d1fecac8bdc7dc1aff20a4270daf  UwView-1.4.0-linux-x86_64.tar.gz
716335788bd167ea5675990c6b5c77f7afc4ed02a61be08255b47195b370db33  UwView-1.4.0-mac-arm64.zip
5f3d7baa007e66585bd1536c77f2fc7c989ee57ee3371e6f9f8dc2995e150c85  UwView-1.4.0-mac-x64.zip
d5a56bcdbd79f5b77c317b2fea889d6736980c8aa211611c3c31416f9ea4a18e  UwView-1.4.0-win-arm64.zip
502a685746d207bd9394f4013a9729951cf4408f0a8aef7dcece438e0df6f7ed  UwView-1.4.0-win-x64.zip
```

</details>

### 🚀 UwView Pro V1.4.0 も同時公開

有料版の [UwView Pro](https://uvp.y42u.net/pro/) は、V1.4.0 で**編集機能を Edit ライセンスとして統合**しました。

- **View ライセンス**（買い切り $129 ／ 月額 $9）: 永続索引による2回目以降の瞬間オープン、圧縮サイドカーキャッシュ（`.uwvz`・約1/9保管）経由の高速検索、多段階検索（Drill-down・最大8段）、シーケンシャル検索（`w1 → w2 → w3` の順序一致）
- **Edit Upgrade**（買い切り +$120 ／ 月額 +$8・View ライセンスが必要）: 元ファイルを書き換えない差分編集（全置換・中断・翌日再開）
- **どのライセンスも14日間の無料試用**があります（支払い情報は不要。試用キーは発行から14日で失効します）

無料版の UwView は今後も無料のまま、巨大ファイルの閲覧・検索用途で提供を続けます。


---

## English summary — UwView v1.4.0

**A version-alignment release with the paid [UwView Pro](https://uvp.y42u.net/en/pro-en/).** No new features in the free edition (functionally identical to v1.2.2), plus one bug fix.

### 🛠 Bug fix

**Fixed: in line mode, the status bar and scrollbar position were always stuck at 0%**

The scroll percentage was computed from the byte offset (`TopByteOffset`) regardless of mode. In line mode the top position is held by the line number and the byte offset stays put, so the value never moved off 0%. It now uses the current position appropriate to the mode.

### 📦 Internal change (no effect on the free edition)

Settings migration gained an option to *not* carry the license over — used by UwView Pro when splitting its settings out of the file it used to share with the free edition. Search history, favourites and language are carried over as before.

### 📥 Downloads

No .NET installation required (self-contained builds).

| File | Platform |
| --- | --- |
| `UwView-1.4.0-win-x64.zip` | Windows (x64) |
| `UwView-1.4.0-win-arm64.zip` | Windows (ARM64) |
| `UwView-1.4.0-mac-arm64.zip` | macOS (Apple Silicon) |
| `UwView-1.4.0-mac-x64.zip` | macOS (Intel) |
| `UwView-1.4.0-linux-x86_64.tar.gz` | Linux (x86_64) |
| `UwView-1.4.0-linux-aarch64.tar.gz` | Linux (ARM64) |

After extracting: on macOS launch `UwView.app`; on Windows / Linux run the bundled executable (`UwView.exe` / `UwView`). The Windows build is currently unsigned — if SmartScreen appears, choose *More info* → *Run anyway*. The same archives are also committed to [`dist/`](https://github.com/amru195704/UwView/tree/main/dist). SHA256 checksums are in the collapsed section above.

### 🚀 UwView Pro V1.4.0 ships alongside

[UwView Pro](https://uvp.y42u.net/en/pro-en/) V1.4.0 **integrates editing as an Edit license**.

- **View License** ($129 one-time / $9 per month): instant reopen from a persistent index, fast search through the compressed sidecar cache (`.uwvz`, ~1/9 storage), drill-down search (up to 8 stages), and sequence search (`w1 → w2 → w3` in that order)
- **Edit Upgrade** (+$120 one-time / +$8 per month — requires an active View License): non-destructive editing that never rewrites the original (replace-all, pause, resume the next day)
- **Every license comes with a 14-day free trial** — no payment details required; the trial key expires 14 days after issue

The free UwView stays free, for viewing and searching huge files.

**Full changelog** → [README (English)](https://github.com/amru195704/UwView/blob/main/README.en.md) ／ **Website** → https://uvp.y42u.net/en/

---

**すべての変更履歴** → [README の実装状況](https://github.com/amru195704/UwView#実装状況) ／ **公式サイト** → https://uvp.y42u.net/
