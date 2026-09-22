#!/usr/bin/env bash
# UwView (Free / UVF) — 配布ビルド生成（mac / Linux / Windows）。
#
# 生成物: dist/UwView-<ver>-mac-<arch>.dmg(.app内包) / -linux-<arch>.tar.gz / -win-<arch>.zip
#         どれにも CLI の uvf を GUI 実行ファイルの隣に入れる。
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
OUT="${OUT:-dist}"   # 試験用に別フォルダへ出せる（例: OUT=distWideField ./build/publish.sh）

# 試験用に別名で出す（既に入れてある版と並べて置けるように。オーナー指示 2026-09-22）。
#   SUFFIX=WF → 配布物 UwViewWF-<ver>-…・アプリ UwViewWF.app・CLI uvfWF・別の bundle id
# 既定（SUFFIX なし）では従来とまったく同じ名前で出る。
SUFFIX="${SUFFIX:-}"
NAME="${NAME:-UwView$SUFFIX}"
CLI="${CLI:-uvf$SUFFIX}"
# 画面に出る名前（.app・DMG ボリューム・Info.plist）。配布物のファイル名（$NAME）とは分ける:
# ファイル名は短く、画面の名前は分かりやすく（例 APP_NAME="UwView (Wide Field)"）
APP_NAME="${APP_NAME:-$NAME}"
BUNDLE_ID="${BUNDLE_ID:-net.y42u.uwview$(printf '%s' "${SUFFIX:+.$SUFFIX}" | tr 'A-Z' 'a-z')}"
# 単一ファイル実行体名（プロジェクト名由来）。試験用ビルドは本体にも別名を付ける——
# tar / zip を展開したとき、既に入れてある版と同じ名前だと上書きになる（オーナー報告 2026-09-22）
EXE_BUILT="UwView.Desktop"
EXE="UwView.Desktop$SUFFIX"
RIDS=("$@"); [ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-arm64)

mkdir -p "$OUT"
echo "$NAME $VER → ${RIDS[*]}"

publish_one() {
  local rid="$1" pubdir="obj/pub/$rid"
  rm -rf "$pubdir"
  dotnet publish "$APP_PROJ" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o "$pubdir" 1>&2
  # 配布物に不要なもの（依存パッケージ同梱のデバッグ情報 libSkiaSharp.pdb など）は入れない
  find "$pubdir" -name '*.pdb' -delete
  # 別名ビルドは本体の実行ファイルも改名する（単一ファイルなので名前を変えても動く）
  if [ "$EXE" != "$EXE_BUILT" ]; then
    for ext in "" ".exe"; do
      if [ -f "$pubdir/$EXE_BUILT$ext" ]; then mv "$pubdir/$EXE_BUILT$ext" "$pubdir/$EXE$ext"; fi
    done
  fi
  add_cli "$rid" "$pubdir"
  echo "$pubdir"
}

# CLI（uvf）を GUI の隣に置く。uvf は「同じフォルダの UwView 本体を --uvf 付きで起動する」だけの
# 小さな起動アプリ（CLI の中身は本体にある）。本体と同じ場所に入れる（mac は .app/Contents/MacOS、
# win/linux はアーカイブ直下）。トリミング設定は UwView.Cli.csproj 側（約11MB）。
# 別フォルダに発行してから実行ファイルだけを写す（GUI の出力と混ぜない）。
CLI_PROJ="UwView.Cli/UwView.Cli.csproj"
add_cli() { # $1=rid $2=GUI の発行先
  local rid="$1" dest="$2" clipub="obj/pub-cli/$rid"
  rm -rf "$clipub"
  dotnet publish "$CLI_PROJ" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:AssemblyName="$CLI" -o "$clipub" 1>&2
  find "$clipub" -maxdepth 1 -type f \( -name "$CLI" -o -name "$CLI.exe" \) -exec cp {} "$dest/" \;
  [ -f "$dest/$CLI" ] || [ -f "$dest/$CLI.exe" ] || { echo "$CLI の発行に失敗: $rid" >&2; exit 1; }
}

