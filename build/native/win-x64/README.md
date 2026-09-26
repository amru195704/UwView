# Windows x64 に同梱する展開ライブラリ

Windows には OS の liblzma・libzstd が無いので、公式の配布物から取った DLL を実行ファイルの隣に置く
（`build/publish.sh` の Windows の手順が写す。UwView Pro の publish.sh もここを使う）。
uvf / uvp は起動したフォルダーの DLL を探し、無ければ .NET 向けの展開に戻す（`UwView.Core/NativeCompression.cs`）。

| DLL | 版 | 入手元 | ライセンス |
|---|---|---|---|
| `liblzma.dll` | XZ Utils 5.8.4 | https://github.com/tukaani-project/xz/releases/download/v5.8.4/xz-5.8.4-windows.zip の `bin_x86-64/` | 0BSD（MinGW-w64 ランタイム部分は `third-party/` の通り） |
| `libzstd.dll` | zstd 1.5.7 | https://github.com/facebook/zstd/releases/download/v1.5.7/zstd-v1.5.7-win64.zip の `dll/` | BSD 3-Clause（GPLv2 との二重ライセンスの BSD を選ぶ） |

- 取得: 2026-09-26（v1.7.2.1）。依存は KERNEL32・C ランタイム（Windows 10 以降に標準）だけ
- liblz4 は公式の Windows 配布物に DLL が入っていない（静的ライブラリのみ）ので同梱していない
- libbz2 は bzip2 の公式に Windows 版 DLL が無いので同梱していない
- 目的: xz を OS 並みの並列展開にする（Windows で rg -z に 1/7.95 だった）。.uwvz の zstd 圧縮・展開も速くなる
