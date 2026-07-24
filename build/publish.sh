#!/usr/bin/env bash
# UwView (Free / UVF) — 配布ビルド生成（mac / Linux / Windows）。
#
# 生成物: dist/UwView-<ver>-mac-<arch>.zip(.app内包) / -linux-<arch>.tar.gz / -win-<arch>.zip
#         ＋ dist/SHA256SUMS.uwview。バージョンは UwView/UwView.csproj <Version> と一致。
#
# 注意: UVF の dist/ はリリース資産としてトラック済みのため、dist 全体は消さない
#       （対象の出力ファイルだけを上書きする）。
#
# 署名・公証は環境変数がある時のみ（mac: MAC_SIGN_ID / AC_PROFILE、win: EV は publish-win.ps1）。
# 使い方: build/publish.sh [rids...]   既定: osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-arm64
set -euo pipefail
cd "$(dirname "$0")/.."

APP_PROJ="UwView.Desktop/UwView.Desktop.csproj"
VER=$(grep -oE '<Version>[^<]+' UwView/UwView.csproj | sed 's/<Version>//' | head -1)
: "${VER:=0.0.0}"
OUT="dist"
EXE="UwView.Desktop"   # 単一ファイル実行体名（プロジェクト名由来）
RIDS=("$@"); [ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-arm64)

mkdir -p "$OUT"
echo "UwView $VER → ${RIDS[*]}"

publish_one() {
  local rid="$1" pubdir="obj/pub/$rid"
  rm -rf "$pubdir"
  dotnet publish "$APP_PROJ" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o "$pubdir" 1>&2
  echo "$pubdir"
}

pack_mac() { # $1=rid  $2=arch-label
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local app="$OUT/UwView.app"
  local macos="$app/Contents/MacOS"
  rm -rf "$app"; mkdir -p "$macos" "$app/Contents/Resources"
  cp -R "$pub/." "$macos/"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>UwView</string>
  <key>CFBundleDisplayName</key><string>UwView</string>
  <key>CFBundleIdentifier</key><string>net.y42u.uwview</string>
  <key>CFBundleExecutable</key><string>$EXE</string>
  <key>CFBundleIconFile</key><string>UwView.icns</string>
  <key>CFBundleShortVersionString</key><string>$VER</string>
  <key>CFBundleVersion</key><string>$VER</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
  cp "UwView.Desktop/macos/UwView.icns" "$app/Contents/Resources/UwView.icns"
  chmod +x "$macos/$EXE"

  if [ -n "${MAC_SIGN_ID:-}" ]; then
    echo "  codesign ($arch)…"
    codesign --force --deep --options runtime --timestamp --sign "$MAC_SIGN_ID" "$app"
    if [ -n "${AC_PROFILE:-}" ]; then
      local nz="$OUT/UwView-$VER-mac-$arch-notarize.zip"
      ditto -c -k --keepParent "$app" "$nz"
      xcrun notarytool submit "$nz" --keychain-profile "$AC_PROFILE" --wait
      xcrun stapler staple "$app"; rm -f "$nz"
    fi
  else
    echo "  ⚠ MAC_SIGN_ID 未設定 → 未署名（配布前に署名・公証が必要）"
  fi

  local out="$OUT/UwView-$VER-mac-$arch.zip"
  rm -f "$out"; ditto -c -k --keepParent "$app" "$out"
  rm -rf "$app"
  echo "  → $out"
}

pack_linux() { # $1=rid  $2=arch-label(x86_64/aarch64)
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local out="$OUT/UwView-$VER-linux-$arch.tar.gz"
  rm -f "$out"; tar -C "$pub" -czf "$out" .
  echo "  → $out"
}

pack_win() { # $1=rid  $2=arch-label(x64/arm64)
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local out="$PWD/$OUT/UwView-$VER-win-$arch.zip"
  rm -f "$out"; ( cd "$pub" && ditto -c -k . "$out" )
  echo "  → $OUT/UwView-$VER-win-$arch.zip（署名は Windows で EV 署名）"
}

for rid in "${RIDS[@]}"; do
  case "$rid" in
    osx-arm64)   pack_mac "$rid" arm64 ;;
    osx-x64)     pack_mac "$rid" x64 ;;
    linux-x64)   pack_linux "$rid" x86_64 ;;
    linux-arm64) pack_linux "$rid" aarch64 ;;
    win-x64)     pack_win "$rid" x64 ;;
    win-arm64)   pack_win "$rid" arm64 ;;
    *) echo "skip unknown rid: $rid" ;;
  esac
done

( cd "$OUT" && shasum -a 256 UwView-$VER-* > "SHA256SUMS-$VER.txt" )
echo "SHA256SUMS-$VER.txt:"; cat "$OUT/SHA256SUMS-$VER.txt"
echo "done. → $OUT/"
