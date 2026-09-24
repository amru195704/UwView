#!/usr/bin/env bash
# =============================================================================
# uv_format.sh — 圧縮形式ごとの速さと正しさ（v1.7.1）
#
#   指示書_開発部_圧縮形式サポート_2026-09-24 §0-4「形式を入れるたびに、その形式ぶんだけ測る」。
#   置き場所は UwTest／元データは UwTest/osm17 の平文（3サイズ）。
#
#   形式ごとに、同じ中身を CLI（ripgrep）・uvfWF・uvpWF で探して比べる:
#     CLI  rg -z -n -F 語 file      ripgrep は外部コマンド（gzip / bzip2 / xz / zstd / lz4 / brotli）へパイプして展開する
#     uvf  uvfWF file 語            自分で展開しながら探す（外部コマンドは使わない）
#     uvp  uvpWF file 語            1回目は .uwvz を作りながら（cold）、2回目は .uwvz を使う（hot）
#   正しさは「平文を rg で探した結果」と突き合わせる（rg -z が展開できない環境でも比べられるように）。
#
#   ripgrep は展開コマンドが PATH に無いと**黙って飛ばす**（0 件で終わる）。
#   その場合は「rg は探せなかった」と書き、倍率は出さない（こちらの勝ちにも数えない）。
#
#   圧縮ファイルは osm17_fmt/ に作って残す（2回目からは作らない。元の平文は消さない）。
#   作り方は各コマンドの既定: gzip -6 / bzip2 -9 / xz -6 / lzma（xz --format=lzma -6）/ zstd -3 / lz4 -1。
#   brotli だけは既定（-q 11）が極端に遅い（1GB で十数分）ので -q 6 にする（展開の速さは段階でほぼ変わらない）。
#   作る道具が無い環境（Windows など）は、Mac で作った osm17_fmt/ を写せば同じものを測れる。
#
# 使い方（uvfWF / uvpWF と同じ場所で。環境はそのまま使う）:
#   ./uv_format.sh                          7形式 × 3サイズ（1m / 3m / 10m）
#   ./uv_format.sh --formats bz2,xz         形式を絞る（gz,bz2,xz,lzma,zst,lz4,br）
#   ./uv_format.sh --sizes 1m,3m            サイズを絞る
#   ./uv_format.sh --no-uvp                 uvf だけ
#   -p 検索語（既定 東京）  -d 元データ（既定 osm17）  -f 圧縮の置き場所（既定 osm17_fmt）  -o 結果の置き場所
#   PURGE=0   キャッシュ破棄をしない（cold は参考値）
#   SETTLE=5  破棄後の待機秒（既定 10）
#   KEEPTMP=1 作業ファイルを残す
# =============================================================================
set -u

DATA="osm17"; FMT="osm17_fmt"; OUT=""; UVF="uvfWF"; UVP="uvpWF"; PAT="東京"; WITH_UVP=1
FORMATS="gz,bz2,xz,lzma,zst,lz4,br"; SIZES="1m,3m,10m"
PURGE="${PURGE:-1}"; SETTLE="${SETTLE:-10}"

while [ $# -gt 0 ]; do
  case "$1" in
    -d) DATA="$2"; shift 2;;
    -f) FMT="$2"; shift 2;;
    -o) OUT="$2"; shift 2;;
    -p) PAT="$2"; shift 2;;
    --uvf) UVF="$2"; shift 2;;
    --uvp) UVP="$2"; shift 2;;
    --formats) FORMATS="$2"; shift 2;;
    --sizes) SIZES="$2"; shift 2;;
    --no-uvp) WITH_UVP=0; shift;;
    -h|--help) sed -n '2,34p' "$0"; exit 0;;
    *) echo "知らない引数: $1" >&2; exit 2;;
  esac
done

case "$(uname -s)" in
  Darwin) OSKIND=mac ;;
  Linux)  OSKIND=linux ;;
  MINGW*|MSYS*|CYGWIN*) OSKIND=win ;;
  *) OSKIND=other ;;
