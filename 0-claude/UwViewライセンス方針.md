> パス: UwView/0-claude/UwViewライセンス方針.md

# UwView ライセンス方針

## 決定

- 採用ライセンス: **PolyForm Internal Use License 1.0.0**（source-available。OSI「オープンソース」ではないが、ソースは GitHub で公開する＝「両立」方針）。
- 意図: **社内利用は無料／第三者への配布は別途商用ライセンス**。
  - 無料（許可される用途）: 個人利用、および**あなた自身と会社の社内業務利用**（営利企業の社内利用も含めて無料）。
  - 不可（別途商用ライセンスが必要）: 本ソフトの**配布**全般＝再配布・自社製品/サービスへの組込み・転売・第三者への提供・OEM・ホスティング提供。
- 旧方針からの変更: README 等の **MIT は撤回**（MITは商用配布も無償許可のため意図と不一致）。Prosperity も検討したが「営利企業の社内利用まで有料」になるため不採用。社内無料の意図に合う PolyForm Internal Use を採用。

## LICENSE ファイル

- 本フォルダの `LICENSE-PolyForm-Internal-Use-1.0.0.txt` を、リポジトリ直下に **`LICENSE`** として設置する（Mac 側で配置・コミット）。
- 冒頭の要約に **商用（再配布）ライセンスの問い合わせ先**を記入すること（例: GitHub Issues もしくはメール）。

## README ライセンス節（日英・貼り付け用）

### 日本語

```
## ライセンス

UwView は [PolyForm Internal Use License 1.0.0](LICENSE) で提供されます。

- 個人利用、および企業の社内業務利用は**無料**です。
- 本ソフトの**再配布・自社製品/サービスへの組込み・転売・第三者への提供**はできません。
  これらを行う場合は別途「商用（再配布）ライセンス」が必要です。
- 商用ライセンスのお問い合わせ: <連絡先>

本ソフトは「現状のまま」提供され、いかなる保証もありません。自己責任でご利用ください。
```

### English

```
## License

UwView is licensed under the [PolyForm Internal Use License 1.0.0](LICENSE).

- Free for internal use by individuals and companies (internal business operations).
- Redistribution, bundling into a product or service, resale, or providing the
  software to third parties is not permitted under this license. Those uses require
  a separate commercial (redistribution) license.
- Commercial licensing inquiries: <contact>

Provided "as is", without warranty of any kind. Use at your own risk.
```

## 商用（再配布）ライセンスの考え方

社内利用は無料なので、課金対象は「**配布・組込み・転売・OEM・第三者提供**」に限られる。収益源は次の3本立てで整理する。

1. **商用（再配布/OEM）ライセンス** — 製品・サービスに UwView を組み込む／再配布する企業向け。個別見積り＋年額または買い切り。
   - 目安（要・市場調査後に確定）: 再配布/OEM 1製品あたり 年額 ¥100,000〜、または買い切り ¥200,000〜。規模・サポート範囲で調整。
2. **Pro版**（任意・オープンコア的追加価値） — 検索強化・索引キャッシュ・Tail強化など（`UwView販売戦略.md` §6）。
3. **GitHub Sponsors** — 応援・寄付（同 §7）。

> 個人開発者にとって強制力は限定的。ライセンスは「期待値の明示」と「請求・交渉の法的足場」を作るもの、という前提で運用する。正確な商用ライセンス文面は専門家確認を推奨。

## 配布チャネルへの影響（非OSIのため注意）

- GitHub のライセンス表示は「Other」になる（OSI認定ではないため）。問題はないが認識しておく。
- **OSS 前提のディレクトリ（SourceForge / FossHub 等）は非OSIを敬遠する場合がある**。フリーウェア/シェアウェアを扱う **Vector・窓の杜**、および自サイト・GitHub Releases・Product Hunt は問題なし。`UwView販売戦略.md` の海外サイト方針はこの点を踏まえて再調整済み。

## 実装チェックリスト（Mac 側）

- [ ] リポジトリ直下に `LICENSE`（PolyForm Internal Use 1.0.0）を設置。要約に商用問い合わせ先を記入。
- [ ] `README.md` / `README.en.md` のライセンス節を上記に差し替え（MIT表記を撤去）。
- [ ] `.csproj` の `PackageLicenseExpression` は使わず（SPDX未収載のため）`PackageLicenseFile` で `LICENSE` を指定、または `<PackageLicenseFile>LICENSE</PackageLicenseFile>`。
- [ ] Release ノート・配布アーカイブ同梱の LICENSE も更新。
- [ ] 商用ライセンスの問い合わせ導線（Issues テンプレ or メール）を用意。
```
