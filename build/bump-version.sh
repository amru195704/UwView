#!/usr/bin/env bash
# 版数を1つ上げる（UwView / UVF）。
#
# **dist を作るたびに上げる**（9.22修正）。
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

# 表示する版数。.NET の <Version> は数字 4 つまでなので、5 つ目まで使う版（1.7.3.6.2 のような修正版）は
# <InformationalVersion> に全部を書き、<Version> には先頭 4 つを書く（UVP と同じ。2026-10-02）。
# build/publish.sh も表示する版数をファイル名に使う
current() {
  local v
  v="$(grep -oE '<InformationalVersion>[^<]+' "${FILES[0]}" | sed 's/<InformationalVersion>//' | head -1)"
  [ -n "$v" ] || v="$(grep -oE '<Version>[^<]+' "${FILES[0]}" | sed 's/<Version>//' | head -1)"
  echo "$v"
}

case "${1:-}" in
  --show) current; exit 0 ;;
  --set)  next="${2:?--set には版数が要ります（例: --set 1.7.1）}" ;;
  "")     next="$(python3 - "$(current)" <<'PY'
import sys
parts = sys.argv[1].split('.')
while len(parts) < 3: parts.append('0')
if len(parts) == 3: parts.append('1')          # 1.7.0 → 1.7.0.1
else: parts[-1] = str(int(parts[-1]) + 1)      # 1.7.0.1 → 1.7.0.2・1.7.3.6.1 → 1.7.3.6.2
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
four = '.'.join(version.split('.')[:4])          # .NET の版数（数字 4 つまで）
new, n = re.subn(r'<Version>[^<]+</Version>', f'<Version>{four}</Version>', text, count=1)
if n != 1: raise SystemExit(f'{path}: <Version> が見つかりません')
# 表示する版数（そのまま）。無ければ <Version> の次の行に足す。.NET が付けるコミット番号（+abc）は付けない
if '<InformationalVersion>' in new:
    new = re.sub(r'<InformationalVersion>[^<]+</InformationalVersion>',
                 f'<InformationalVersion>{version}</InformationalVersion>', new, count=1)
else:
    new = re.sub(r'(\n(\s*)<Version>[^<]+</Version>)',
                 lambda m: f'{m.group(1)}\n{m.group(2)}<InformationalVersion>{version}</InformationalVersion>'
                           f'\n{m.group(2)}<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>',
                 new, count=1)
open(path, 'w', encoding='utf-8-sig').write(new)
PY
done
echo "UwView: $from → $next"
