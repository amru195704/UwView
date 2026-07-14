#!/usr/bin/env bash
# UwView.app を Developer ID 署名 → Apple 公証 → staple する（App Store 外配布用）。
#
# 前提（初回のみ・§0-claude/macOS署名・notarize手順.md 参照）:
#   1) キーチェーンに "Developer ID Application: 名前 (TEAMID)" 証明書がある
#        確認: security find-identity -v -p codesigning
#   2) notarytool のキーチェーンプロファイルを登録済み
#        xcrun notarytool store-credentials "uwview-notary" \
#          --apple-id "AppleID" --team-id "TEAMID" --password "App固有パスワード"
#
# 使い方:
#   UWVIEW_SIGN_IDENTITY="Developer ID Application: 名前 (TEAMID)" \
#   UWVIEW_NOTARY_PROFILE="uwview-notary" \
#   ./sign-notarize.sh path/to/UwView.app
# または:
#   ./sign-notarize.sh path/to/UwView.app "Developer ID Application: 名前 (TEAMID)" uwview-notary
set -euo pipefail

APP="${1:?usage: sign-notarize.sh <UwView.app> [IDENTITY] [NOTARY_PROFILE]}"
IDENT="${2:-${UWVIEW_SIGN_IDENTITY:-}}"
PROFILE="${3:-${UWVIEW_NOTARY_PROFILE:-uwview-notary}}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENT="$HERE/UwView.entitlements"
EXE="UwView.Desktop"   # Contents/MacOS 内の apphost 名（AssemblyName 既定）

if [ -z "$IDENT" ]; then
  echo "エラー: 署名IDが未指定。UWVIEW_SIGN_IDENTITY か第2引数で指定してください。" >&2
  echo "  例: security find-identity -v -p codesigning で表示される" >&2
  echo "      \"Developer ID Application: 名前 (TEAMID)\" をそのまま渡す" >&2
  exit 1
fi

echo "==> 1/4 ネストされた Mach-O（dylib/so）を署名"
find "$APP/Contents" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
| while IFS= read -r -d '' f; do
    codesign --force --timestamp --options runtime -s "$IDENT" "$f"
  done

echo "==> 2/4 メイン実行体を署名（entitlements 付き）"
codesign --force --timestamp --options runtime --entitlements "$ENT" -s "$IDENT" "$APP/Contents/MacOS/$EXE"

echo "==> 3/4 .app 本体を署名（内側→外側）"
codesign --force --timestamp --options runtime --entitlements "$ENT" -s "$IDENT" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

echo "==> 4/4 公証（notarytool submit --wait）→ staple"
ZIP="${APP%.app}-notarize.zip"
ditto -c -k --keepParent "$APP" "$ZIP"        # zip は必ず ditto（zip コマンドは構造破壊）
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
rm -f "$ZIP"
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"

echo "==> Gatekeeper 最終確認"
spctl --assess --type execute --verbose=4 "$APP" || true
echo "==> 完了: $APP （署名＋公証＋staple 済み）"
