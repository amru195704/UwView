#!/usr/bin/env bash
# =============================================================================
# uvf_vs_wf.sh — 既存の uvf（v1.6.x）と試験用の uvfWF（Wide Field）を比べる
# -----------------------------------------------------------------------------
# 目的: 「1スレッド と 8スレッド で、本当に1スレッドが速いのか」を実測で決める。
#
# ★ここを先に読んでください（測る場所を間違えないため）
#
#   uvf は **1つのファイルを検索するときは常に1スレッド**です（新旧どちらも）。
#   スレッド数が効くのは **複数ファイルを一度に検索するとき**だけで、
#   ファイル1つにつき1本を割り当てて同時に走らせます（Wide Field 段階3）。
#
#   したがって:
#     A) 1ファイル  … uvf と uvfWF の比較（スレッドは関係ない。退行が無いかの確認）
#     B) 複数ファイル … 「uvf を1つずつ順に回す（＝1本）」と
#                       「uvfWF に一度に渡す（＝N本）」の比較 ← ここが本題
#
#   B が「1スレッド vs 8スレッド」そのものです。
#
# 使い方:
#   ./uvf_vs_wf.sh -o ./uvf -n ./uvfWF -d /data/logs -g '*.log' -p ERROR
#
#   -o  既存の uvf（v1.6.x）へのパス                     既定: ./uvf
#   -n  試験用の uvfWF へのパス                          既定: ./uvfWF
#   -d  対象のあるディレクトリ（ここへ cd して測る）      既定: カレント
#   -g  複数ファイルの指定（uvfWF に渡す形・要引用符）    既定: '*.log'
#   -f  1ファイル比較で使うファイル（省略時は -g の先頭）
#   -p  検索語                                            既定: ERROR
#   -t  試すスレッド数（カンマ区切り）                    既定: 1,2,4,8
#   -r  各条件の反復回数                                  既定: 3
#   -c  CSV の出力先                                      既定: uvf_vs_wf.csv
#   --cold  毎回キャッシュを捨ててから測る（sudo が要ります）
#   -h  この使い方
#
# 出るもの: 画面に表（中央値）と、CSV に全回ぶんの生データ。
#           **結果が一致しているかも必ず確かめます**（違うものを測って速さを比べない）。
# =============================================================================
set -uo pipefail

OLD="./uvf"; NEW="./uvfWF"; DIR="."; GLOB='*.log'; ONE=""; PAT="ERROR"
THREADS="1,2,4,8"; RUNS=3; CSV="uvf_vs_wf.csv"; COLD=0

while [ $# -gt 0 ]; do
  case "$1" in
    -o) OLD="$2"; shift 2;;
    -n) NEW="$2"; shift 2;;
    -d) DIR="$2"; shift 2;;
    -g) GLOB="$2"; shift 2;;
    -f) ONE="$2"; shift 2;;
    -p) PAT="$2"; shift 2;;
    -t) THREADS="$2"; shift 2;;
    -r) RUNS="$2"; shift 2;;
    -c) CSV="$2"; shift 2;;
    --cold) COLD=1; shift;;
    -h|--help) sed -n '2,40p' "$0"; exit 0;;
    *) echo "知らない引数: $1" >&2; exit 2;;
  esac
done

OLD="$(cd "$(dirname "$OLD")" && pwd)/$(basename "$OLD")"
NEW="$(cd "$(dirname "$NEW")" && pwd)/$(basename "$NEW")"
CSV="$(cd "$(dirname "$CSV")" 2>/dev/null || cd .; pwd)/$(basename "$CSV")"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$DIR" || exit 2

[ -x "$OLD" ] || { echo "実行できません: $OLD" >&2; exit 2; }
[ -x "$NEW" ] || { echo "実行できません: $NEW" >&2; exit 2; }