esac
[ -n "$OUT" ] || OUT="${OSKIND}_result"
[ -d "$DATA" ] || { echo "データのフォルダがありません: $DATA" >&2; exit 2; }
command -v rg >/dev/null 2>&1 || { echo "rg が見つかりません" >&2; exit 2; }

TS="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT" "$FMT"
OUT="$(cd "$OUT" && pwd)/${TS}-format"; mkdir -p "$OUT"
WORK="$OUT/work"; mkdir -p "$WORK"
LOG="$OUT/summary.md"
TOT="$OUT/format.tsv"; : > "$TOT"

say()  { printf '%s\n' "$*"; }
now_s() {
  if [ -n "${EPOCHREALTIME:-}" ]; then printf '%s' "${EPOCHREALTIME/,/.}"
  else python3 -c 'import time;print(f"{time.time():.3f}")'; fi
}
el()   { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.2f", b-a}'; }
sum2() { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.2f", a+b}'; }
_r()   { awk -v a="$1" -v b="$2" 'BEGIN{
           if (a<=0||b<=0) printf "-";
           else if (a>=b)  printf "%.2f 🟢", a/b;
           else            printf "1/%.2f 🍊", b/a }'; }
mib()  { awk -v b="${1:-0}" 'BEGIN{printf "%.0f", b/1048576}'; }
bytes(){ stat -c %s "$1" 2>/dev/null || stat -f %z "$1" 2>/dev/null; }

# ---- キャッシュ破棄 ----
RAMMAP_BIN=""
if [ "$OSKIND" = win ]; then
  for n in RAMMap.exe RAMMap64.exe RAMMap64a.exe; do
    for d in . "$(dirname "$0")"; do
      [ -x "$d/$n" ] || continue
      "$d/$n" -accepteula -Et >/dev/null 2>&1 && { RAMMAP_BIN="$d/$n"; break 2; }
    done
  done
fi
purge_warned=0; NPRG=0
drop_caches() {
  case "$OSKIND" in
    mac)   sync; sudo purge ;;
    linux) sync; sudo sh -c 'echo 3 > /proc/sys/vm/drop_caches' ;;
    win)   [ -n "$RAMMAP_BIN" ] || return 1; "$RAMMAP_BIN" -accepteula -Et >/dev/null 2>&1 ;;
    *) return 1 ;;
  esac
}
cold_prep() {
  [ "$PURGE" = 1 ] || { sleep 1; return 0; }
  if drop_caches; then NPRG=$((NPRG+1)); sleep "$SETTLE"
  else
    [ "$purge_warned" = 0 ] && { echo "    ⚠ キャッシュ破棄ができません（cold は参考値）" | tee -a "$LOG"; purge_warned=1; }
    sleep "$SETTLE"
  fi
}

# ---- 出力の正規化（行番号<TAB>本文 に揃える。rg は「行番号:本文」）----
norm_rg() { awk '{ i=index($0,":"); if(i==0){print; next}
                   printf "%s\t%s\n", substr($0,1,i-1), substr($0,i+1) }'; }

run_to() { local o="$1"; shift; local t0 t1
           t0="$(now_s)"; "$@" > "$o" 2> "$o.err"; echo $? > "$o.rc"; t1="$(now_s)"; el "$t0" "$t1"; }

# ---- 形式ごとの作り方（道具が無ければ作れない）----
maker_of() {
  case "$1" in
    gz)   command -v gzip  >/dev/null 2>&1 && echo "gzip -6 -c" ;;
    bz2)  command -v bzip2 >/dev/null 2>&1 && echo "bzip2 -9 -c" ;;
    xz)   command -v xz    >/dev/null 2>&1 && echo "xz -6 -T1 -c" ;;
    lzma) command -v xz    >/dev/null 2>&1 && echo "xz --format=lzma -6 -c" ;;
    zst)  command -v zstd  >/dev/null 2>&1 && echo "zstd -3 -q -c" ;;
    lz4)  command -v lz4   >/dev/null 2>&1 && echo "lz4 -1 -q -c" ;;
    br)   command -v brotli >/dev/null 2>&1 && echo "brotli -q 6 -c" ;;
  esac
}
uwvz_of() { printf '%s.uwvz' "$1"; }

