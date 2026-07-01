#!/usr/bin/env bash
# UwView.app（macOS アプリバンドル）を生成する。
# 使い方: ./build-app.sh [RID]     RID 既定 = osx-arm64（Apple Silicon）。Intel は osx-x64。
set -euo pipefail

RID="${1:-osx-arm64}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ_DIR="$(dirname "$HERE")"                 # UwView.Desktop/
PROJ="$PROJ_DIR/UwView.Desktop.csproj"
PUBLISH="$PROJ_DIR/bin/Release/net10.0/$RID/publish"
APP="$PROJ_DIR/bin/UwView.app"
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

# 未署名配布時に Gatekeeper の隔離属性を落としておく（自機向け）
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

echo "==> done: $APP"
