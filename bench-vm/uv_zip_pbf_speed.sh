#!/usr/bin/env bash
# =============================================================================
# uv_zip_pbf_speed.sh — Wide Field #5(zip) / #6(pbf) の速さ計測
#
#   連載計画 `UVP_WideField_連載計画_2026-09-27.md` の #5・#6 が「要計測」で止まって
#   いるので、その2回ぶんの数字だけを取るための計測スクリプト。
#   uv_multi.sh と同じ測り方（sync → キャッシュ破棄 → SETTLE 秒待機 → cold → 続けて hot）。
#
#   ■ zip（#5）… zip 入力は uvp だけの機能（uvf は対象外・実装指示書 §2.5）
#       Z0 rg そのまま        rg -n -F PAT ZIP        ← 書庫は探さないことの記録
#       Z1 全エントリ         unzip -p ZIP | rg       対 uvp ZIP PAT
#       Z2 1エントリに絞る    unzip -p ZIP ENTRY | rg 対 uvp 'ZIP!ENTRY' PAT
#       Z3 展開してから探す   unzip -o -d DIR + rg    （--extract-case のときだけ）
#
#   ■ pbf（#6）… pbf も uvp だけ（uvf は名指しで断る。その断り文も記録する）
#       P0 uvf の断り         uvf PBF PAT             ← 断り文と終了コードの記録
#       P1 パイプ             osmium cat PBF -f osm | rg
#                             対 uvp PBF PAT を3回（①.uwvz 作成込み ②purge 後の再利用 ③hot）
#
#   zip は「平文5本すべて」から作る（10.1修正）。元の .osm は消さない。
#   pbf の相手は「パイプだけ」（同・51GB の XML をディスクに出さない）。
#
# 使い方（uvf / uvp と同じ場所で。UwTest 直下に置き、osm17 を見に行く）:
#   ./uv_zip_pbf_speed.sh                 zip と pbf の両方
#   ./uv_zip_pbf_speed.sh --only zip      zip だけ
#   ./uv_zip_pbf_speed.sh --only pbf      pbf だけ
#   ./uv_zip_pbf_speed.sh --dry-run       何をするか・どれだけ要るかだけ出して終わる
#
#   -d osm17            データのフォルダ（既定 osm17）
#   -o mac_result       結果の置き場（既定 <OS>_result）
#   -p 東京             検索語（既定 東京）
#   --uvf uvf / --uvp uvp     コマンド名（既定 uvf / uvp）
#   --zip PATH          既にある zip を使う（作らない）
#   --pbf PATH          pbf の場所（既定 DATA の中の *.pbf を1本）
#   --entry NAME        Z2 で絞るエントリ名（既定 japan-dv-10m.osm）
#   --extract-case      Z3（展開してから探す）も測る。**元と同じだけ空きが要る**
#   PURGE=0             キャッシュ破棄をしない（cold は参考値）
#   SETTLE=5            破棄後の待機秒（既定 10）
#   ZIPLEVEL=6          zip を作るときの圧縮レベル（既定 6）
#   KEEPZIP=1           作った zip を残す（既定は残す。0 で終わりに消す）
#   KEEPTMP=1           作業ファイルを残す
#
#   ※ キャッシュ破棄は mac で sudo purge を使う。先に `sudo -v` を済ませておくと
#      途中でパスワードを聞かれて測定が伸びない。
# =============================================================================
set -u

DATA="osm17"; OUT=""; UVF="uvf"; UVP="uvp"; PAT="東京"
ONLY="all"; ZIPFILE=""; PBF=""; ENTRY="japan-dv-10m.osm"
EXTRACT_CASE=0; DRYRUN=0
PURGE="${PURGE:-1}"; SETTLE="${SETTLE:-10}"; ZIPLEVEL="${ZIPLEVEL:-6}"
KEEPZIP="${KEEPZIP:-1}"

while [ $# -gt 0 ]; do
  case "$1" in
    -d) DATA="$2"; shift 2;;
    -o) OUT="$2"; shift 2;;
    -p) PAT="$2"; shift 2;;
    --uvf) UVF="$2"; shift 2;;
    --uvp) UVP="$2"; shift 2;;
    --zip) ZIPFILE="$2"; shift 2;;
    --pbf) PBF="$2"; shift 2;;
    --entry) ENTRY="$2"; shift 2;;
    --only) ONLY="$2"; shift 2;;
    --extract-case) EXTRACT_CASE=1; shift;;
    --dry-run) DRYRUN=1; shift;;
    -h|--help) sed -n '2,52p' "$0"; exit 0;;
    *) echo "知らない引数: $1" >&2; exit 2;;
  esac
