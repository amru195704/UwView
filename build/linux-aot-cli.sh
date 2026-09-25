#!/usr/bin/env bash
# Linux 向けの CLI（uvf・uvp）を NativeAOT でビルドする（UwView・UwView Pro の publish.sh から呼ぶ）。
#
# NativeAOT はその OS の上でしかビルドできないので、Mac の中の Linux（Colima＋Docker・arm64）で作る。
# 環境は /Volumes/BIWIN/Docker に置いてある（env.sh・colima-start.sh・dotnet-aot/Dockerfile）。
# Linux でも起動が「起動アプリ → 本体（JIT）」の約 0.3 秒から、rg と同じ 0.01 秒前後になる（2026-09-25 の切り分け）。
#
# ソースは読み取り専用で見せ、コンテナの中へ要るものだけ写してからビルドする
#（Mac 側の obj/ と混ざると、Mac のビルドの復元情報を Linux のパスで上書きしてしまうため）。
#
# 使い方: linux-aot-cli.sh <rid> <プロジェクト（26-git からの相対）> <出力フォルダー（絶対パス）>
#   例: linux-aot-cli.sh linux-arm64 UwView/UwView.Cli/UwView.Cli.csproj /…/obj/pub-cli/linux-arm64
set -euo pipefail

RID="$1"; PROJ="$2"; OUTDIR="$3"
GIT=/Volumes/BIWIN/26-git
DOCKER_DIR=/Volumes/BIWIN/Docker
IMAGE=uwview-aot:10.0

source "${DOCKER_DIR}/env.sh"
if ! docker info >/dev/null 2>&1; then
  echo "Linux のビルド環境（Colima）が動いていません。先に ${DOCKER_DIR}/colima-start.sh を実行してください" >&2
  echo "（NativeAOT を使わず従来の起動アプリで作るなら LINUX_AOT=0 を付けて publish してください）" >&2
  exit 1
fi
if ! docker image inspect "${IMAGE}" >/dev/null 2>&1; then
  echo "ビルド用イメージ ${IMAGE} を作ります（初回だけ）" >&2
  docker build -t "${IMAGE}" "${DOCKER_DIR}/dotnet-aot" 1>&2
fi

mkdir -p "${OUTDIR}" "${DOCKER_DIR}/nuget"
docker run --rm \
  -v "${GIT}:/src:ro" -v "${OUTDIR}:/out" -v "${DOCKER_DIR}/nuget:/root/.nuget/packages" \
  -e EDITION="${EDITION:-}" \
  "${IMAGE}" bash -c "
    set -euo pipefail
    mkdir -p /work
    cd /src
    # 各リポ直下の共通設定と、CLI に要るプロジェクトだけ（bin・obj は写さない）
    tar -cf - --exclude=bin --exclude=obj \
        UwView/Directory.Packages.props UwView/global.json UwView/UwView.Core UwView/UwView.Cli \
        UwViewPro/Directory.Packages.props UwViewPro/global.json \
        UwViewPro/src/UwView.Pro.Core UwViewPro/src/UwView.Pro.Cli \
      | tar -C /work -xf -
    dotnet publish '/work/${PROJ}' -c Release -r '${RID}' -p:DebugType=none -o /tmp/pub 1>&2
    cp /tmp/pub/* /out/
  "
