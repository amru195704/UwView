> パス: UwView/0-claude/macOS署名・notarize手順.md

# UwView macOS 署名＋公証（notarize）手順

対象: Avalonia/.NET 10 製 `UwView.app` を **App Store 外配布**する場合の、Developer ID 署名＋Apple 公証（notarization）＋staple の手順。実行は Mac のターミナルで行う。

> App Store 配布は別物（App Sandbox＋`Apple Distribution` 証明書。公証は不要）。本書は Developer ID 配布（サイト/GitHub 等で直接配る）前提。

---

## 0. 全体像（5ステップ）

```
1. 前提準備（Developer登録・証明書・認証情報）
2. ユニバーサル .app をビルド（arm64＋x64）
3. codesign（Hardened Runtime＋entitlements、内側→外側の順）
4. notarytool で Apple に提出→合否待ち
5. stapler で公証チケットを添付→検証→配布（DMG）
```

Gatekeeper が「未確認の開発元」で弾かないためには **署名＋公証＋staple の3つが揃うこと**が必要。

---

## 1. 前提準備（初回のみ）

### 1.1 Apple Developer Program

- 有料メンバーシップが必要（約 99 USD/年）。個人 or 組織で登録。
- **Team ID** を控える（developer.apple.com → Membership details）。以後 `<TEAM_ID>` と表記。

### 1.2 Developer ID Application 証明書

Xcode があるなら簡単:
- Xcode → Settings → Accounts → 対象 Apple ID → **Manage Certificates** → 左下「＋」→ **Developer ID Application** を作成。
- 作成すると login キーチェーンに秘密鍵付きで入る。

確認:
```bash
security find-identity -v -p codesigning
# 例: "Developer ID Application: Yuji Maruyama (<TEAM_ID>)" が出ればOK
```
以後この文字列を `IDENT` に使う。

### 1.3 公証用の認証情報（どちらか一方）

**A. App用パスワード（手軽・手動向き）**
- appleid.apple.com → サインインとセキュリティ → **App固有パスワード**を発行。
- キーチェーンにプロファイル保存（毎回パスワードを打たなくて済む）:
```bash
xcrun notarytool store-credentials "uwview-notary" \
  --apple-id "あなたのAppleID@example.com" \
  --team-id "<TEAM_ID>" \
  --password "xxxx-xxxx-xxxx-xxxx"   # 上で発行したApp固有パスワード
```

**B. App Store Connect API キー（CI・自動化向き・推奨）**
- App Store Connect → Users and Access → Integrations（Keys）→ Developer 権限でキー発行 → `AuthKey_XXXX.p8` をDL。
- 提出時に `--key AuthKey_XXXX.p8 --key-id <KEY_ID> --issuer <ISSUER_ID>` を使う（`store-credentials` にも登録可）。

---

## 2. ユニバーサル .app をビルド

### 2.1 各アーキテクチャを publish
```bash
cd UwView/UwView.Desktop   # Desktop head

dotnet publish -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -o out/arm64
dotnet publish -c Release -r osx-x64 --self-contained \
  -p:PublishSingleFile=true -o out/x64
```
> `PublishSingleFile=true` は署名対象を減らし手順を簡略化できる。ネイティブ .dylib は別出力されることがあるので、後段の署名は「バンドル内の全 Mach-O」を対象にする（§3）。

### 2.2 ユニバーサル実行体を作る（lipo）
```bash
lipo -create -output out/UwView \
  out/arm64/UwView \
  out/x64/UwView
lipo -info out/UwView   # arm64 x86_64 と出ればOK
```
同様に、両アーキで別々に出た `*.dylib` があれば同名同士を `lipo -create` で結合する。

### 2.3 .app バンドル構成を組む
```
UwView.app/
└── Contents/
    ├── Info.plist
    ├── MacOS/        ← UwView(ユニバーサル)＋必要な .dylib 群
    └── Resources/    ← UwView.icns
```
`Info.plist` 必須キー:
```xml
<key>CFBundleIdentifier</key>       <string>jp.y4u.uwview</string>
<key>CFBundleName</key>              <string>UwView</string>
<key>CFBundleExecutable</key>        <string>UwView</string>
<key>CFBundlePackageType</key>       <string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
<key>CFBundleVersion</key>           <string>1</string>
<key>LSMinimumSystemVersion</key>    <string>11.0</string>
<key>CFBundleIconFile</key>          <string>UwView.icns</string>
```
> 手作業が面倒なら、コミュニティ製 **MacOsPublish**（build/bundle/codesign/notarize を一括、arm64＋x64 同梱）や Avalonia 公式ドキュメントのバンドル手順を利用してよい。

