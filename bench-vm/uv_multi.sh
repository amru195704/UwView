#!/usr/bin/env bash
# =============================================================================
# uv_multi.sh — 複数ファイル機能のテスト（正しさ＋速さ）
#
#   これ1本で、uv_multi_test.sh（正しさ）と uv_multi_speed.sh（速さ）の両方を行う。
#   置き場所は UwTest／対象は UwTest/osm17（オーナー指示 2026-09-24）。
#
#   第1部 正しさ  PASS/FAIL を並べる
#     A 展開      (A) の書き方（ワイルドカード・カンマ・空白）と --files
#     B 検索結果  rg と突き合わせ。-i / -v / -h / -H / --json / 終了コード
#     C 圧縮・断り 平文と gz の混在／gz 単体／テキストでない gz を断る／
#                  複数ファイルの -open を断る／zip の中をエントリごとに束ねる／pbf
#     D 統合uwvz  作る／再利用／名前(B)でも引ける／追加を見落とさない／
#                 追加は足したぶんだけで済む／変更で予告／-extract がバイト一致／大きさ
#
#   第2部 速さ    3つの土俵で CLI（ripgrep）と比べる
#     ① *.osm 平文だけ        rg
#     ② *.gz  圧縮だけ        rg -z
#     ③ 平文＋圧縮の混在      rg -z（**1コマンドで両方を見られる**）
#
#   ripgrep は -z で gz を展開しながら探せる（オーナー指摘 2026-09-24）。
#   展開パイプラインと同じ速さ・同じ出力なので（5.75GB の gz で rg -z 21.6 秒 /
#   gzip -dc | rg 21.8 秒・Mac）、CLI 側は利用者が実際に打つ `rg -z` に揃える。
#   混在も rg -z なら1コマンドで足りる＝③は**純粋な速度比較**である。
#
# 使い方（uvfWF / uvpWF と同じ場所で。環境はそのまま使う）:
#   ./uv_multi.sh                       osm17 を使い、<OS>_result/ へ結果を書く
#   ./uv_multi.sh --only test           正しさだけ
#   ./uv_multi.sh --only speed          速さだけ
#   ./uv_multi.sh -d osm17 -o mac_result
#   ./uv_multi.sh --no-uvp              uvf だけ（ライセンスが無い環境）
#   ./uv_multi.sh --pbf ../UwViewData/osm/japan-latest.osm.pbf   pbf も試す
#
#   -p 検索語（既定 東京）  -q 2語目（既定 大阪）
#   PURGE=0  キャッシュ破棄をしない（cold は参考値）
#   SETTLE=5 破棄後の待機秒（既定 10）
#   MKGZ=0   足りない gz を作らない（無ければ ②③ を飛ばす）
#   KEEPTMP=1 作業ファイルを残す
# =============================================================================
set -u

DATA="osm17"; OUT=""; UVF="uvfWF"; UVP="uvpWF"; PAT="東京"; PAT2="大阪"
WITH_UVP=1; ONLY="all"; PBF="${PBF:-}"
PURGE="${PURGE:-1}"; SETTLE="${SETTLE:-10}"; MKGZ="${MKGZ:-1}"; GZLEVEL="${GZLEVEL:-6}"

while [ $# -gt 0 ]; do
  case "$1" in
    -d) DATA="$2"; shift 2;;
    -o) OUT="$2"; shift 2;;
    --uvf) UVF="$2"; shift 2;;
    --uvp) UVP="$2"; shift 2;;
    -p) PAT="$2"; shift 2;;
    -q) PAT2="$2"; shift 2;;
    --pbf) PBF="$2"; shift 2;;
    --only) ONLY="$2"; shift 2;;
    --no-uvp) WITH_UVP=0; shift;;
    -h|--help) sed -n '2,42p' "$0"; exit 0;;
    *) echo "知らない引数: $1" >&2; exit 2;;
  esac
done
case "$ONLY" in all|test|speed) ;; *) echo "--only は test / speed / all です" >&2; exit 2;; esac

case "$(uname -s)" in
  Darwin) OSKIND=mac ;;
  Linux)  OSKIND=linux ;;
  MINGW*|MSYS*|CYGWIN*) OSKIND=win ;;
  *) OSKIND=other ;;
esac
[ -n "$OUT" ] || OUT="${OSKIND}_result"
[ -d "$DATA" ] || { echo "データのフォルダがありません: $DATA" >&2; exit 2; }

TS="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
# Git for Windows の bash では pwd が /c/… 形になり、Windows の uvp.exe は読めない。
# pwd -W で C:/… 形を得る（mac / Linux では -W が無いので普通の pwd）
ROOT="$(pwd -W 2>/dev/null || pwd)"
OUT="$(cd "$OUT" && pwd)/${TS}-multi"; mkdir -p "$OUT"
WORK="$OUT/work"; mkdir -p "$WORK"
DETAIL="$OUT/detail"; mkdir -p "$DETAIL"
LOG="$OUT/summary.md"
TOT="$OUT/speed.tsv"; : > "$TOT"

