#!/usr/bin/env bash
# Linux 向けの CLI（uvf）を NativeAOT でビルドする（build/publish.sh から呼ぶ）。
#
# NativeAOT はその OS の上でしかビルドできないので、Mac の中の Linux（Colima＋Docker・arm64）で作る。
# x86_64（linux-x64）も同じ arm64 の中からクロスでつなぐ。
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
# UwView・UwViewPro を並べて置いてある親フォルダ（スクリプトの場所から決める。2026-11-25 に 26-git 直下 → 26-git/Uw へ移った）
GIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
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

# x86_64 は arm64 の中からクロスでつなぐ。後処理の objcopy も x86_64 用を使う（arm64 用は x86_64 を扱えない）。
# リンカー・C ライブラリはイメージ（dotnet-aot/Dockerfile）に入れてある（1.7.3.5 のリリースで追加・2026-09-28）
# 古い glibc（イメージの中の /sysroots/focal＝Ubuntu 20.04・glibc 2.31）に対してリンクする。イメージ自身（Ubuntu 24.04）の
# glibc でリンクすると 2.38 以上が要る実行ファイルになり、Ubuntu 22.04・Debian 12・RHEL 9 系で起動しなかった（2026-11-25）。
# 約束する下限は glibc 2.34（RHEL 9 系）。作ったあと下で確かめる
CROSS="-p:SysRoot=/sysroots/focal"
[ "${RID}" = linux-x64 ] && CROSS="${CROSS} -p:ObjCopyName=x86_64-linux-gnu-objcopy"

mkdir -p "${OUTDIR}" "${DOCKER_DIR}/nuget"
docker run --rm \
  -v "${GIT}:/src:ro" -v "${OUTDIR}:/out" -v "${DOCKER_DIR}/nuget:/root/.nuget/packages" \
  -e EDITION="${EDITION:-}" \
  -e UV_BUILD_NUMBER="${UV_BUILD_NUMBER:-$(date +%y.%m.%d.%H)}" \
  "${IMAGE}" bash -c "
    set -euo pipefail
    mkdir -p /work
    cd /src
    # 各リポ直下の共通設定と、CLI に要るプロジェクトだけ（bin・obj は写さない）
    tar -cf - --exclude=bin --exclude=obj \
        UwView/Directory.Packages.props UwView/global.json UwView/UwView.Core UwView/UwView.Cli \
      | tar -C /work -xf -
    dotnet publish '/work/${PROJ}' -c Release -r '${RID}' -p:DebugType=none ${CROSS} -o /tmp/pub 1>&2
    cp /tmp/pub/* /out/
  "

# 作った実行ファイルが要る glibc の版を確かめる（2.34 を超えたら止める。超えるのは、イメージが古い版のままのとき）
for f in "${OUTDIR}"/*; do
  [ -f "$f" ] && [ -x "$f" ] || continue
  need="$(objdump -T "$f" 2>/dev/null | grep -oE 'GLIBC_[0-9.]+' | sed 's/GLIBC_//' | sort -u -V | tail -1)"
  [ -n "$need" ] || continue
  echo "  $(basename "$f") (${RID}) が要る glibc: ${need}" >&2
  if [ "$(printf '%s\n' "$need" 2.34 | sort -V | tail -1)" != 2.34 ]; then
    echo "glibc の下限（2.34）を超えています: $(basename "$f") は ${need} が要ります。ビルド用イメージを作り直してください: docker build -t ${IMAGE} ${DOCKER_DIR}/dotnet-aot" >&2
    exit 1
  fi
done