say "$UVF --version: $("$UVF" --version 2>&1 | head -1)"
[ "$WITH_UVP" = 1 ] && say "$UVP --version: $("$UVP" --version 2>&1 | head -1)"
say "ripgrep: $(rg --version | head -1)"
say ""

{
  echo "# 圧縮形式ごとの速さと正しさ（v1.7.1）  $TS"
  echo
  echo "| 項目 | 値 |"
  echo "|---|---|"
  echo "| OS | $(uname -sr) $(uname -m)（判定: ${OSKIND}） |"
  echo "| ${UVF} | $(command -v "$UVF") $("$UVF" --version 2>/dev/null | head -1) |"
  [ "$WITH_UVP" = 1 ] && echo "| ${UVP} | $(command -v "$UVP") $("$UVP" --version 2>/dev/null | head -1) |"
  echo "| ripgrep | $(command -v rg) ($(rg --version | head -1)) |"
  echo "| 展開コマンド（rg が使う） | gzip:$(command -v gzip >/dev/null && echo あり || echo 無し) bzip2:$(command -v bzip2 >/dev/null && echo あり || echo 無し) xz:$(command -v xz >/dev/null && echo あり || echo 無し) zstd:$(command -v zstd >/dev/null && echo あり || echo 無し) lz4:$(command -v lz4 >/dev/null && echo あり || echo 無し) brotli:$(command -v brotli >/dev/null && echo あり || echo 無し) |"
  echo "| 元データ | ${DATA}（サイズ: ${SIZES}） |"
  echo "| 圧縮の置き場所 | ${FMT}（無ければ作って残す） |"
  echo "| 検索語 | $PAT |"
  echo "| 測り方 | sync → キャッシュ破棄 → ${SETTLE}秒待機 → 1回目(cold) → 続けて 2回目(hot) |"
  echo "| CLI | \`rg -z -n -F 語 file\`（rg が外部コマンドで展開） |"
  echo "| ${UVP} の cold | .uwvz の作成を含む（測る前に消す）。hot は .uwvz を使う |"
  echo "| 正しさ | 平文を \`rg -n -F\` で探した結果と突き合わせる |"
  echo "| 倍率 | CLI を 1 としたとき（1超＝速い🟢） |"
  echo
} > "$LOG"

OK=0; NG=0; RGSKIP=0