---

## 3. codesign（Hardened Runtime＋entitlements）

### 3.1 entitlements ファイル（.NET 必須）

`UwView.entitlements` を用意（.NET の JIT/実行メモリ・混在署名ライブラリ読込のため）:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
 "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
```
> Developer ID 配布では App Sandbox 不要。UwView は任意ファイルを mmap するので、むしろ **非サンドボックスが素直**（サンドボックス化＝App Store 化の際は user-selected-file 権限が別途必要）。

### 3.2 内側→外側の順で署名

Hardened Runtime（`--options runtime`）＋タイムスタンプ付きで、**まずバンドル内の全 Mach-O（dylib/so/実行体）**を署名し、**最後に .app 本体**を署名する。
```bash
APP="UwView.app"
IDENT="Developer ID Application: Yuji Maruyama (<TEAM_ID>)"
ENT="UwView.entitlements"

# 1) ネストされたライブラリ類を先に署名
find "$APP/Contents" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
| while IFS= read -r -d '' f; do
    codesign --force --timestamp --options runtime -s "$IDENT" "$f"
  done

# 2) メイン実行体（entitlements 付き）
codesign --force --timestamp --options runtime \
  --entitlements "$ENT" -s "$IDENT" "$APP/Contents/MacOS/UwView"

# 3) 最後に .app 全体（entitlements 付き）
codesign --force --timestamp --options runtime \
  --entitlements "$ENT" -s "$IDENT" "$APP"

# 検証
codesign --verify --deep --strict --verbose=2 "$APP"
```

---

## 4. notarytool で提出

zip はシンボリックリンク/構造を壊さないよう **ditto** で作る（`zip` コマンドは不可）:
```bash
ditto -c -k --keepParent "UwView.app" "UwView.zip"

xcrun notarytool submit "UwView.zip" \
  --keychain-profile "uwview-notary" \
  --wait
```
- `--wait` で処理完了までブロックし、`status: Accepted` を待つ。
- **失敗（Invalid）時**は理由を確認:
```bash
xcrun notarytool log <submission-id> --keychain-profile "uwview-notary"
```
（署名漏れ・Hardened Runtime 未設定・タイムスタンプ無し等が典型原因）

---

## 5. staple → 検証 → 配布（DMG）

### 5.1 公証チケットを .app に添付
```bash
xcrun stapler staple "UwView.app"
xcrun stapler validate "UwView.app"

# Gatekeeper 相当の最終確認（accepted と出ればOK）
spctl --assess --type execute --verbose=4 "UwView.app"
```

### 5.2 配布用 DMG（推奨）

DMG 自体も署名・公証・staple しておくと、DMG のままダウンロードされても Gatekeeper が通る:
```bash
hdiutil create -volname "UwView" -srcfolder "UwView.app" -ov -format UDZO "UwView.dmg"
codesign --force --timestamp -s "$IDENT" "UwView.dmg"
xcrun notarytool submit "UwView.dmg" --keychain-profile "uwview-notary" --wait
xcrun stapler staple "UwView.dmg"
xcrun stapler validate "UwView.dmg"
```
> zip 配布なら「.app を staple → ditto で再 zip」して配る。DMG 配布なら上記。

---

## 6. チェックリスト / よくある落とし穴

- [ ] すべての Mach-O（メイン＋全 dylib/so）を **Hardened Runtime＋タイムスタンプ**で署名したか（`--options runtime` と `--timestamp`）。
- [ ] entitlements（allow-jit / allow-unsigned-executable-memory / disable-library-validation）を付けたか。付け忘れると起動時クラッシュや公証 Invalid。
- [ ] zip は **ditto**（`zip` は構造破壊で公証失敗）。
- [ ] staple 忘れ → オフライン環境やDL直後に「壊れている/開発元未確認」表示。
- [ ] 署名は **内側→外側**。`--deep` 署名は非推奨（検証にのみ `--deep` を使う）。
- [ ] `notarytool` は現行ツール。旧 `altool` は廃止済み。
- [ ] Team ID / IDENT 文字列 / キーチェーンプロファイル名を実値に置換したか。

---

## 7. 自動化メモ（任意）

- 上記 §2〜§5 を1本の `sign_and_notarize.sh` にまとめ、`UwView.Desktop` のリリース時に実行。
- CI（GitHub Actions の macOS ランナー）では認証情報を **App Store Connect API キー（§1.3B）** ＋証明書を一時キーチェーンにインポートする方式が定番。
- バージョンは `Info.plist` の `CFBundleShortVersionString`/`CFBundleVersion` を更新して再実行。