done
case "$ONLY" in all|zip|pbf) ;; *) echo "--only は zip / pbf / all です" >&2; exit 2;; esac

case "$(uname -s)" in
  Darwin) OSKIND=mac ;;
  Linux)  OSKIND=linux ;;
  MINGW*|MSYS*|CYGWIN*) OSKIND=win ;;
  *) OSKIND=other ;;
esac
[ -n "$OUT" ] || OUT="${OSKIND}_result"
[ -d "$DATA" ] || { echo "データのフォルダがありません: $DATA" >&2; exit 2; }

RG="$(command -v rg 2>/dev/null || true)"
[ -n "$RG" ] || { echo "rg が見つかりません" >&2; exit 2; }

# ---- 小道具（uv_multi.sh と同じもの）----
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
gib()  { awk -v b="${1:-0}" 'BEGIN{printf "%.2f", b/1073741824}'; }
bytes(){ stat -c %s "$1" 2>/dev/null || stat -f %z "$1" 2>/dev/null || echo 0; }
list_of() { LC_ALL=C ls -1d $1 2>/dev/null | LC_ALL=C sort; }
freeg() { df -k "${1:-.}" 2>/dev/null | awk 'NR==2{printf "%.1f", $4/1048576}'; }

# 本文だけを取り出して並べ替える（行番号とファイル名は落とす）。
# unzip -p の連結では行番号が通しになるので、照合は「本文の集合」で行う。
# $1 = 落とす前置きフィールドの数（1 = "行番号:本文" ／ 2 = "パス:行番号:本文"）
body_rg() { awk -v n="${1:-1}" '{ s=$0
             for(k=0;k<n;k++){ i=index(s,":"); if(i==0) break; s=substr(s,i+1) }
             print s }' | LC_ALL=C sort; }
# uvp の出力は入力が1本なら "行番号:本文"、ファイル名が付くと "名前:行番号:本文" になる。
# 先頭行を見て、どちらかを決める（本文は空白か < で始まるので、数字＋区切りなら行番号始まり）。
uv_fields() {  # $1=出力ファイル → 1 か 2
  if [ ! -s "$1" ]; then echo 1; return; fi
  if head -1 "$1" | awk '{ exit !($0 ~ /^[0-9]+[:\t]/) }'; then echo 1; else echo 2; fi
}
body_uv() { awk -v n="${1:-2}" '{ s=$0
             for(k=0;k<n;k++){ i=match(s,/[:\t]/); if(i==0) break; s=substr(s,i+RLENGTH) }
             print s }' | LC_ALL=C sort; }

PASS=0; FAIL=0; NO=0; NPRG=0
result() {  # $1=PASS/FAIL/SKIP $2=題 $3=備考
  case "$1" in
    PASS) PASS=$((PASS+1)); say "  ✅ $2";;
    FAIL) FAIL=$((FAIL+1)); say "  ❌ $2  … $3";;
    SKIP) NO=$((NO+1));     say "  －  $2  … $3";;
  esac
  [ -n "${LOG:-}" ] && echo "| $((PASS+FAIL+NO)) | $2 | $1 | ${3//|/／} |" >> "$LOG"
}

RAMMAP_BIN=""
if [ "$OSKIND" = win ]; then
  for n in RAMMap.exe RAMMap64.exe RAMMap64a.exe; do
    for d in . "$(dirname "$0")"; do
      [ -x "$d/$n" ] || continue
      "$d/$n" -accepteula -Et >/dev/null 2>&1 && { RAMMAP_BIN="$d/$n"; break 2; }
    done
  done
fi
purge_warned=0
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
    printf '    · キャッシュ破棄＋%s秒待機（%s）\n' "$SETTLE" "$1" | tee -a "$LOG"
  else
    [ "$purge_warned" = 0 ] && { echo "    ⚠ キャッシュ破棄ができません（cold は参考値）" | tee -a "$LOG"; purge_warned=1; }
    sleep "$SETTLE"; printf '    · 待機のみ %s秒（%s）\n' "$SETTLE" "$1" | tee -a "$LOG"
  fi
}

