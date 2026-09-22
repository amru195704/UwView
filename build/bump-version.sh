#!/usr/bin/env bash
# 版数を1つ上げる（UwView / UVF）。
#
# **dist を作るたびに上げる**（オーナー指示 2026-09-22）。
# 試験物を手元に何本も置くので、版数が同じだと「どれを入れたか」が分からなくなる。
#   v1.7.0 → v1.7.0.1 → v1.7.0.2 …
# 出荷版に上げるときは --set で明示する（例: --set 1.7.1）。
#
# 使い方:
#   build/bump-version.sh              4つ目を1つ上げる（無ければ .1 を付ける）
#   build/bump-version.sh --set 1.7.1  指定した版数にする
#   build/bump-version.sh --show       いまの版数を出すだけ
set -euo pipefail
cd "$(dirname "$0")/.."

# 版数を書いてあるところ（GUI 本体と CLI。両方そろえる）
FILES=(UwView/UwView.csproj UwView.Cli/UwView.Cli.csproj)

current() { grep -oE '<Version>[^<]+' "${FILES[0]}" | sed 's/<Version>//' | head -1; }

case "${1:-}" in
  --show) current; exit 0 ;;
  --set)  next="${2:?--set には版数が要ります（例: --set 1.7.1）}" ;;
  "")     next="$(python3 - "$(current)" <<'PY'
import sys
parts = sys.argv[1].split('.')
while len(parts) < 3: parts.append('0')
if len(parts) == 3: parts.append('1')          # 1.7.0 → 1.7.0.1
else: parts[3] = str(int(parts[3]) + 1)        # 1.7.0.1 → 1.7.0.2
print('.'.join(parts))
PY
)" ;;
  *) echo "使い方: build/bump-version.sh [--set <版数> | --show]" >&2; exit 2 ;;
esac

from="$(current)"
for f in "${FILES[@]}"; do
  python3 - "$f" "$next" <<'PY'
import re, sys
path, version = sys.argv[1], sys.argv[2]
text = open(path, encoding='utf-8-sig').read()
new, n = re.subn(r'<Version>[^<]+</Version>', f'<Version>{version}</Version>', text, count=1)
if n != 1: raise SystemExit(f'{path}: <Version> が見つかりません')
open(path, 'w', encoding='utf-8-sig').write(new)
PY
done
echo "UwView: $from → $next"
