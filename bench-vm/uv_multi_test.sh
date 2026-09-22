#!/usr/bin/env bash
# =============================================================================
# uv_multi_test.sh — 複数ファイル機能のテスト（Wide Field 段階3・段階4）
#
#   uv_all_test.sh と同じ形（PASS/FAIL を並べ、<OS>_result/summary.md に残す）で、
#   **複数ファイル指定・統合 .uwvz** だけを確かめる。
#
#   データ（osm17/）の前提:
#     japan-dv-1m.osm / 3m / 6m / 10m … 平文（複数ファイル検索・統合 .uwvz の対象）
#     japan-dv-ac.gz / japan-dv-ai.gz  … 圧縮（**まだ対象外**であることの確認に使う）
#
#   確かめること:
#     A 展開      (A) の書き方（ワイルドカード・カンマ・空白）と --files
#     B 検索結果  rg（無ければ uvf 1本ずつ）と突き合わせ。-i / -E / -v / -H / -h / --json
#     C 圧縮混在  gz を黙って素通りしない（uvf）・黙って束ねない（uvp）
#     D 統合uwvz  作る／再利用／名前(B)でも引ける／追加を見落とさない／
#                 変更で予告が出る／-extract がバイト一致／大きさが 1/9〜1/12
#
# 使い方（distWideField で・uvfWF / uvpWF と同じ場所）:
#   ./uv_multi_test.sh                      osm17 を使い、<OS>_result/ へ結果を書く
#   ./uv_multi_test.sh -d osm17 -o win_result
#   ./uv_multi_test.sh --uvf ./uvfWF --uvp ./uvpWF
#   ./uv_multi_test.sh --no-uvp             uvf だけ（ライセンスが無い環境）
#
#   -p 検索語（既定 東京）  -q 2語目（既定 大阪）
# =============================================================================
set -u

DATA="osm17"; OUT=""; UVF="./uvfWF"; UVP="./uvpWF"; PAT="東京"; PAT2="大阪"; WITH_UVP=1

while [ $# -gt 0 ]; do
  case "$1" in
    -d) DATA="$2"; shift 2;;
    -o) OUT="$2"; shift 2;;
    --uvf) UVF="$2"; shift 2;;
    --uvp) UVP="$2"; shift 2;;
    -p) PAT="$2"; shift 2;;
    -q) PAT2="$2"; shift 2;;
    --no-uvp) WITH_UVP=0; shift;;
    -h|--help) sed -n '2,30p' "$0"; exit 0;;
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