run_to() { local o="$1"; shift; local t0 t1
           t0="$(now_s)"; "$@" > "$o" 2> "${o}.err"; echo $? > "${o}.rc"; t1="$(now_s)"; el "$t0" "$t1"; }
run_sh() { local o="$1" fn="$2" t0 t1
           t0="$(now_s)"; "$fn" > "$o" 2> "${o}.err"; echo $? > "${o}.rc"; t1="$(now_s)"; el "$t0" "$t1"; }

# =============================================================================
# 下ごしらえ
# =============================================================================
OSMS="$(list_of "${DATA}/*.osm")"
NOSM=$(printf '%s\n' "$OSMS" | grep -c . || true)
SRCBYTES=0
if [ "$NOSM" -gt 0 ]; then
  while IFS= read -r f; do [ -n "$f" ] && SRCBYTES=$((SRCBYTES + $(bytes "$f"))); done <<EOF
$OSMS
EOF
fi
[ -n "$ZIPFILE" ] || ZIPFILE="${DATA}/uvzip-plain${NOSM}.zip"

if [ -z "$PBF" ]; then
  PBF="$(list_of "${DATA}/*.pbf" | head -1)"
  [ -n "$PBF" ] || PBF="$(list_of "${DATA}/*.osm.pbf" | head -1)"
fi
OSMIUM="$(command -v osmium 2>/dev/null || true)"
UNZIP="$(command -v unzip 2>/dev/null || true)"

if [ "$DRYRUN" = 1 ]; then
  say "== dry-run: これから何をするか =="
  say ""
  say "データ          : ${DATA}（平文 ${NOSM}本 ・ 合計 $(gib "$SRCBYTES") GiB）"
  printf '%s\n' "$OSMS" | while IFS= read -r f; do [ -n "$f" ] && printf '  %-34s %s GiB\n' "$f" "$(gib "$(bytes "$f")")"; done
  say ""
  if [ -f "$ZIPFILE" ]; then
    say "zip             : ${ZIPFILE} は既にあります（$(gib "$(bytes "$ZIPFILE")") GiB）。作り直しません"
  else
    say "zip             : ${ZIPFILE} を作ります（deflate -${ZIPLEVEL}・元の .osm は残します）"
    say "                  目安: 作成に数分／できあがりは元の 1/8〜1/9 ≒ $(awk -v b="$SRCBYTES" 'BEGIN{printf "%.2f", b/8.9/1073741824}') GiB"
  fi
  say "Z2 のエントリ   : ${ENTRY}"
  say "Z3 展開して探す : $([ "$EXTRACT_CASE" = 1 ] && echo "測る（空きが $(gib "$SRCBYTES") GiB 要る）" || echo "測らない（--extract-case で有効）")"
  say "unzip           : ${UNZIP:-** 見つかりません **}"
  say ""
  say "pbf             : ${PBF:-** 見つかりません（--pbf で指定）**}"
  [ -n "$PBF" ] && [ -f "$PBF" ] && say "                  $(gib "$(bytes "$PBF")") GiB"
  if [ -n "$OSMIUM" ]; then
    say "osmium          : ${OSMIUM}（$("$OSMIUM" --version 2>&1 | head -1)）"
  else
    say "osmium          : ** 見つかりません **  →  brew install osmium-tool で入ります"
  fi
  say "                  osmium cat … -f osm は 51GB 相当の XML をパイプに流します。"
  say "                  1回あたり数分かかる見込み。ディスクには出しません（10.1修正）"
  say ""
  say "空き容量        : $(freeg "$DATA") GiB（${DATA} のあるボリューム）"
  say "キャッシュ破棄  : PURGE=${PURGE} ／ 待機 ${SETTLE} 秒"
  say "測る回数        : zip = cold+hot を 3〜4 ケース ／ pbf = 相手 cold+hot ＋ uvp 3回"
  say ""
  say '※ mac は sudo purge を使います。先に `sudo -v` を済ませておくと途中で止まりません。'
  exit 0
fi

TS="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)/${TS}-zippbf"; mkdir -p "$OUT"
WORK="$OUT/work"; mkdir -p "$WORK"
LOG="$OUT/summary.md"
TOT="$OUT/speed.tsv"; : > "$TOT"

{
  echo "# zip / pbf 速さ計測（Wide Field #5・#6）  $TS"
  echo
  echo "| 項目 | 値 |"
  echo "|---|---|"
  echo "| OS | $(uname -sr) $(uname -m)（判定: ${OSKIND}） |"
  echo "| ${UVF} | $(command -v "$UVF" 2>/dev/null) $("$UVF" --version 2>/dev/null | head -1) |"
  echo "| ${UVP} | $(command -v "$UVP" 2>/dev/null) $("$UVP" --version 2>/dev/null | head -1) |"
  echo "| ripgrep | $(command -v rg) ($("$RG" --version | head -1)) |"
  echo "| unzip | ${UNZIP:-（無し）} $([ -n "$UNZIP" ] && "$UNZIP" -v 2>&1 | head -1) |"
  echo "| osmium | ${OSMIUM:-（無し。brew install osmium-tool）} $([ -n "$OSMIUM" ] && "$OSMIUM" --version 2>&1 | head -1) |"
  echo "| データ | ${DATA} … 平文 ${NOSM}本・$(gib "$SRCBYTES") GiB |"
  echo "| 検索語 | ${PAT} |"
  echo "| 実行する部 | ${ONLY} |"
  echo "| 測り方 | sync → キャッシュ破棄 → **${SETTLE}秒待機** → 1回目(cold) → 続けて 2回目(hot) |"
  echo "| ${UVP} の cold | **.uwvz の作成を含む**（測定前に消してから走らせる）。hot は作られた .uwvz を再利用 |"
  echo "| 倍率 | CLI を 1 としたときの倍率（1超＝${UVP} が速い🟢） |"
  echo "| 照合 | 行番号は通しになるので**本文の集合**で突き合わせる（件数と並べ替えた本文） |"
  echo
} > "$LOG"

# 1ケース分を測って記録する
#   $1=見出し $2=キー $3=CLI関数 $4=uvp に渡す (A) $5=CLIの表示 $6=消す .uwvz（空可）
#   $7=CLI 出力の形（1="行番号:本文"・2="パス:行番号:本文"。既定 1）
SOK=0; SNG=0
one_case() {
  local title="$1" key="$2" clifn="$3" apat="$4" clishow="$5" uwvz="$6" cliform="${7:-1}"
  local cc ch pc ph hits mark
  { echo ""; echo "### $title"; echo ""; } | tee -a "$LOG"

  echo "    \$ $clishow" | tee -a "$LOG"
  cold_prep "CLI cold"
  cc="$(run_sh "$WORK/${key}_cli.txt" "$clifn")"
  ch="$(run_sh "$WORK/${key}_cli2.txt" "$clifn")"
  body_rg "$cliform" < "$WORK/${key}_cli.txt" > "$WORK/${key}_exp.txt"
  hits=$(wc -l < "$WORK/${key}_exp.txt" | tr -d ' ')
  printf '    ・ CLI  cold %7s / hot %7s / 2回計 %7s   %s 件（期待値）\n' \
    "$cc" "$ch" "$(sum2 "$cc" "$ch")" "$hits" | tee -a "$LOG"

  echo "    \$ $UVP '$apat' '$PAT'" | tee -a "$LOG"
  [ -n "$uwvz" ] && rm -f $uwvz
  cold_prep "${UVP} cold（.uwvz 無しから）"
  pc="$(run_to "$WORK/${key}_uvp.txt" "$UVP" "$apat" "$PAT")"
  ph="$(run_to "$WORK/${key}_uvp2.txt" "$UVP" "$apat" "$PAT")"
  body_uv "$(uv_fields "$WORK/${key}_uvp.txt")" < "$WORK/${key}_uvp.txt" > "$WORK/${key}_uvp.body"
  if cmp -s "$WORK/${key}_exp.txt" "$WORK/${key}_uvp.body"; then mark="✅"; SOK=$((SOK+1))
  else mark="❌"; SNG=$((SNG+1)); fi
  printf '    %s %-6s cold %7s / hot %7s / 2回計 %7s   %s 件  exit=%s\n' \
    "$mark" "$UVP" "$pc" "$ph" "$(sum2 "$pc" "$ph")" \
    "$(wc -l < "$WORK/${key}_uvp.txt" | tr -d ' ')" "$(cat "$WORK/${key}_uvp.txt.rc")" | tee -a "$LOG"
  if [ "$mark" = "❌" ]; then
    echo "      ↳ 期待 $(wc -l < "$WORK/${key}_exp.txt" | tr -d ' ') 行 / 実際 $(wc -l < "$WORK/${key}_uvp.body" | tr -d ' ') 行" | tee -a "$LOG"
    echo "      ↳ 食い違いの先頭: $(diff "$WORK/${key}_exp.txt" "$WORK/${key}_uvp.body" 2>/dev/null | head -2 | tr '\n' ' ')" | tee -a "$LOG"
    echo "      ↳ ${UVP} stderr: $(head -2 "$WORK/${key}_uvp.txt.err" 2>/dev/null | tr '\n' ' ')" | tee -a "$LOG"
  fi
  for u in $uwvz; do [ -f "$u" ] && echo "      · .uwvz: ${u}（$(gib "$(bytes "$u")") GiB）" | tee -a "$LOG"; done

  echo "" | tee -a "$LOG"
  if [ "$mark" = "✅" ]; then
    printf '      CLI/%-6s cold=%s  hot=%s  2回計=%s\n' "$UVP" \
      "$(_r "$cc" "$pc")" "$(_r "$ch" "$ph")" "$(_r "$(sum2 "$cc" "$ch")" "$(sum2 "$pc" "$ph")")" | tee -a "$LOG"
  else
    printf '      CLI/%-6s **倍率は出しません**（本文が期待値と一致していないため）\n' "$UVP" | tee -a "$LOG"
  fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$key" "$title" "$hits" "$cc" "$ch" "$pc" "$ph" >> "$TOT"
}

# =============================================================================
# 第1部 zip（#5）
# =============================================================================
make_zip() {
  local t0 t1 tmp
  [ -f "$ZIPFILE" ] && { say "  ・${ZIPFILE} は既にあります（$(gib "$(bytes "$ZIPFILE")") GiB）。作り直しません"; return 0; }
  [ "$NOSM" -ge 2 ] || { say "  ・平文が2本以上ありません"; return 1; }
  say "  ・${ZIPFILE} を作ります（元 $(gib "$SRCBYTES") GiB・deflate -${ZIPLEVEL}・元ファイルは残します）"
  say "    数分かかります。中断したら作りかけは消します"
  tmp="${ZIPFILE}.making-$$"
  t0="$(now_s)"
  if command -v zip >/dev/null 2>&1; then
    # -j でパスを落とし、エントリ名を basename にそろえる
    # shellcheck disable=SC2086
    zip -q -j -"${ZIPLEVEL}" "$tmp" $OSMS || { rm -f "$tmp"; return 1; }
  else
    python3 - "$tmp" "$ZIPLEVEL" $OSMS <<'PY' || { rm -f "$tmp"; return 1; }
import os, sys, zipfile
out, lvl = sys.argv[1], int(sys.argv[2])
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=lvl, allowZip64=True) as z:
    for p in sys.argv[3:]:
        z.write(p, os.path.basename(p))
PY
  fi
  t1="$(now_s)"
  mv -f "$tmp" "$ZIPFILE"
  ZIPMAKE="$(el "$t0" "$t1")"
  say "    → できました $(gib "$(bytes "$ZIPFILE")") GiB ／ ${ZIPMAKE} 秒"
  return 0
}

cli_zip_all()   { "$UNZIP" -p "$ZIPFILE" | "$RG" -n -F "$PAT"; }
cli_zip_entry() { "$UNZIP" -p "$ZIPFILE" "$ENTRY" | "$RG" -n -F "$PAT"; }
cli_zip_extract() {
  rm -rf "$EXDIR"; mkdir -p "$EXDIR"
  "$UNZIP" -q -o -d "$EXDIR" "$ZIPFILE"
  "$RG" -n -F "$PAT" "$EXDIR"
}

ZIPMAKE=""
run_zip() {
  { echo ""; echo "## 第1部 zip（連載 #5）"; echo ""; } >> "$LOG"
  say "== 第1部 zip =="

  if [ -z "$UNZIP" ]; then
    { echo "**unzip が見つからないため、zip の部は飛ばしました。**"; echo; } >> "$LOG"
    result SKIP "zip の速さ" "unzip が無い"
    return 0
  fi
  make_zip || { { echo "**zip を作れなかったため飛ばしました。**"; echo; } >> "$LOG"
                result SKIP "zip の速さ" "zip を作れなかった"; return 0; }

  {
    echo "| 項目 | 値 |"
    echo "|---|---|"
    echo "| zip | \`${ZIPFILE}\` … $(gib "$(bytes "$ZIPFILE")") GiB（元 $(gib "$SRCBYTES") GiB の平文 ${NOSM}本・deflate -${ZIPLEVEL}） |"
    [ -n "$ZIPMAKE" ] && echo "| zip を作るのにかかった時間 | ${ZIPMAKE} 秒（**計測の外**。参考値） |"
    echo "| 無料版 | zip 入力は \`${UVP}\` だけ（実装指示書 §2.5）。\`${UVF}\` は対象外 |"
    echo
    echo "**エントリ一覧**"
    echo
    echo '```'
    "$UNZIP" -l "$ZIPFILE" 2>/dev/null | sed -n '2,20p'
    echo '```'
  } >> "$LOG"

  # ---- Z0 rg をそのまま zip に当てる（書庫は探さないことの記録）----
  { echo ""; echo "### Z0 \`rg\` をそのまま zip に当てる（相手が書庫を探さないことの確認）"; echo ""; } | tee -a "$LOG"
  echo "    \$ rg -n -F '$PAT' $ZIPFILE" | tee -a "$LOG"
  "$RG" -n -F "$PAT" "$ZIPFILE" > "$WORK/z0.txt" 2> "$WORK/z0.err"; z0rc=$?
  {
    echo "    · exit=${z0rc} ／ 出力 $(wc -l < "$WORK/z0.txt" | tr -d ' ') 行"
    echo "    · stdout 先頭: $(head -1 "$WORK/z0.txt" 2>/dev/null | cut -c1-120)"
    echo "    · stderr 先頭: $(head -1 "$WORK/z0.err" 2>/dev/null | cut -c1-120)"
  } | tee -a "$LOG"
  if [ "$z0rc" != 0 ] || [ ! -s "$WORK/z0.txt" ]; then
    result PASS "rg は zip の中を探さない（exit=${z0rc}）" ""
  else
    result SKIP "rg は zip の中を探さない" "何か出力した。中身を見てから記事に書くこと"
  fi

  one_case "Z1 全エントリ（\`unzip -p\` のパイプ 対 \`${UVP} ZIP\`）" "z1" cli_zip_all \
           "$ZIPFILE" "unzip -p $ZIPFILE | rg -n -F '$PAT'" "${ZIPFILE}.uwvz"

  one_case "Z2 1エントリに絞る（\`${ENTRY}\`）" "z2" cli_zip_entry \
           "${ZIPFILE}!${ENTRY}" "unzip -p $ZIPFILE $ENTRY | rg -n -F '$PAT'" "uw-*.uwvz"

  if [ "$EXTRACT_CASE" = 1 ]; then
    rm -f uw-*.uwvz
    EXDIR="$WORK/extracted"
    one_case "Z3 展開してから探す（\`unzip -d\` ＋ \`rg\` 対 \`${UVP} ZIP\`）" "z3" cli_zip_extract \
             "$ZIPFILE" "unzip -q -o -d DIR $ZIPFILE && rg -n -F '$PAT' DIR" "${ZIPFILE}.uwvz" 2
    rm -rf "$EXDIR"
  else
    { echo ""; echo "### Z3 展開してから探す … 測っていません（\`--extract-case\` で有効）"; } | tee -a "$LOG"
  fi
}

# =============================================================================
# 第2部 pbf（#6）
# =============================================================================
cli_pbf() { "$OSMIUM" cat "$PBF" -f osm | "$RG" -n -F "$PAT"; }

run_pbf() {
  { echo ""; echo "## 第2部 pbf（連載 #6）"; echo ""; } >> "$LOG"
  say "== 第2部 pbf =="

  if [ -z "$PBF" ] || [ ! -f "$PBF" ]; then
    { echo "**pbf が見つからないため飛ばしました**（\`--pbf PATH\` で指定してください）。"; echo; } >> "$LOG"
    result SKIP "pbf の速さ" "pbf が見つからない"
    return 0
  fi
  {
    echo "| 項目 | 値 |"
    echo "|---|---|"
    echo "| pbf | \`${PBF}\` … $(gib "$(bytes "$PBF")") GiB |"
    echo "| 相手 | \`osmium cat ${PBF} -f osm \| rg -n -F '${PAT}'\`（**XML をディスクに出さずパイプで流す**・10.1修正） |"
    echo "| 無料版 | pbf は \`${UVP}\` だけ。\`${UVF}\` は名指しで断る（下の P0） |"
    echo
  } >> "$LOG"

  # ---- P0 uvf の断り文 ----
  { echo ""; echo "### P0 \`${UVF}\` は pbf を名指しで断る"; echo ""; } | tee -a "$LOG"
  echo "    \$ $UVF '$PBF' '$PAT'" | tee -a "$LOG"
  "$UVF" "$PBF" "$PAT" > "$WORK/p0.txt" 2> "$WORK/p0.err"; p0rc=$?
  {
    echo "    · exit=${p0rc}"
    echo "    · stderr: $(head -2 "$WORK/p0.err" 2>/dev/null | tr '\n' ' ' | cut -c1-200)"
  } | tee -a "$LOG"
  if [ "$p0rc" != 0 ] && [ -s "$WORK/p0.err" ]; then
    result PASS "${UVF} は pbf を断る（exit=${p0rc}）" "$(head -1 "$WORK/p0.err" | cut -c1-80)"
  else
    result FAIL "${UVF} は pbf を断る" "exit=${p0rc}・stderr が空"
  fi

  if [ -z "$OSMIUM" ]; then
    { echo ""
      echo "**osmium が見つからないため、相手の計測を飛ばしました。**"
      echo; echo "\`brew install osmium-tool\` で入ります（Mac）。"; echo; } >> "$LOG"
    result SKIP "pbf の相手（osmium cat ｜ rg）" "osmium が無い。brew install osmium-tool"
    return 0
  fi

  # ---- P1 相手（osmium cat | rg）----
  { echo ""; echo "### P1 \`osmium cat … -f osm | rg\` 対 \`${UVP}\`"; echo ""; } | tee -a "$LOG"
  echo "    \$ osmium cat $PBF -f osm | rg -n -F '$PAT'" | tee -a "$LOG"
  say "    （51GB 相当の XML をパイプに流します。1回に数分かかります）"
  cold_prep "osmium cold"
  oc="$(run_sh "$WORK/pbf_cli.txt" cli_pbf)"
  oh="$(run_sh "$WORK/pbf_cli2.txt" cli_pbf)"
  body_rg 1 < "$WORK/pbf_cli.txt" > "$WORK/pbf_exp.txt"
  ohits=$(wc -l < "$WORK/pbf_exp.txt" | tr -d ' ')
  printf '    ・ osmium|rg  cold %8s / hot %8s / 2回計 %8s   %s 件（期待値）\n' \
    "$oc" "$oh" "$(sum2 "$oc" "$oh")" "$ohits" | tee -a "$LOG"
  [ -s "$WORK/pbf_cli.txt.err" ] && echo "      ↳ osmium stderr: $(head -2 "$WORK/pbf_cli.txt.err" | tr '\n' ' ' | cut -c1-200)" | tee -a "$LOG"

  # ---- uvp 3回（①.uwvz 作成込み ②purge 後の再利用 ③hot）----
  UW="${PBF}.uwvz"
  echo "    \$ $UVP '$PBF' '$PAT'   … ①.uwvz 作成込み ②purge 後の再利用 ③hot" | tee -a "$LOG"
  rm -f "$UW"
  cold_prep "${UVP} ①（.uwvz 無しから）"
  p1="$(run_to "$WORK/pbf_uvp1.txt" "$UVP" "$PBF" "$PAT")"
  cold_prep "${UVP} ②（.uwvz 再利用・cold）"
  p2="$(run_to "$WORK/pbf_uvp2.txt" "$UVP" "$PBF" "$PAT")"
  p3="$(run_to "$WORK/pbf_uvp3.txt" "$UVP" "$PBF" "$PAT")"
  body_uv "$(uv_fields "$WORK/pbf_uvp1.txt")" < "$WORK/pbf_uvp1.txt" > "$WORK/pbf_uvp.body"
  if cmp -s "$WORK/pbf_exp.txt" "$WORK/pbf_uvp.body"; then mark="✅"; SOK=$((SOK+1))
  else mark="❌"; SNG=$((SNG+1)); fi
  printf '    %s %-6s ①作成込み %8s / ②再利用cold %8s / ③hot %8s   %s 件  exit=%s\n' \
    "$mark" "$UVP" "$p1" "$p2" "$p3" \
    "$(wc -l < "$WORK/pbf_uvp1.txt" | tr -d ' ')" "$(cat "$WORK/pbf_uvp1.txt.rc")" | tee -a "$LOG"
  if [ "$mark" = "❌" ]; then
    echo "      ↳ 期待 ${ohits} 行 / 実際 $(wc -l < "$WORK/pbf_uvp.body" | tr -d ' ') 行" | tee -a "$LOG"
    echo "      ↳ 食い違いの先頭: $(diff "$WORK/pbf_exp.txt" "$WORK/pbf_uvp.body" 2>/dev/null | head -2 | tr '\n' ' ' | cut -c1-200)" | tee -a "$LOG"
  fi
  [ -f "$UW" ] && echo "      · .uwvz: ${UW}（$(gib "$(bytes "$UW")") GiB）" | tee -a "$LOG"
  echo "      · 要素数など: $(grep -o 'nodes [0-9,]*.*' "$WORK/pbf_uvp1.txt.err" 2>/dev/null | head -1 | cut -c1-160)" | tee -a "$LOG"

  echo "" | tee -a "$LOG"
  if [ "$mark" = "✅" ]; then
    printf '      osmium|rg ÷ %s  ①作成込み=%s  ②再利用cold=%s  ③hot=%s\n' "$UVP" \
      "$(_r "$oc" "$p1")" "$(_r "$oc" "$p2")" "$(_r "$oh" "$p3")" | tee -a "$LOG"
    { echo ""
      echo "> ②③ は \`.uwvz\` を使い回した値なので、相手の cold／hot と比べている。"
      echo "> ①は \`.uwvz\` を作る時間を含むので、**1回で済ませる使い方なら①で比べる**こと。"; } | tee -a "$LOG"
  else
    printf '      **倍率は出しません**（本文が期待値と一致していないため）\n' | tee -a "$LOG"
  fi
  printf 'pbf\tP1 osmium cat … \\| rg 対 %s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$UVP" "$ohits" "$oc" "$oh" "$p1" "$p2" "$p3" >> "$TOT"
}

# =============================================================================
# 実行
# =============================================================================
say "${UVF} --version: $("$UVF" --version 2>&1 | head -1)"
say "${UVP} --version: $("$UVP" --version 2>&1 | head -1)"
say "データ: ${DATA}（平文 ${NOSM}本・$(gib "$SRCBYTES") GiB）／空き $(freeg "$DATA") GiB"
say ""

[ "$ONLY" = pbf ] || run_zip
[ "$ONLY" = zip ] || run_pbf

{
  echo ""
  echo "## まとめ"
  echo ""
  echo "| ケース | 件数 | CLI 1回目 | CLI 2回目 | ${UVP} 1回目 | ${UVP} 2回目 | ${UVP} 3回目 |"
  echo "|---|---:|---:|---:|---:|---:|---:|"
} | tee -a "$LOG"
while IFS=$'\t' read -r key title hits a b c d e; do
  [ -n "$key" ] || continue
  printf '| %s | %s | %s | %s | %s | %s | %s |\n' "$title" "$hits" "$a" "$b" "$c" "${d:-—}" "${e:-—}"
done < "$TOT" | tee -a "$LOG"

{
  echo ""
  echo "**照合: 一致 ${SOK} 件 ／ 不一致 ${SNG} 件 ／ キャッシュ破棄 ${NPRG} 回**"
  echo ""
  echo "**正しさの記録: PASS ${PASS} ／ FAIL ${FAIL} ／ 省略 ${NO}**"
  echo ""
  echo "> 判定の決まり（正直表示ルール）: 単項目は **1.5 倍未満を「差」と呼ばない**。"
  echo "> 秒数は OS をまたいで比べない。cold／hot と「1回目（\`.uwvz\` 作成込み）／2回目」を必ず区別する。"
} | tee -a "$LOG"

if [ "${KEEPTMP:-0}" = 1 ]; then
  say "（作業ファイルを $WORK に残しました）"
else
  rm -rf "$WORK"; say "（作業ファイルは削除しました。残すには KEEPTMP=1）"
fi
if [ "$KEEPZIP" = 0 ] && [ -f "$ZIPFILE" ]; then
  rm -f "$ZIPFILE"; say "（${ZIPFILE} を消しました。残すには KEEPZIP=1）"
fi

say ""
say "PASS ${PASS} ／ FAIL ${FAIL} ／ 省略 ${NO} ／ 照合の不一致 ${SNG}"
say "まとめ: $LOG"
[ "$FAIL" = 0 ] && [ "$SNG" = 0 ] || exit 1
exit 0