# =============================================================================
# 1組（形式 × サイズ）を測る
# =============================================================================
one() {  # $1=形式 $2=サイズ名
  local fmt="$1" size="$2" plain="$DATA/japan-dv-$2.osm" packed key ref hits
  local cc ch fc fh pc ph rghits mark fmark pmark maker
  key="${fmt}_${size}"
  [ -f "$plain" ] || { say "  ・$plain がありません（飛ばします）"; return; }
  packed="$FMT/japan-dv-${size}.osm.${fmt}"

  if [ ! -f "$packed" ]; then
    maker="$(maker_of "$fmt")"
    if [ -z "$maker" ]; then
      echo "  ・${packed} が無く、作る道具もありません（Mac で作った ${FMT}/ を写してください）" | tee -a "$LOG"
      return
    fi
    say "  ・${packed} を作ります（${maker}）"
    local t0 t1; t0="$(now_s)"
    if ! $maker "$plain" > "${packed}.making-$$" 2>/dev/null; then
      rm -f "${packed}.making-$$"; say "      → 失敗しました"; return; fi
    mv -f "${packed}.making-$$" "$packed"; t1="$(now_s)"
    say "      → $(mib "$(bytes "$packed")") MiB ／ $(el "$t0" "$t1") 秒"
  fi

  { echo ""; echo "### ${fmt} — japan-dv-${size}（平文 $(mib "$(bytes "$plain")") MiB → ${fmt} $(mib "$(bytes "$packed")") MiB）"; echo ""; } | tee -a "$LOG"

  # 正解: 平文を rg で探した結果
  ref="$WORK/${key}_ref.txt"
  rg -n -F -- "$PAT" "$plain" 2>/dev/null | norm_rg > "$ref"
  hits=$(wc -l < "$ref" | tr -d ' ')

  # ---- CLI（rg -z）----
  echo "    \$ rg -z -n -F '$PAT' $packed" | tee -a "$LOG"
  cold_prep "CLI cold"
  cc="$(run_to "$WORK/${key}_cli.txt" rg -z -n -F -- "$PAT" "$packed")"
  ch="$(run_to "$WORK/${key}_cli2.txt" rg -z -n -F -- "$PAT" "$packed")"
  norm_rg < "$WORK/${key}_cli.txt" > "$WORK/${key}_cli.norm"
  rghits=$(wc -l < "$WORK/${key}_cli.norm" | tr -d ' ')
  if [ "$rghits" = 0 ] && [ "$hits" != 0 ]; then
    mark="⛔"; RGSKIP=$((RGSKIP+1))
    printf '    %s CLI  cold %7s / hot %7s   **0 件＝rg は展開できずに黙って飛ばした**（展開コマンドが無い）\n' \
      "$mark" "$cc" "$ch" | tee -a "$LOG"
  else
    cmp -s "$ref" "$WORK/${key}_cli.norm" && mark="✅" || mark="❌"
    printf '    %s CLI  cold %7s / hot %7s / 2回計 %7s   %s 件\n' \
      "$mark" "$cc" "$ch" "$(sum2 "$cc" "$ch")" "$rghits" | tee -a "$LOG"
  fi
  local climark="$mark"

  # ---- uvf ----
  echo "    \$ $UVF $packed '$PAT'" | tee -a "$LOG"
  cold_prep "uvf cold"
  fc="$(run_to "$WORK/${key}_uvf.txt" "$UVF" "$packed" "$PAT")"
  fh="$(run_to "$WORK/${key}_uvf2.txt" "$UVF" "$packed" "$PAT")"
  if cmp -s "$ref" "$WORK/${key}_uvf.txt"; then fmark="✅"; OK=$((OK+1)); else fmark="❌"; NG=$((NG+1)); fi
  printf '    %s %-6s cold %7s / hot %7s / 2回計 %7s   %s 件  exit=%s\n' \
    "$fmark" "$UVF" "$fc" "$fh" "$(sum2 "$fc" "$fh")" \
    "$(wc -l < "$WORK/${key}_uvf.txt" | tr -d ' ')" "$(cat "$WORK/${key}_uvf.txt.rc")" | tee -a "$LOG"
  [ "$fmark" = "❌" ] && echo "      ↳ $(head -1 "$WORK/${key}_uvf.txt.err")" | tee -a "$LOG"

  # ---- uvp ----
  pc=0; ph=0; pmark="－"
  if [ "$WITH_UVP" = 1 ]; then
    echo "    \$ $UVP $packed '$PAT'" | tee -a "$LOG"
    rm -f "$(uwvz_of "$packed")"                  # cold に「.uwvz を作る」を含める
    cold_prep "uvp cold"
    pc="$(run_to "$WORK/${key}_uvp.txt" "$UVP" "$packed" "$PAT")"
    ph="$(run_to "$WORK/${key}_uvp2.txt" "$UVP" "$packed" "$PAT")"
    if cmp -s "$ref" "$WORK/${key}_uvp.txt" && cmp -s "$ref" "$WORK/${key}_uvp2.txt"; then
      pmark="✅"; OK=$((OK+1)); else pmark="❌"; NG=$((NG+1)); fi
    printf '    %s %-6s cold %7s（.uwvz 作成込み）/ hot %7s   %s 件  .uwvz %s MiB\n' \
      "$pmark" "$UVP" "$pc" "$ph" "$(wc -l < "$WORK/${key}_uvp.txt" | tr -d ' ')" \
      "$(mib "$(bytes "$(uwvz_of "$packed")" 2>/dev/null)")" | tee -a "$LOG"
    [ "$pmark" = "❌" ] && echo "      ↳ $(head -1 "$WORK/${key}_uvp.txt.err")" | tee -a "$LOG"
    rm -f "$(uwvz_of "$packed")"                  # 残さない（元データの隣を汚さない）
  fi

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$fmt" "$size" "$hits" "$cc" "$ch" "$fc" "$fh" "$pc" "$ph" "$climark" "$fmark" "$pmark" >> "$TOT"
}

