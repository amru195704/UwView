#!/usr/bin/env bash
# 試験用ビルド（Wide Field）— UwView / UVF
#
# 本番の dist とは別物で、**既に入れてある版と並べて置ける**ように名前をすべて変える:
#   配布物   UwViewWF-<ver>-…          アプリ  UwView (Wide Field).app
#   実行体   UwView.DesktopWF          CLI     uvfWF
#   bundle   net.y42u.uwview.wf        表示    題名・About・--version に (Wide Field)(v<ver>)
#
# **作る対象は3点だけ**（オーナー指示 2026-09-22）:
#   mac arm64 ／ Linux arm64 ／ Windows x64
#
# **焼くたびに版数を1つ上げる**（v1.7.0 → v1.7.0.1 → …）。試験物を取り違えないため。
#
# 使い方:
#   build/publish-wf.sh                  3点そろえて焼く
#   build/publish-wf.sh linux-arm64      対象を絞る（版上げはする）
#   NO_BUMP=1 build/publish-wf.sh        版数を上げずに焼き直す
set -euo pipefail
cd "$(dirname "$0")/.."

export EDITION="${EDITION:-Wide Field}"
export MAC_SIGN_ID="${MAC_SIGN_ID:-9737970DAE0495030DFF12A5BB6B442C62B044F0}"
export AC_PROFILE="${AC_PROFILE:-uwviewpro-notary}"

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then RIDS=(osx-arm64 linux-arm64 win-x64); fi

if [ -z "${NO_BUMP:-}" ]; then build/bump-version.sh; fi

SUFFIX=WF OUT=distWideField APP_NAME="UwView (Wide Field)" build/publish.sh "${RIDS[@]}"

echo ""
echo "==================== 試験用ビルド完了 ===================="
ls -la distWideField/