resolve() {  # $1=指定 $2=説明 → 実体のパス（Windows は .exe を補う）
  local given="$1" what="$2" found="" try
  for try in "$given" "$given.exe"; do
    case "$try" in
      */*) if [ -x "$try" ]; then found="$(cd "$(dirname "$try")" && pwd)/$(basename "$try")"; fi ;;
      *)   found="$(command -v "$try" 2>/dev/null || true)" ;;
    esac
    if [ -n "$found" ]; then break; fi
  done
  if [ -z "$found" ]; then echo "$what が見つかりません: $given" >&2; exit 2; fi
  printf '%s' "$found"
}
UVF="$(resolve "$UVF" uvfWF)"
[ "$WITH_UVP" = 1 ] && UVP="$(resolve "$UVP" uvpWF)"

[ -d "$DATA" ] || { echo "データのフォルダがありません: $DATA" >&2; exit 2; }
mkdir -p "$OUT"
WORK="$OUT/work"; rm -rf "$WORK"; mkdir -p "$WORK"
LOG="$OUT/summary.md"
DETAIL="$OUT/detail"; mkdir -p "$DETAIL"

PLAIN="$DATA/*.osm"          # 平文だけ（統合 .uwvz の対象）
ALL="$DATA/*"                # gz も含む（混在の確認用）
RG="$(command -v rg 2>/dev/null || true)"

PASS=0; FAIL=0; NO=0
{
  echo "# 複数ファイル機能テスト（Wide Field 段階3・段階4）"
  echo
  echo "| | |"
  echo "|---|---|"
  echo "| 実行 | $(date '+%Y-%m-%d %H:%M:%S') |"
  echo "| OS | $OSKIND |"
  echo "| uvf | $("$UVF" --version 2>/dev/null) |"
  [ "$WITH_UVP" = 1 ] && echo "| uvp | $("$UVP" --version 2>/dev/null) |"
  echo "| データ | $DATA |"
  echo "| 検索語 | $PAT ／ $PAT2 |"
  echo "| 突き合わせ | $([ -n "$RG" ] && echo 'rg' || echo 'uvf を1本ずつ') |"
  echo
  echo "| No | 確かめたこと | 結果 | 備考 |"
  echo "|---|---|---|---|"
} > "$LOG"

say() { printf '%s\n' "$*"; }
result() {  # $1=結果(PASS/FAIL/SKIP) $2=題 $3=備考
  case "$1" in
    PASS) PASS=$((PASS+1)); say "  ✅ $2";;
    FAIL) FAIL=$((FAIL+1)); say "  ❌ $2  … $3";;
    SKIP) NO=$((NO+1));     say "  －  $2  … $3";;
  esac
  NO_N=$((PASS+FAIL+NO))
  echo "| $NO_N | $2 | $1 | ${3//|/／} |" >> "$LOG"
}
check() {  # $1=題 $2=期待 $3=実際 （文字列比較）
  if [ "$2" = "$3" ]; then result PASS "$1" ""; else result FAIL "$1" "期待「$2」 実際「$3」"; fi
}
check_file() {  # $1=題 $2=期待ファイル $3=実際ファイル
  if cmp -s "$2" "$3"; then result PASS "$1" ""
  else result FAIL "$1" "$(diff "$2" "$3" 2>/dev/null | head -3 | tr '\n' ' ')"; fi
}

# 平文ファイルの一覧（名前順）
FILES=()
while IFS= read -r line; do FILES+=("$line"); done < <(ls -1d $PLAIN 2>/dev/null | sort)
[ ${#FILES[@]} -ge 2 ] || { echo "平文が2つ以上ありません: $PLAIN" >&2; exit 2; }

say "データ: ${#FILES[@]} ファイル（平文）／ $(ls -1d $ALL 2>/dev/null | wc -l | tr -d ' ') ファイル（gz 含む）"
say "結果: $OUT/"
say ""

# ---- 参照（正解）を作る: rg があれば rg、無ければ uvf を1本ずつ ----
reference() {  # $1=出力先 $2..=uvf/rg のオプション
  local out="$1"; shift
  : > "$out"
  local f
  for f in "${FILES[@]}"; do
    if [ -n "$RG" ]; then
      "$RG" -n "$@" -- "$PAT" "$f" 2>/dev/null | sed "s#^#$f:#" | sed 's#:\([0-9][0-9]*\):#:\1\t#' >> "$out"
    else
      "$UVF" "$f" "$PAT" "$@" 2>/dev/null | sed "s#^#$f:#" >> "$out"
    fi
  done
}

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
check_file "複数ファイルの結果が参照と一致（$(wc -l < "$DETAIL/ref.txt" | tr -d ' ') 行）" "$DETAIL/ref.txt" "$DETAIL/multi.txt"

for opt in -i -v; do
  reference "$DETAIL/ref$opt.txt" "$opt"
  "$UVF" "$PLAIN" "$PAT" "$opt" > "$DETAIL/multi$opt.txt" 2>/dev/null
  check_file "$opt でも一致" "$DETAIL/ref$opt.txt" "$DETAIL/multi$opt.txt"
done

hcount="$("$UVF" "$PLAIN" "$PAT" -h 2>/dev/null | grep -c ":" )"
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
say "== C 圧縮の混在 =="
"$UVF" "$ALL" "$PAT" > "$DETAIL/mixed.txt" 2>"$DETAIL/mixed.err"; code=$?
if [ "$code" = 2 ] && grep -q "gz" "$DETAIL/mixed.err"; then
  result PASS "uvf: gz を名指しで知らせ、平文だけ探す（exit 2）" ""
else
  result FAIL "uvf: gz を名指しで知らせ、平文だけ探す（exit 2）" "exit=$code $(head -1 "$DETAIL/mixed.err")"
fi

gz="$(ls -1d $DATA/*.gz 2>/dev/null | head -1)"
if [ -n "$gz" ]; then
  n="$("$UVF" "$gz" "$PAT" 2>/dev/null | wc -l | tr -d ' ')"
  if [ "$n" -gt 0 ]; then result PASS "uvf: gz 単体はこれまでどおり検索できる（$n 行）" ""
  else result FAIL "uvf: gz 単体はこれまでどおり検索できる" "0 行"; fi
else
  result SKIP "uvf: gz 単体" "gz がありません"
fi

if [ "$WITH_UVP" = 1 ]; then
  ( cd "$WORK" && "$UVP" "../../$ALL" "$PAT" >/dev/null 2>"../../$DETAIL/uvp-mixed.err" ); code=$?
  if [ "$code" = 2 ] && grep -qi "compress\|圧縮" "$DETAIL/uvp-mixed.err"; then
    result PASS "uvp: gz が混ざったら束ねずに断る" ""
  else
    result FAIL "uvp: gz が混ざったら束ねずに断る" "exit=$code $(head -1 "$DETAIL/uvp-mixed.err")"
  fi
fi

if [ "$WITH_UVP" = 0 ]; then
  say ""
  say "（uvp は省きました）"
else
  say ""
  say "== D 統合 .uwvz =="
  # 作業用に小さい2本を写して、追加・変更まで試す（実データは触らない）
  small=$(ls -S -r $PLAIN 2>/dev/null | head -2)
  i=1
  for f in $small; do cp "$f" "$WORK/m$i.osm"; i=$((i+1)); done
  ( cd "$WORK" || exit 2

    "$UVP" '*.osm' "$PAT" > d1.txt 2>d1.err; code=$?
    uwvz="$(ls -1 *.uwvz 2>/dev/null | head -1)"
    if [ -n "$uwvz" ] && [ "$code" -le 1 ]; then echo "PASS|統合 .uwvz を作る（$uwvz）|"; else echo "FAIL|統合 .uwvz を作る|exit=$code $(head -1 d1.err)"; fi

    "$UVP" '*.osm' "$PAT" > d2.txt 2>d2.err
    if ! grep -q "creating" d2.err && cmp -s d1.txt d2.txt; then echo "PASS|2回目は作り直さず同じ結果|"; else echo "FAIL|2回目は作り直さず同じ結果|$(head -1 d2.err)"; fi

    "$UVP" "$uwvz" "$PAT" > d3.txt 2>/dev/null
    if cmp -s d1.txt d3.txt; then echo "PASS|名前(B)でも同じ結果|"; else echo "FAIL|名前(B)でも同じ結果|"; fi

    head -50 m1.osm > m3.osm            # ファイルを増やす
    "$UVP" '*.osm' "$PAT" > d4.txt 2>d4.err
    if [ "$(wc -l < d4.txt)" -ge "$(wc -l < d1.txt)" ] && grep -q "m3.osm" d4.txt; then
      echo "PASS|増えたファイルの行も拾う|"
    else
      echo "FAIL|増えたファイルの行も拾う|$(head -1 d4.err)"
    fi

    printf '\n%s\n' "$PAT" >> m1.osm    # 中身を変える
    "$UVP" '*.osm' "$PAT" > d5.txt 2>d5.err
    if grep -qi "changed\|変化" d5.err; then echo "PASS|中身が変わったら予告して作り直す|"; else echo "FAIL|中身が変わったら予告して作り直す|$(head -1 d5.err)"; fi

    # -extract でバイト一致に戻せるか（完了条件1）
    rm -f m3.osm; "$UVP" '*.osm' "$PAT" >/dev/null 2>&1
    uwvz="$(ls -1 *.uwvz 2>/dev/null | head -1)"
    rm -rf back; "$UVP" "$uwvz" -extract txt -out back > extract.log 2>&1
    got="$(ls -1 back 2>/dev/null | tr '\n' ' ')"
    same=1
    for f in *.osm; do cmp -s "$f" "back/$f" || same=0; done
    if [ -n "$got" ] && [ "$same" = 1 ]; then
      echo "PASS|-extract で元の名前・バイト一致で戻せる|$got"
    else
      echo "FAIL|-extract で元の名前・バイト一致で戻せる|$(head -1 extract.log) got=[$got]"
    fi

    # 大きさ（完了条件4: 元の 1/9〜1/12）
    total=0
    for f in *.osm; do total=$((total + $(stat -c%s "$f" 2>/dev/null || stat -f%z "$f"))); done
    size=$(stat -c%s "$uwvz" 2>/dev/null || stat -f%z "$uwvz")
    ratio=$(awk -v a="$total" -v b="$size" 'BEGIN{printf "%.1f", a/b}')
    echo "INFO|.uwvz の大きさは元の 1/$ratio|元 $((total/1024/1024))MiB → $((size/1024/1024))MiB"
  ) > "$DETAIL/uvp.txt" 2>&1

  while IFS='|' read -r r title note; do
    case "$r" in
      PASS|FAIL) result "$r" "$title" "$note";;
      INFO) result PASS "$title" "$note";;
    esac
  done < "$DETAIL/uvp.txt"
fi

{
  echo
  echo "**結果: PASS $PASS ／ FAIL $FAIL ／ 省略 $NO**"
} >> "$LOG"

say ""
say "PASS $PASS ／ FAIL $FAIL ／ 省略 $NO"
say "まとめ: $LOG"
[ "$FAIL" = 0 ] || exit 1
exit 0
