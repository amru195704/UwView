#!/usr/bin/env bash
# UwView (Free / UVF) — 配布ビルド生成（mac / Linux / Windows）。
#
# 生成物: dist/UwView-<ver>-mac-<arch>.dmg(.app内包) / -linux-<arch>.tar.gz / -win-<arch>.zip
#         mac は DMG のみ（UVP に合わせた。zip は作らない）。
#         ＋ dist/SHA256SUMS.uwview。バージョンは UwView/UwView.csproj <Version> と一致。
#
# 注意: UVF の dist/ はリリース資産としてトラック済みのため、dist 全体は消さない
#       （対象の出力ファイルだけを上書きする）。
#
# 署名・公証は環境変数がある時のみ（mac: MAC_SIGN_ID / AC_PROFILE、win: EV は publish-win.ps1）。
#   MAC_SIGN_ID=9737970DAE0495030DFF12A5BB6B442C62B044F0 \
#   AC_PROFILE=uwviewpro-notary build/publish.sh osx-arm64 osx-x64
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
  <!-- Finder の「このアプリケーションで開く」、アイコンへのドラッグ&ドロップ、
       open -a UwView file.log を受けるための宣言。これが無いと Finder が候補に出さない。
       テキストは LSHandlerRank=Alternate（既定のアプリを奪わない）。
       .uwvz / .uwvhl は自前の形式なので Owner として宣言する。 -->
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Text File</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>public.plain-text</string>
        <string>public.utf8-plain-text</string>
        <string>public.utf16-plain-text</string>
        <string>public.log</string>
        <string>public.comma-separated-values-text</string>
        <string>public.tab-separated-values-text</string>
        <string>public.xml</string>
        <string>public.json</string>
      </array>
    </dict>
    <dict>
      <!-- 拡張子の無いログ・未知の形式も受ける（ビューアなので開けて困らない） -->
      <key>CFBundleTypeName</key><string>Any File</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key><array><string>public.data</string></array>
    </dict>
    <dict>
      <key>CFBundleTypeName</key><string>UwView Compressed Cache</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>LSHandlerRank</key><string>Owner</string>
      <key>LSItemContentTypes</key><array><string>net.y42u.uwview.uwvz</string></array>
    </dict>
  </array>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key><string>net.y42u.uwview.uwvz</string>
      <key>UTTypeDescription</key><string>UwView Compressed Cache</string>
      <key>UTTypeConformsTo</key><array><string>public.data</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>uwvz</string></array></dict>
    </dict>
    <dict>
      <key>UTTypeIdentifier</key><string>net.y42u.uwview.uwvhl</string>
      <key>UTTypeDescription</key><string>UwView Highlighter Set</string>
      <key>UTTypeConformsTo</key><array><string>public.data</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>uwvhl</string></array></dict>
    </dict>
  </array>
</dict></plist>
PLIST
  cp "UwView.Desktop/macos/UwView.icns" "$app/Contents/Resources/UwView.icns"
  chmod +x "$macos/$EXE"

  if [ -n "${MAC_SIGN_ID:-}" ]; then
    echo "  codesign ($arch)…"
    # .NET には entitlements が要る（JIT・実行メモリ・混在署名ライブラリの読込）。
    # これを付けずに hardened runtime で署名すると、署名は通るのに起動時に落ちる。
    local ent; ent="$(mktemp -t uwview-entitlements).plist"
    cat > "$ent" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
PLIST
    # --deep には頼らず内側→外側の順に署名する（取りこぼしが起きるため）
    local mainbin="$macos/$EXE"
    while IFS= read -r -d '' f; do
      [ "$f" = "$mainbin" ] && continue
      if file "$f" | grep -q 'Mach-O'; then
        codesign --force --timestamp --options runtime -s "$MAC_SIGN_ID" "$f"
      fi
    done < <(find "$app/Contents" -type f -print0)
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$mainbin"
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$app"
    codesign --verify --deep --strict --verbose=2 "$app"
    rm -f "$ent"

    if [ -n "${AC_PROFILE:-}" ]; then
      echo "  notarytool submit ($arch)…"
      local nz="$OUT/UwView-$VER-mac-$arch-notarize.zip"
      ditto -c -k --keepParent "$app" "$nz"
      xcrun notarytool submit "$nz" --keychain-profile "$AC_PROFILE" --wait
      xcrun stapler staple "$app"; xcrun stapler validate "$app"
      spctl --assess --type execute --verbose=4 "$app" || true
      rm -f "$nz"
    fi
  else
    echo "  ⚠ MAC_SIGN_ID 未設定 → 未署名（配布前に署名・公証が必要）"
  fi

  # 配布物は DMG（UVP と同じ形。Applications へのリンクを置いてドラッグで入れられるようにする）
  local stage; stage=$(mktemp -d)
  cp -R "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  local out="$OUT/UwView-$VER-mac-$arch.dmg"
  rm -f "$out"
  hdiutil create -volname "UwView $VER ($arch)" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$out"
  rm -rf "$stage"

  # DMG 自身も署名・公証する（中の .app だけ公証しても、DMG が未署名だと警告が出る）
  if [ -n "${MAC_SIGN_ID:-}" ]; then
    codesign --force --timestamp --sign "$MAC_SIGN_ID" "$out"
    if [ -n "${AC_PROFILE:-}" ]; then
      xcrun notarytool submit "$out" --keychain-profile "$AC_PROFILE" --wait
      xcrun stapler staple "$out"; xcrun stapler validate "$out"
    fi
  fi

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