# 対象ファイル一覧（順序はシェルの展開＝名前順。uvfWF の並びと同じ）
FILES=()
while IFS= read -r line; do FILES+=("$line"); done < <(eval ls -1 -- $GLOB 2>/dev/null | sort)
[ ${#FILES[@]} -gt 0 ] || { echo "対象が1つもありません: $GLOB" >&2; exit 2; }
[ -n "$ONE" ] || ONE="${FILES[0]}"

CPUS="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo '?')"
MEM_KB="$(awk '/MemTotal/{print $2}' /proc/meminfo 2>/dev/null || echo 0)"
TOTAL_BYTES=0
for f in "${FILES[@]}"; do
  TOTAL_BYTES=$((TOTAL_BYTES + $(stat -c%s "$f" 2>/dev/null || stat -f%z "$f")))
done

echo "対象: ${#FILES[@]} ファイル・合計 $((TOTAL_BYTES / 1024 / 1024)) MiB"
echo "機械: 論理プロセッサ $CPUS ／ メモリ $((MEM_KB / 1024)) MiB"
echo "検索語: $PAT"
[ "$COLD" = 1 ] && echo "測り方: cold（毎回キャッシュを捨てます）" || echo "測り方: warm（2回目以降のキャッシュに載った状態）"
echo

drop_caches() {
  [ "$COLD" = 1 ] || return 0
  sync
  sudo sh -c 'echo 3 > /proc/sys/vm/drop_caches' 2>/dev/null || {
    echo "※ キャッシュを捨てられませんでした（sudo 不可）。warm として続けます" >&2
    COLD=0
  }
}

# いまの時刻（秒・小数）。date +%s.%N は BSD（mac）で使えないので避ける
now_s() {
  if [ -n "${EPOCHREALTIME:-}" ]; then printf '%s' "${EPOCHREALTIME/,/.}"
  else python3 -c 'import time;print(f"{time.time():.6f}")'; fi
}

# 秒を測る（出力は捨てずにファイルへ。結果が同じかを後で比べる）
timed() {  # $1=出力先ファイル  残り=コマンド
  local out="$1"; shift
  local start end
  start="$(now_s)"
  "$@" >"$out" 2>>"$WORK/stderr.log"
  end="$(now_s)"
  awk -v a="$start" -v b="$end" 'BEGIN{printf "%.3f", b-a}'
}

# 旧 uvf を1つずつ順に回す（＝1スレッド）。出力は uvfWF と同じ形（ファイル名:行番号<TAB>本文）に揃える
old_many() {  # $1=出力先
  local out="$1" f
  : > "$out"
  for f in "${FILES[@]}"; do
    "$OLD" "$f" "$PAT" 2>>"$WORK/stderr.log" | sed "s#^#$f:#" >> "$out"
  done
}

[ -f "$CSV" ] || echo "when,cpus,mem_mib,files,total_mib,pattern,case,threads,run,seconds,mode" > "$CSV"
now() { date +%Y-%m-%dT%H:%M:%S; }
mode() { [ "$COLD" = 1 ] && echo cold || echo warm; }

record() { # $1=case $2=threads $3=run $4=seconds
  echo "$(now),$CPUS,$((MEM_KB/1024)),${#FILES[@]},$((TOTAL_BYTES/1024/1024)),\"$PAT\",$1,$2,$3,$4,$(mode)" >> "$CSV"
}

median() { printf '%s\n' "$@" | sort -n | awk '{a[NR]=$1} END{print (NR%2)? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2}'; }

echo "== A) 1ファイル: uvf と uvfWF（どちらも1スレッド。退行が無いかの確認）=="
a_old=(); a_new=()
for r in $(seq 1 "$RUNS"); do
  drop_caches; t_old="$(timed "$WORK/a_old.txt" "$OLD" "$ONE" "$PAT")"; a_old+=("$t_old")
  drop_caches; t_new="$(timed "$WORK/a_new.txt" "$NEW" "$ONE" "$PAT")"; a_new+=("$t_new")
  record "single_old" 1 "$r" "$t_old"
  record "single_new" 1 "$r" "$t_new"
done
if cmp -s "$WORK/a_old.txt" "$WORK/a_new.txt"; then
  echo "  結果は一致（$(wc -l < "$WORK/a_new.txt") 行）"
else
  echo "  ★結果が違います。速さを比べる前にここを調べてください"
  diff <(head -5 "$WORK/a_old.txt") <(head -5 "$WORK/a_new.txt") | head -10
fi
printf "  uvf   %ss\n  uvfWF %ss\n\n" "$(median "${a_old[@]}")" "$(median "${a_new[@]}")"

echo "== B) 複数ファイル: uvf を順に回す（1本）vs uvfWF に一度に渡す（N本）=="
b_old=()
for r in $(seq 1 "$RUNS"); do
  drop_caches
  start="$(now_s)"; old_many "$WORK/b_old.txt"; end="$(now_s)"
  t_serial="$(awk -v a="$start" -v b="$end" 'BEGIN{printf "%.3f", b-a}')"
  b_old+=("$t_serial")
  record "many_old_serial" 1 "$r" "$t_serial"
done
printf "  uvf を1つずつ順に（1本）  %ss\n" "$(median "${b_old[@]}")"

IFS=',' read -r -a TLIST <<< "$THREADS"
for t in "${TLIST[@]}"; do
  runs=()
  for r in $(seq 1 "$RUNS"); do
    drop_caches
    t_new="$(UVF_MAX_THREADS="$t" timed "$WORK/b_new_$t.txt" "$NEW" "$GLOB" "$PAT")"
    runs+=("$t_new")
    record "many_new" "$t" "$r" "$t_new"
  done
  same="一致"
  cmp -s "$WORK/b_old.txt" "$WORK/b_new_$t.txt" || same="★不一致"
  printf "  uvfWF %2s 本                %ss   （旧との結果: %s）\n" "$t" "$(median "${runs[@]}")" "$same"
done

echo
echo "CSV: $CSV"
echo "※ 結果が「★不一致」なら、違うものを測っています。速さの比較より先に中身を確かめてください"