say "== 測定 =="
IFS=',' read -r -a FMTS <<EOF
$FORMATS
EOF
IFS=',' read -r -a SZS <<EOF
$SIZES
EOF
for fmt in "${FMTS[@]}"; do
  { echo ""; echo "## ${fmt}"; } | tee -a "$LOG"
  for size in "${SZS[@]}"; do one "$fmt" "$size"; done
done

# =============================================================================
# まとめ
# =============================================================================
{
  echo ""
  echo "## まとめ（2回計＝cold＋hot。uvp の hot は .uwvz を使った検索）"
  echo ""
  echo "| 形式 | サイズ | 件数 | CLI 2回計 | ${UVF} 2回計 | CLI/${UVF} | ${UVP} cold | ${UVP} hot | CLI hot/${UVP} hot |"
  echo "|---|---|---:|---:|---:|---|---:|---:|---|"
} | tee -a "$LOG"
while IFS=$'\t' read -r fmt size hits cc ch fc fh pc ph cm fm pm; do
  [ -n "$fmt" ] || continue
  ct="$(sum2 "$cc" "$ch")"; ft="$(sum2 "$fc" "$fh")"
  if [ "$cm" = "⛔" ]; then
    fr="— ⛔ rg は探せず"; ct="$ct ⛔"; hr="— ⛔"
  else
    if [ "$fm" = "✅" ]; then fr="$(_r "$ct" "$ft")"; else fr="— ❌"; fi
    if [ "$pm" = "✅" ]; then hr="$(_r "$ch" "$ph")"; else hr="—"; fi
  fi
  [ "$fm" = "✅" ] || ft="$ft ❌"
  printf '| %s | %s | %s | %s | %s | %s | %s | %s | %s |\n' "$fmt" "$size" "$hits" "$ct" "$ft" "$fr" "$pc" "$ph" "$hr"
done < "$TOT" | tee -a "$LOG"

{
  echo ""
  echo "**見どころ**"
  echo "- bz2・xz・lzma は、mac・Linux では OS 標準のライブラリ（libbz2・liblzma＝bzip2 / xz コマンドの中身）で展開する。"
  echo "  展開と検索を同じプロセスで重ねるので、rg -z（別プロセスへパイプ）より少し速い（Mac・1GB で 1.06〜1.28 倍）。"
  echo "  Windows は OS にライブラリが無いので .NET 側の展開になり、bz2 で 1/2.4・xz で 1/2.8 程度に落ちる。"
  echo "- lz4・brotli は展開そのものは自前のほうが速い（50MB 実測）。ただし ${UVF} は起動に約 0.2 秒かかるので、"
  echo "  小さいファイルでは rg に届かない。大きいほど差は縮む（Mac・1GB で lz4 1/1.3、brotli 同着）。"
  echo "- ${UVP} は1回目に .uwvz を作れば、2回目からは**展開しない**。形式が遅いほど、hot の差が開く。"
  echo "- ⛔ は ripgrep が展開コマンドを見つけられず、**何も言わずに 0 件で終わった**もの（rg の仕様）。"
  echo ""
  echo "**突き合わせ: 一致 $OK 件 ／ 不一致 $NG 件 ／ rg が探せなかった組 $RGSKIP ／ キャッシュ破棄 $NPRG 回**"
  echo ""
  echo "結果: $LOG"
} | tee -a "$LOG"

if [ "${KEEPTMP:-0}" = 1 ]; then say "（作業ファイルを $WORK に残しました）"
else rm -rf "$WORK"; say "（作業ファイルは削除しました。残すには KEEPTMP=1）"; fi
[ "$NG" = 0 ] || exit 1
exit 0
