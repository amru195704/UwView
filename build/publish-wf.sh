#!/usr/bin/env bash
# 試験用ビルド（Wide Field の開発版）— UwView / UVF
#
# **名前は正式名**（オーナー指示 2026-09-27「uvfWF/uvpWF は正式名に戻す」）:
#   配布物   UwView-<ver>-…            アプリ  UwView.app
#   実行体   UwView.Desktop            CLI     uvf
#   bundle   net.y42u.uwview           表示    題名・--version に (Wide Field) を付けない
# 出力先だけは distWideField/ のまま（dist/ は公開した配布物として git に登録してあるので汚さない）。
# 以前は並べて置けるよう uvfWF・UwViewWF.app などの別名にしていた（〜1.7.3）。
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

export EDITION=""
export MAC_SIGN_ID="${MAC_SIGN_ID:-9737970DAE0495030DFF12A5BB6B442C62B044F0}"
export AC_PROFILE="${AC_PROFILE:-uwviewpro-notary}"

RIDS=("$@")
if [ ${#RIDS[@]} -eq 0 ]; then RIDS=(osx-arm64 linux-arm64 win-x64); fi

if [ -z "${NO_BUMP:-}" ]; then build/bump-version.sh; fi

OUT=distWideField build/publish.sh "${RIDS[@]}"

echo ""
echo "==================== 試験用ビルド完了 ===================="
ls -la distWideField/