pack_mac() { # $1=rid  $2=arch-label
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local app="$OUT/$APP_NAME.app"
  local macos="$app/Contents/MacOS"
  rm -rf "$app"; mkdir -p "$macos" "$app/Contents/Resources"
  cp -R "$pub/." "$macos/"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$APP_NAME</string>
  <key>CFBundleDisplayName</key><string>$APP_NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
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
      <key>LSItemContentTypes</key><array><string>$BUNDLE_ID.uwvz</string></array>
    </dict>
  </array>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key><string>$BUNDLE_ID.uwvz</string>
      <key>UTTypeDescription</key><string>UwView Compressed Cache</string>
      <key>UTTypeConformsTo</key><array><string>public.data</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>uwvz</string></array></dict>
    </dict>
    <dict>
      <key>UTTypeIdentifier</key><string>$BUNDLE_ID.uwvhl</string>
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
      if [ "$(basename "$f")" = "$CLI" ]; then
        # 起動アプリ uvf も .NET の実行ファイルなので GUI と同じ entitlements が要る（無いと実行時に落ちる）
        codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$f"
      elif file "$f" | grep -q 'Mach-O'; then
        codesign --force --timestamp --options runtime -s "$MAC_SIGN_ID" "$f"
      fi
    done < <(find "$app/Contents" -type f -print0)
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$mainbin"
    codesign --force --timestamp --options runtime --entitlements "$ent" -s "$MAC_SIGN_ID" "$app"
    codesign --verify --deep --strict --verbose=2 "$app"
    rm -f "$ent"

    if [ -n "${AC_PROFILE:-}" ]; then
      echo "  notarytool submit ($arch)…"
      local nz="$OUT/$NAME-$VER-mac-$arch-notarize.zip"
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
  local out="$OUT/$NAME-$VER-mac-$arch.dmg"
  rm -f "$out"

  # hdiutil create -srcfolder は「中身をコピー → 一時ボリュームを unmount → 圧縮」の順に動くが、
  # 公証・staple 済みの .app を置いた直後は OS 側がそれを掴んでいて unmount が
  # 「リソースが使用中です」で失敗することがある（2026-09-17 の x64 ビルドで再現）。
  # そこで 書き込み可能イメージを作る → attach → 中身を置く → detach -force → 圧縮に分ける。
  #
  # 置くときは <b>.app を名指しで</b> ditto する。フォルダごと（中身まとめて）コピーすると
  # macOS の App Management 保護に当たって「Operation not permitted」で弾かれる。
  # マウント先は自前の場所を指定する。/Volumes に同名が残っていると（前回の失敗で detach し損ねた等）
  # 別名で mount され、こちらは残骸のほうへ書いてしまう（2026-09-17 に発生）。
  local vol="$APP_NAME $VER ($arch)"
  local rw mnt size
  rw=$(mktemp -u).rw.dmg
  mnt=$(mktemp -d)
  size=$(( $(du -sm "$app" | cut -f1) + 80 ))             # 余白 80MB
  hdiutil create -size "${size}m" -fs HFS+ -volname "$vol" -type UDIF -ov "$rw"
  local dev; dev=$(hdiutil attach "$rw" -nobrowse -noverify -noautoopen -mountpoint "$mnt" \
                   | grep -Eo '^/dev/disk[0-9]+' | head -1)
  ditto "$app" "$mnt/$APP_NAME.app"
  ln -s /Applications "$mnt/Applications"
  sync
  hdiutil detach "$dev" -force
  rmdir "$mnt" 2>/dev/null || true
  hdiutil convert "$rw" -format UDZO -o "$out"
  rm -f "$rw"

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
  local out="$OUT/$NAME-$VER-linux-$arch.tar.gz"
  # COPYFILE_DISABLE: mac の tar が付ける ._* （拡張属性の退避ファイル）を入れない
  rm -f "$out"; COPYFILE_DISABLE=1 tar -C "$pub" -czf "$out" .
  echo "  → $out"
}

pack_win() { # $1=rid  $2=arch-label(x64/arm64)
  local rid="$1" arch="$2" pub; pub=$(publish_one "$rid")
  local out="$PWD/$OUT/$NAME-$VER-win-$arch.zip"
  # --norsrc --noextattr: mac の ._* （リソースフォーク・拡張属性）を zip に入れない
  rm -f "$out"; ( cd "$pub" && ditto -c -k --norsrc --noextattr . "$out" )
  echo "  → $OUT/$NAME-$VER-win-$arch.zip（署名は Windows で EV 署名）"
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

( cd "$OUT" && shasum -a 256 $NAME-$VER-* > "SHA256SUMS-$VER.txt" )
echo "SHA256SUMS-$VER.txt:"; cat "$OUT/SHA256SUMS-$VER.txt"

# サイト用: 版数なしの名前の複製（dist/latest/。6点揃ったときだけ）。
# 別名（SUFFIX）で焼いたものは試験用なので latest/ は作らない
if [ -z "$SUFFIX" ]; then build/make-latest.sh "$NAME" "$VER" "$OUT"; fi
echo "done. → $OUT/"