PLAIN="$DATA/*.osm"
ALL="$DATA/*"
RG="$(command -v rg 2>/dev/null || true)"
[ -n "$RG" ] || { echo "rg が見つかりません" >&2; exit 2; }

PASS=0; FAIL=0; NO=0; NPRG=0

say() { printf '%s\n' "$*"; }
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
bytes(){ stat -c %s "$1" 2>/dev/null || stat -f %z "$1" 2>/dev/null; }
list_of() { LC_ALL=C ls -1d $1 2>/dev/null | LC_ALL=C sort; }

result() {  # $1=PASS/FAIL/SKIP $2=題 $3=備考
  case "$1" in
    PASS) PASS=$((PASS+1)); say "  ✅ $2";;
    FAIL) FAIL=$((FAIL+1)); say "  ❌ $2  … $3";;
    SKIP) NO=$((NO+1));     say "  －  $2  … $3";;
  esac
  echo "| $((PASS+FAIL+NO)) | $2 | $1 | ${3//|/／} |" >> "$LOG"
}
check()      { if [ "$2" = "$3" ]; then result PASS "$1" ""; else result FAIL "$1" "期待「$2」 実際「$3」"; fi; }
check_file() { if cmp -s "$2" "$3"; then result PASS "$1" ""
               else result FAIL "$1" "$(diff "$2" "$3" 2>/dev/null | head -3 | tr '\n' ' ')"; fi; }

# ---- 出力の正規化（path<TAB>行番号<TAB>本文 に揃えて並べ替える）----
#   **sed は使わない。** macOS の BSD sed は置換文字列の \t をタブとして扱わず、
#   文字クラス [:\t] も「: か \ か t」になる。awk なら 3OS で同じに動く。
norm_rg() { awk '{ i=index($0,":"); if(i==0){print; next}
                   p=substr($0,1,i-1); r=substr($0,i+1);
                   j=index(r,":");      if(j==0){print; next}
                   printf "%s\t%s\t%s\n", p, substr(r,1,j-1), substr(r,j+1) }' | LC_ALL=C sort; }
norm_uv() { awk '{ i=index($0,":"); if(i==0){print; next}
                   p=substr($0,1,i-1); r=substr($0,i+1);
                   j=match(r,/[:\t]/); if(j==0){print; next}
                   printf "%s\t%s\t%s\n", p, substr(r,1,j-1), substr(r,j+RLENGTH) }' | LC_ALL=C sort; }

run_to() { local o="$1"; shift; local t0 t1
           t0="$(now_s)"; "$@" > "$o" 2> "$o.err"; echo $? > "$o.rc"; t1="$(now_s)"; el "$t0" "$t1"; }
run_sh() { local o="$1" fn="$2" t0 t1
           t0="$(now_s)"; "$fn" > "$o" 2> "$o.err"; echo $? > "$o.rc"; t1="$(now_s)"; el "$t0" "$t1"; }

# ---- キャッシュ破棄（速さの部でだけ使う）----
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

# ---- 統合 .uwvz の後始末（このスクリプトが作ったものだけ消す）----
base_uwvz() { { list_of "*.uwvz"; list_of "$DATA/*.uwvz"; } 2>/dev/null; }
base_uwvz > "$WORK/uwvz.base"
clean_new_uwvz() {
  base_uwvz > "$WORK/uwvz.now"
  LC_ALL=C comm -13 "$WORK/uwvz.base" "$WORK/uwvz.now" 2>/dev/null | while IFS= read -r f; do
    [ -n "$f" ] && rm -f "$f"
  done
}

# ---- 足りない gz を作る（元の .osm は消さない）----
GZTMP=""
trap '[ -n "$GZTMP" ] && rm -f "$GZTMP"; echo; echo "中断しました（作りかけの gz は削除済み）"; exit 130' INT TERM
make_missing_gz() {
  local base src gz t0 t1
  for base in japan-dv-1m.osm japan-dv-3m.osm; do
    src="$DATA/$base"; gz="$DATA/${base}.gz"
    [ -f "$gz" ] && continue
    [ "$MKGZ" = 1 ] || { say "  ・$gz が無く、MKGZ=0 なので作りません"; continue; }
    [ -f "$src" ] || continue
    say "  ・$gz を作ります（元 $(gib "$(bytes "$src")") GiB・gzip -${GZLEVEL}・元ファイルは残します）"
    GZTMP="${gz}.making-$$"; t0="$(now_s)"
    if ! gzip -"$GZLEVEL" -c "$src" > "$GZTMP" 2>/dev/null; then
      rm -f "$GZTMP"; GZTMP=""; say "      → 失敗しました"; continue; fi
    t1="$(now_s)"; mv -f "$GZTMP" "$gz"; GZTMP=""
    say "      → できました $(gib "$(bytes "$gz")") GiB ／ $(el "$t0" "$t1") 秒"
  done
}

