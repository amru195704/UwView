#!/usr/bin/env bash
# UwView.app（macOS アプリバンドル）を生成する。
# 使い方: ./build-app.sh [RID]     RID 既定 = osx-arm64（Apple Silicon）。Intel は osx-x64。
# 出力: UwView.Desktop/bin/<RID>/UwView.app
set -euo pipefail

RID="${1:-osx-arm64}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ_DIR="$(dirname "$HERE")"                 # UwView.Desktop/
PROJ="$PROJ_DIR/UwView.Desktop.csproj"
PUBLISH="$PROJ_DIR/bin/Release/net10.0/$RID/publish"
APP="$PROJ_DIR/bin/$RID/UwView.app"
EXE="UwView.Desktop"                          # apphost 名（AssemblyName 既定）

echo "==> publish ($RID, self-contained)"
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained -p:UseAppHost=true

echo "==> assemble $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$HERE/Info.plist"  "$APP/Contents/Info.plist"
cp "$HERE/UwView.icns" "$APP/Contents/Resources/UwView.icns"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/$EXE"

# Apple Silicon は無署名バイナリを実行拒否するため ad-hoc 署名を付ける
# （正式配布には Developer ID 署名＋notarize が別途必要）
codesign --force --deep -s - "$APP" 2>/dev/null || echo "warn: codesign 失敗（続行）"

# 自機での検証用に隔離属性を落としておく
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

echo "==> done: $APP"
