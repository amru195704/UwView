# UwView テストシリーズ

uvf（無料版 UwView のコマンド）と uvp（有料版 UwView Pro のコマンド）を、macOS・Linux・Windows で
同じデータ・同じ手順で試すための一式です。ブログ連載「UwView テスト」の数値は、すべてここの CSV から作ります。

* ルール（cold／hot・中央値・1.5 倍未満は同等・OS をまたいで秒を比べない）: [docs/00-rules.md](docs/00-rules.md)（英語）
* 見つかった弱点: [weak-points.md](weak-points.md)

```sh
cd testsuite
python3 tools/gen.py --work ~/uwtest --preset smoke     # 小さいデータ（1 分ほど）
python3 tools/check.py data --work ~/uwtest             # SHA-256 の照合
python3 tools/run.py --work ~/uwtest --label my-machine # 計測（hot・uvf）
```

巨大なデータは配布せず、`tools/gen.py` が種を固定して作ります（どの OS でも同じバイト列・同じ SHA-256）。
作業フォルダーはリポジトリの外に置いてください。100GB の回は空きが 250GB ほど要ります。