say "$UVF --version: $("$UVF" --version 2>&1 | head -1)"
[ "$WITH_UVP" = 1 ] && say "$UVP --version: $("$UVP" --version 2>&1 | head -1)"
say "データ: $DATA"
say ""
say "== gz の準備 =="
make_missing_gz
say ""

OSMS="$(list_of "$DATA/*.osm")"
GZS="$(list_of "$DATA/*.gz")"
NOSM=$(printf '%s\n' "$OSMS" | grep -c . || true)
NGZ=$(printf '%s\n' "$GZS"  | grep -c . || true)
[ "$NOSM" -ge 2 ] || { echo "平文が2本以上ありません: $PLAIN" >&2; exit 2; }

FILES=()
while IFS= read -r line; do [ -n "$line" ] && FILES+=("$line"); done <<EOF
$OSMS
EOF

A_OSM="$DATA/*.osm"
A_GZ="$DATA/*.gz"
A_ALL="$DATA/*.osm,$DATA/*.gz"

{
  echo "# 複数ファイル機能テスト（Wide Field）  $TS"
  echo
  echo "| 項目 | 値 |"
  echo "|---|---|"
  echo "| OS | $(uname -sr) $(uname -m)（判定: ${OSKIND}） |"
  echo "| ${UVF} | $(command -v "$UVF") $("$UVF" --version 2>/dev/null | head -1) |"
  [ "$WITH_UVP" = 1 ] && echo "| ${UVP} | $(command -v "$UVP") $("$UVP" --version 2>/dev/null | head -1) |"
  echo "| ripgrep | $(command -v rg) ($("$RG" --version | head -1)) |"
  echo "| データ | $DATA … 平文 ${NOSM}本 ／ gz ${NGZ}本 |"
  echo "| 検索語 | $PAT ／ $PAT2 |"
  echo "| 実行する部 | $ONLY |"
  echo "| CLI 側 | 平文は \`rg\`、gz は \`rg -z\`（ripgrep が展開しながら探す）。**混在も rg -z の1コマンドで足りる** |"
  echo
} > "$LOG"

# =============================================================================
# 第1部 正しさ
# =============================================================================
run_correctness() {
  {
    echo "## 第1部 正しさ"
    echo
    echo "| No | 確かめたこと | 結果 | 備考 |"
    echo "|---|---|---|---|"
  } >> "$LOG"

  reference_stream() {  # $1..=rg のオプション
    local f
    for f in "${FILES[@]}"; do
      "$RG" -n "$@" -- "$PAT" "$f" 2>/dev/null | sed "s#^#$f:#" | sed 's#:\([0-9][0-9]*\):#:\1\t#'
    done
  }
  reference() { local out="$1"; shift; reference_stream "$@" > "$out"; }

  say "== A 展開 =="
  got="$("$UVF" --files "$PLAIN" 2>&1 | awk '{print $2}' | tr '\n' ' ' | sed 's/ $//')"
  want="$(printf '%s ' "${FILES[@]}" | sed 's/ $//')"
  check "--files が指定順・名前順に出す" "$want" "$got"

  a="$("$UVF" "$PLAIN" "$PAT" 2>/dev/null | wc -l | tr -d ' ')"
  comma="$(printf '%s,' "${FILES[@]}" | sed 's/,$//')"
  b="$("$UVF" "$comma" "$PAT" 2>/dev/null | wc -l | tr -d ' ')"
  space="$(printf '%s ' "${FILES[@]}" | sed 's/ $//')"
  c="$("$UVF" "$space" "$PAT" 2>/dev/null | wc -l | tr -d ' ')"
  check "ワイルドカード・カンマ・空白で同じ結果" "$a $a" "$b $c"

  "$UVF" "${FILES[0]}" "${FILES[1]}" "$PAT" >/dev/null 2>"$DETAIL/quote.txt"; code=$?
  if [ "$code" = 2 ] && grep -qi "引用符\|Quote" "$DETAIL/quote.txt"; then
    result PASS "シェルが展開した形は引用符を促す" ""
  else
    result FAIL "シェルが展開した形は引用符を促す" "exit=$code"
  fi

  say ""
  say "== B 検索結果 =="
  reference "$DETAIL/ref.txt"
  "$UVF" "$PLAIN" "$PAT" > "$DETAIL/multi.txt" 2>/dev/null
  check_file "複数ファイルの結果が rg と一致（$(wc -l < "$DETAIL/ref.txt" | tr -d ' ') 行）" \
             "$DETAIL/ref.txt" "$DETAIL/multi.txt"

  reference "$DETAIL/ref-i.txt" -i
  "$UVF" "$PLAIN" "$PAT" -i > "$DETAIL/multi-i.txt" 2>/dev/null
  check_file "-i でも一致" "$DETAIL/ref-i.txt" "$DETAIL/multi-i.txt"

  # -v は当たらない行を全部出すので、実データでは数GBになる。
  # ファイルに書かず、照合値（cksum：CRC と長さ）で比べる
  want_v="$(reference_stream -v | cksum)"
  got_v="$("$UVF" "$PLAIN" "$PAT" -v 2>/dev/null | cksum)"
  check "-v でも一致（照合値）" "$want_v" "$got_v"

  plain_ok=$("$UVF" "$PLAIN" "$PAT" -h 2>/dev/null | head -1 | grep -c "^[0-9]")
  check "-h でファイル名を付けない" "1" "$plain_ok"

  one_h="$("$UVF" "${FILES[0]}" "$PAT" -H 2>/dev/null | head -1 | cut -d: -f1)"
  check "-H は1ファイルでもファイル名を付ける" "${FILES[0]}" "$one_h"

  json_lines="$("$UVF" "$PLAIN" "$PAT" --json 2>/dev/null | grep -c '"file"')"
  text_lines="$(wc -l < "$DETAIL/multi.txt" | tr -d ' ')"
  check "--json の行数が本文と一致し file が入る" "$text_lines" "$json_lines"

  "$UVF" "$PLAIN" "ZZ_NOT_FOUND_ZZ" >/dev/null 2>&1; code=$?
  check "見つからなければ exit 1" "1" "$code"

  say ""
  say "== C 圧縮と断り方 =="
  # 混在の参照は rg -z の1コマンド（ripgrep が gz を展開しながら探す）
  if [ "$NGZ" -ge 1 ]; then
    "$RG" -z -n -F -- "$PAT" $A_OSM $A_GZ 2>/dev/null | norm_rg > "$DETAIL/mixed-ref.txt"
    "$UVF" "$A_ALL" "$PAT" 2>"$DETAIL/mixed.err" | norm_uv > "$DETAIL/mixed.txt"
    check_file "uvf: 平文と gz を混ぜて探し、rg -z と一致（$(wc -l < "$DETAIL/mixed-ref.txt" | tr -d ' ') 行）" \
               "$DETAIL/mixed-ref.txt" "$DETAIL/mixed.txt"

    gz1="$(printf '%s\n' "$GZS" | head -1)"
    n="$("$UVF" "$gz1" "$PAT" 2>/dev/null | wc -l | tr -d ' ')"
    if [ "$n" -gt 0 ]; then result PASS "uvf: gz 単体はこれまでどおり検索できる（$n 行）" ""
    else result FAIL "uvf: gz 単体はこれまでどおり検索できる" "0 行"; fi
  else
    result SKIP "uvf: 平文と gz の混在" "gz がありません"
    result SKIP "uvf: gz 単体" "gz がありません"
  fi

  # テキストでない gz は断る（小さなバイナリをその場で作る）
  printf '\000\001\002binary data\000\377\000' > "$WORK/blob.bin"
  gzip -c "$WORK/blob.bin" > "$WORK/blob.bin.gz" 2>/dev/null
  "$UVF" "$WORK/blob.bin.gz" "$PAT" >"$DETAIL/bin.out" 2>"$DETAIL/bin.err"; code=$?
  if [ "$code" = 2 ] && [ ! -s "$DETAIL/bin.out" ]; then
    result PASS "uvf: テキストでない gz は断る" "$(head -1 "$DETAIL/bin.err" | cut -c1-60)"
  else
    result FAIL "uvf: テキストでない gz は断る" "exit=$code（テキスト判定は v1.7.0.18 以降）"
  fi

  # 複数ファイルの -open は uvp を案内して断る（画面は起こさない）
  "$UVF" "$PLAIN" "$PAT" -open >/dev/null 2>"$DETAIL/open.err"; code=$?
  if [ "$code" = 2 ] && grep -q "uvp" "$DETAIL/open.err"; then
    result PASS "uvf: 複数ファイルの -open は断って uvp を案内" ""
  else
    result FAIL "uvf: 複数ファイルの -open は断って uvp を案内" "exit=$code $(head -1 "$DETAIL/open.err")"
  fi

  if [ "$WITH_UVP" = 1 ]; then
    # zip（テキスト2本＋テキストでない1本）をその場で作る
    zipok=0
    if command -v python3 >/dev/null 2>&1; then
      python3 - "$WORK" "$PAT" <<'PY' && zipok=1
import sys, zipfile
work, pat = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(f"{work}/mix.zip", "w") as z:
    z.writestr("app/app.log", f"a1 {pat}\na2 ok\n")
    z.writestr("server/error.log", f"s1 {pat}\ns2 ok\n")
    z.writestr("blob.bin", bytes([0, 1, 2, 0, 255]))
PY
    elif command -v zip >/dev/null 2>&1; then
      ( cd "$WORK" && mkdir -p zsrc/app zsrc/server \
        && printf 'a1 %s\na2 ok\n' "$PAT" > zsrc/app/app.log \
        && printf 's1 %s\ns2 ok\n' "$PAT" > zsrc/server/error.log \
        && cp blob.bin zsrc/blob.bin \
        && ( cd zsrc && zip -qr ../mix.zip . ) ) && zipok=1
    fi
    if [ "$zipok" = 1 ]; then
      ( cd "$WORK" && "$UVP" mix.zip "$PAT" > zip.out 2> zip.err )
      hits="$(grep -c "mix.zip!" "$WORK/zip.out" 2>/dev/null || echo 0)"
      check "uvp: zip の中をエントリごとに束ねて探せる" "2" "$hits"
      if grep -q "blob.bin" "$WORK/zip.err"; then
        result PASS "uvp: zip のテキストでないエントリは名指しで飛ばす" ""
      else
        result FAIL "uvp: zip のテキストでないエントリは名指しで飛ばす" "$(head -1 "$WORK/zip.err")"
      fi
    else
      result SKIP "uvp: zip の中をエントリごとに束ねて探せる" "zip を作る手段がありません"
      result SKIP "uvp: zip のテキストでないエントリは名指しで飛ばす" "zip を作る手段がありません"
    fi

    # pbf（--pbf を指定したときだけ。.uwvz は pbf の隣にでき、消さずに残す）
    if [ -n "$PBF" ] && [ -f "$PBF" ]; then
      t0="$(now_s)"
      "$UVP" "$PBF" "$PAT" > "$DETAIL/pbf.out" 2> "$DETAIL/pbf.err"; code=$?
      t1="$(now_s)"
      hits="$(wc -l < "$DETAIL/pbf.out" | tr -d ' ')"
      counts="$(grep -o 'nodes [0-9,]*' "$DETAIL/pbf.err" | head -1)"
      if [ "$code" = 0 ] && [ "$hits" -gt 0 ]; then
        result PASS "uvp: pbf を XML にして探せる（$hits 行・$(el "$t0" "$t1") 秒）" "$counts"
      else
        result FAIL "uvp: pbf を XML にして探せる" "exit=$code $(head -1 "$DETAIL/pbf.err")"
      fi
    else
      result SKIP "uvp: pbf を XML にして探せる" "--pbf で pbf を渡すと試します"
    fi
  fi

  if [ "$WITH_UVP" = 0 ]; then
    say ""
    say "（uvp は省きました）"
    return 0
  fi

  say ""
  say "== D 統合 .uwvz =="
  # 作業用に小さい2本を写して、追加・変更まで試す（実データは触らない）
  small=$(ls -S -r $PLAIN 2>/dev/null | head -2)
  i=1
  for f in $small; do cp "$f" "$WORK/m$i.osm"; i=$((i+1)); done
  ( cd "$WORK" || exit 2

    t0="$(now_s)"; "$UVP" '*.osm' "$PAT" > d1.txt 2>d1.err; code=$?; t1="$(now_s)"
    create_s="$(el "$t0" "$t1")"
    uwvz="$(ls -1 *.uwvz 2>/dev/null | head -1)"
    if [ -n "${uwvz}" ] && [ "$code" -le 1 ]; then echo "PASS|統合 .uwvz を作る（${uwvz}）|"; else echo "FAIL|統合 .uwvz を作る|exit=$code $(head -1 d1.err)"; fi
    # できなかったら、残りは確かめようがない（空どうしを比べて PASS にしない）
    if [ -z "${uwvz}" ]; then
      for t in "2回目は作り直さず同じ結果" "名前(B)でも同じ結果" "増えたファイルの行も拾う" \
               "追加は足したぶんだけで済む（作り直さない）" "平文と gz を混ぜて束ね、rg -z と一致" \
               "中身が変わったら予告して作り直す" "-extract で元の名前・バイト一致で戻せる" ".uwvz の大きさ"; do
        echo "SKIP|$t|統合 .uwvz ができなかったため"
      done
      exit 0
    fi

    "$UVP" '*.osm' "$PAT" > d2.txt 2>d2.err
    if ! grep -q "creating" d2.err && cmp -s d1.txt d2.txt; then echo "PASS|2回目は作り直さず同じ結果|"; else echo "FAIL|2回目は作り直さず同じ結果|$(head -1 d2.err)"; fi

    "$UVP" "${uwvz}" "$PAT" > d3.txt 2>/dev/null
    if cmp -s d1.txt d3.txt; then echo "PASS|名前(B)でも同じ結果|"; else echo "FAIL|名前(B)でも同じ結果|"; fi

    # ファイルを増やす。**検索語を含む行から作る**（先頭の数十行を切り出すだけだと、
    # 実データでは語が1件も無く「拾えたか」を確かめられない）
    grep -m 5 -- "$PAT" m1.osm > m3.osm
    t0="$(now_s)"; "$UVP" '*.osm' "$PAT" > d4.txt 2>d4.err; t1="$(now_s)"
    append_s="$(el "$t0" "$t1")"
    if [ "$(wc -l < d4.txt)" -ge "$(wc -l < d1.txt)" ] && grep -q "m3.osm" d4.txt; then
      echo "PASS|増えたファイルの行も拾う|"
    else
      echo "FAIL|増えたファイルの行も拾う|$(head -1 d4.err)"
    fi
    if grep -q "added" d4.err && ! grep -q "creating" d4.err; then
      echo "PASS|追加は足したぶんだけで済む（作り直さない）|作成 ${create_s}s → 追加 ${append_s}s"
    else
      echo "FAIL|追加は足したぶんだけで済む（作り直さない）|$(grep -m1 'creating\|added' d4.err)"
    fi

    printf '\n%s\n' "$PAT" >> m1.osm    # 中身を変える
    "$UVP" '*.osm' "$PAT" > d5.txt 2>d5.err
    if grep -qi "changed\|変化" d5.err; then echo "PASS|中身が変わったら予告して作り直す|"; else echo "FAIL|中身が変わったら予告して作り直す|$(head -1 d5.err)"; fi

    # -extract でバイト一致に戻せるか
    rm -f m3.osm; "$UVP" '*.osm' "$PAT" >/dev/null 2>&1
    uwvz="$(ls -1 *.uwvz 2>/dev/null | head -1)"
    rm -rf back; "$UVP" "${uwvz}" -extract txt -out back > extract.log 2>&1
    got="$(ls -1 back 2>/dev/null | tr '\n' ' ')"
    same=1
    for f in *.osm; do cmp -s "$f" "back/$f" || same=0; done
    if [ -n "$got" ] && [ "$same" = 1 ]; then
      echo "PASS|-extract で元の名前・バイト一致で戻せる|$got"
    else
      echo "FAIL|-extract で元の名前・バイト一致で戻せる|$(head -1 extract.log) got=[$got]"
    fi

    # 平文と gz を混ぜて束ねる。参照は rg -z の1コマンド
    gzip -c m2.osm > z.osm.gz
    "$RG" -z -n -F -- "$PAT" m1.osm z.osm.gz 2>/dev/null \
      | awk '{ i=index($0,":"); p=substr($0,1,i-1); r=substr($0,i+1); j=index(r,":");
               printf "%s\t%s\t%s\n", p, substr(r,1,j-1), substr(r,j+1) }' | LC_ALL=C sort > d7.ref
    "$UVP" 'm1.osm,z.osm.gz' "$PAT" 2>d7.err \
      | awk '{ i=index($0,":"); p=substr($0,1,i-1); r=substr($0,i+1); j=match(r,/[:\t]/);
               printf "%s\t%s\t%s\n", p, substr(r,1,j-1), substr(r,j+RLENGTH) }' | LC_ALL=C sort > d7.txt
    if [ -s d7.ref ] && cmp -s d7.ref d7.txt; then
      echo "PASS|平文と gz を混ぜて束ね、rg -z と一致（$(wc -l < d7.txt | tr -d ' ') 行）|"
    else
      echo "FAIL|平文と gz を混ぜて束ね、rg -z と一致|$(head -1 d7.err)"
    fi
    rm -f z.osm.gz 'm1.osm%cz.osm.gz.uwvz'

    # 大きさ（元の 1/9〜1/12 が目安）
    total=0
    for f in *.osm; do total=$((total + $(stat -c%s "$f" 2>/dev/null || stat -f%z "$f"))); done
    size=$(stat -c%s "${uwvz}" 2>/dev/null || stat -f%z "${uwvz}")
    ratio=$(awk -v a="$total" -v b="$size" 'BEGIN{printf "%.1f", a/b}')
    echo "INFO|.uwvz の大きさは元の 1/$ratio|元 $((total/1024/1024))MiB → $((size/1024/1024))MiB"
  ) > "$DETAIL/uvp.txt" 2>&1

  if ! grep -qE '^(PASS|FAIL|SKIP|INFO)\|' "$DETAIL/uvp.txt"; then
    result FAIL "統合 .uwvz の確認が途中で止まった" "$(head -1 "$DETAIL/uvp.txt")"
  fi
  while IFS='|' read -r r title note; do
    case "$r" in
      PASS|FAIL|SKIP) result "$r" "$title" "$note";;
      INFO) result PASS "$title" "$note";;
    esac
  done < "$DETAIL/uvp.txt"

  { echo; echo "**正しさ: PASS $PASS ／ FAIL $FAIL ／ 省略 $NO**"; echo; } >> "$LOG"
}

# =============================================================================
# 第2部 速さ
# =============================================================================
cli_osm() { "$RG" -n -F "$PAT" $A_OSM; }
cli_gz()  { "$RG" -z -n -F "$PAT" $A_GZ; }
cli_all() { "$RG" -z -n -F "$PAT" $A_OSM $A_GZ; }

SOK=0; SNG=0
one_case() {  # $1=見出し $2=区分キー $3=CLI関数 $4=(A)の文字列 $5=CLIの表示
  local title="$1" key="$2" clifn="$3" apat="$4" clishow="$5"
  local cc ch fc fh pc ph mark fmark pmark hits uw
  fmark="－"; pmark="－"; pc=0; ph=0
  { echo ""; echo "### $title"; echo ""; } | tee -a "$LOG"

  echo "    \$ $clishow" | tee -a "$LOG"
  cold_prep "CLI cold"
  cc="$(run_sh "$WORK/${key}_cli.txt" "$clifn")"
  ch="$(run_sh "$WORK/${key}_cli2.txt" "$clifn")"
  norm_rg < "$WORK/${key}_cli.txt" > "$WORK/${key}_exp.txt"
  hits=$(wc -l < "$WORK/${key}_exp.txt" | tr -d ' ')
  printf '    ・ CLI  cold %7s / hot %7s / 2回計 %7s   %s 件（期待値）\n' \
    "$cc" "$ch" "$(sum2 "$cc" "$ch")" "$hits" | tee -a "$LOG"

  echo "    \$ $UVF '$apat' '$PAT'" | tee -a "$LOG"
  cold_prep "uvf cold"
  fc="$(run_to "$WORK/${key}_uvf.txt" "$UVF" "$apat" "$PAT")"
  fh="$(run_to "$WORK/${key}_uvf2.txt" "$UVF" "$apat" "$PAT")"
  norm_uv < "$WORK/${key}_uvf.txt" > "$WORK/${key}_uvf.norm"
  if cmp -s "$WORK/${key}_exp.txt" "$WORK/${key}_uvf.norm"; then mark="✅"; SOK=$((SOK+1))
  else mark="❌"; SNG=$((SNG+1)); fi
  fmark="$mark"
  printf '    %s %-6s cold %7s / hot %7s / 2回計 %7s   %s 件  exit=%s\n' \
    "$mark" "$UVF" "$fc" "$fh" "$(sum2 "$fc" "$fh")" \
    "$(wc -l < "$WORK/${key}_uvf.txt" | tr -d ' ')" "$(cat "$WORK/${key}_uvf.txt.rc")" | tee -a "$LOG"
  [ "$mark" = "❌" ] && echo "      ↳ ${UVF} stderr: $(head -2 "$WORK/${key}_uvf.txt.err" 2>/dev/null | tr '\n' ' ')" | tee -a "$LOG"

  if [ "$WITH_UVP" = 1 ]; then
    echo "    \$ $UVP '$apat' '$PAT'" | tee -a "$LOG"
    clean_new_uwvz                      # cold に「統合 .uwvz を作る」を含める
    cold_prep "uvp cold（.uwvz 無しから）"
    pc="$(run_to "$WORK/${key}_uvp.txt" "$UVP" "$apat" "$PAT")"
    ph="$(run_to "$WORK/${key}_uvp2.txt" "$UVP" "$apat" "$PAT")"
    norm_uv < "$WORK/${key}_uvp.txt" > "$WORK/${key}_uvp.norm"
    if cmp -s "$WORK/${key}_exp.txt" "$WORK/${key}_uvp.norm"; then mark="✅"; SOK=$((SOK+1))
    else mark="❌"; SNG=$((SNG+1)); fi
    pmark="$mark"
    printf '    %s %-6s cold %7s / hot %7s / 2回計 %7s   %s 件  exit=%s\n' \
      "$mark" "$UVP" "$pc" "$ph" "$(sum2 "$pc" "$ph")" \
      "$(wc -l < "$WORK/${key}_uvp.txt" | tr -d ' ')" "$(cat "$WORK/${key}_uvp.txt.rc")" | tee -a "$LOG"
    [ "$mark" = "❌" ] && echo "      ↳ ${UVP} stderr: $(head -2 "$WORK/${key}_uvp.txt.err" 2>/dev/null | tr '\n' ' ')" | tee -a "$LOG"
    uw="$(base_uwvz | LC_ALL=C comm -13 "$WORK/uwvz.base" - 2>/dev/null | head -1)"
    [ -n "$uw" ] && echo "      · 統合 .uwvz: ${uw}（$(gib "$(bytes "$uw")") GiB）" | tee -a "$LOG"
  fi

  echo "" | tee -a "$LOG"
  if [ "$fmark" = "✅" ]; then
    printf '      CLI/%-6s cold=%s  hot=%s  2回計=%s\n' "$UVF" \
      "$(_r "$cc" "$fc")" "$(_r "$ch" "$fh")" "$(_r "$(sum2 "$cc" "$ch")" "$(sum2 "$fc" "$fh")")" | tee -a "$LOG"
  else
    printf '      CLI/%-6s **倍率は出しません**（結果が期待値と一致していないため）\n' "$UVF" | tee -a "$LOG"
  fi
  if [ "$WITH_UVP" = 1 ]; then
    if [ "$pmark" = "✅" ]; then
      printf '      CLI/%-6s cold=%s  hot=%s  2回計=%s\n' "$UVP" \
        "$(_r "$cc" "$pc")" "$(_r "$ch" "$ph")" "$(_r "$(sum2 "$cc" "$ch")" "$(sum2 "$pc" "$ph")")" | tee -a "$LOG"
    else
      printf '      CLI/%-6s **倍率は出しません**（結果が期待値と一致していないため）\n' "$UVP" | tee -a "$LOG"
    fi
  fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$key" "$hits" "$cc" "$ch" "$fc" "$fh" "$pc" "$ph" "$fmark" "$pmark" >> "$TOT"
}

run_speed() {
  {
    echo "## 第2部 速さ"
    echo
    echo "| 測り方 | sync → キャッシュ破棄 → **${SETTLE}秒待機** → 1回目(cold) → 続けて 2回目(hot) |"
    echo "|---|---|"
    echo "| ${UVP} の cold | **統合 .uwvz の作成を含む**（測定前に消してから走らせる）。hot は作られた .uwvz を再利用 |"
    echo "| 倍率 | CLI を 1 としたときの倍率（1超＝${UVF}/${UVP} が速い🟢） |"
    echo
    echo "### 対象ファイル"
    echo
    echo '```'
    printf '%s\n' "$OSMS" | while IFS= read -r f; do [ -n "$f" ] && printf '%-34s %s GiB\n' "$f" "$(gib "$(bytes "$f")")"; done
    printf '%s\n' "$GZS"  | while IFS= read -r f; do [ -n "$f" ] && printf '%-34s %s GiB\n' "$f" "$(gib "$(bytes "$f")")"; done
    echo '```'
  } >> "$LOG"

  one_case "① 平文 ${NOSM}本（\`*.osm\`）" "osm" cli_osm "$A_OSM" "rg -n -F '$PAT' $A_OSM"
  if [ "$NGZ" -ge 2 ]; then
    one_case "② 圧縮 ${NGZ}本（\`*.gz\`）" "gz" cli_gz "$A_GZ" "rg -z -n -F '$PAT' $A_GZ"
    one_case "③ 混在 $((NOSM+NGZ))本（\`*.osm\` ＋ \`*.gz\`）" "all" cli_all "$A_ALL" "rg -z -n -F '$PAT' $A_OSM $A_GZ"
  else
    { echo ""; echo "### ② ③ … gz が2本未満のため飛ばしました"; } | tee -a "$LOG"
  fi

  {
    echo ""
    echo "### まとめ（速さ）"
    echo ""
    echo "| 土俵 | 件数 | CLI 2回計 | ${UVF} 2回計 | CLI/${UVF} | ${UVP} 2回計 | CLI/${UVP} |"
    echo "|---|---:|---:|---:|---|---:|---|"
  } | tee -a "$LOG"
  while IFS=$'\t' read -r key hits cc ch fc fh pc ph fm pm; do
    [ -n "$key" ] || continue
    case "$key" in
      osm) nm="① 平文 ${NOSM}本" ;;
      gz)  nm="② 圧縮 ${NGZ}本" ;;
      all) nm="③ 混在 $((NOSM+NGZ))本" ;;
      *)   nm="$key" ;;
    esac
    ct="$(sum2 "$cc" "$ch")"; ft="$(sum2 "$fc" "$fh")"; pt="$(sum2 "$pc" "$ph")"
    if [ "$fm" = "✅" ]; then fr="$(_r "$ct" "$ft")"; else fr="— ❌"; ft="$ft ❌"; fi
    if [ "$WITH_UVP" = 1 ]; then
      if [ "$pm" = "✅" ]; then pr="$(_r "$ct" "$pt")"; else pr="— ❌"; pt="$pt ❌"; fi
      printf '| %s | %s | %s | %s | %s | %s | %s |\n' "$nm" "$hits" "$ct" "$ft" "$fr" "$pt" "$pr"
    else
      printf '| %s | %s | %s | %s | %s | — | — |\n' "$nm" "$hits" "$ct" "$ft" "$fr"
    fi
  done < "$TOT" | tee -a "$LOG"

  {
    echo ""
    echo "**③ の見どころ**: ripgrep も \`-z\` で平文と gz を1コマンドで探せる。"
    echo "手間は同じなので、ここは**純粋な速さ比べ**である。"
    echo "${UVF} は毎回読み直す／${UVP} は初回に統合 .uwvz を作り、2回目以降はそれを使う。"
    echo ""
    echo "**速さの照合: 一致 $SOK 件 ／ 不一致 $SNG 件 ／ キャッシュ破棄 $NPRG 回**"
  } | tee -a "$LOG"
}

# =============================================================================
# 実行
# =============================================================================
[ "$ONLY" = speed ] || run_correctness
[ "$ONLY" = test ]  || run_speed

clean_new_uwvz
if [ "${KEEPTMP:-0}" = 1 ]; then
  say "（作業ファイルを $WORK に残しました）"
else
  rm -rf "$WORK"; say "（作業ファイルは削除しました。残すには KEEPTMP=1）"
fi

say ""
say "PASS $PASS ／ FAIL $FAIL ／ 省略 $NO ／ 速さの不一致 $SNG"
say "まとめ: $LOG"
[ "$FAIL" = 0 ] && [ "$SNG" = 0 ] || exit 1
exit 0
