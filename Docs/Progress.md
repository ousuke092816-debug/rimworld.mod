# 継承圏 — 制作の現在地（2026年10月22日）

開発基準は **RimWorld1.7.4982 rev1280固定**、更新追従は完成後。製品 **0.4.17**、全体約20％（15〜25％）・P04約60％の従来見積もりを維持。試験件数や草案数から完成率を上げない。[全体工程](OverallProgress.md)／[固定基準](DevelopmentBaseline.md)。

## 今回完了したこと

**Royalty・Ideology・Biotech・Odyssey有効の実ゲームで、非空の補給準備・同原因再実行・競合操作拒否を確認し、Rootと独立担当の原本レビューを完了した。** Anomalyは無効。

- Native17484正常exit0、wrapper20276完全終了0。新Runtime2698を1コピーだけ用意し、元Runtime2696・通常設定/セーブ・12保全根・過去証拠を保持。両担当がNative後の現物も照合。
- 専用fixture3体をArmed保存前に生成し、元72体→75体。生成段階のallocator65→79と、Prepared作成79→81を区別。最終entity80/op40/Prepared1。
- 同原因replay2回・競合拒否5経路。Armed→Preparedは厳密な追加差分、Prepared→Replayは全Core・全75Pawn・全Caravanの完全一致。食品6個/HP50/元source所有者を保持し、実配送はしない。
- 最初の外部監査は既存community revision2→5で失敗。公開登録3操作の各revision・独立fingerprint・spec参照・commitを証明したV2監査を追加。Armed前だけの正確な変更として承認し、他の旧ledger全fieldは完全一致。元失敗・全保存・旧Verifier/Sealを保持し、ゲーム再実行・原本修復はしていない。
- 純粋試験は入力境界25項目と登録revision証明11項目。Build Attempt004成功、最初のcompile失敗と中間版も保全。

正式checkpoint：`work/rimworld17-four-dlc-fresh-prepared-stage/WarmPreparedCheckpoint-20261022.json` **784DAC9F…**。Root **570AEA3A…**／独立 **E1A5294B…**／外部 **58B93820…**／登録証明の独立レビュー **68905D45…**。

## 検証の限界

raw1873行（Ordinal405種類）とprivate全文copyを両担当が確認。ERROR0、WARN259（shutdown allocation247を含む）。描画やメモリー解放の正常性を意味しない。private原本はAppContainer閉鎖後に再読取できず、閉鎖前capturedOriginalと一致する封印済み全文copyで結合した。

生成前72Pawnの内容や全Colony/全世界の完全一致、全proof specificationの包括監査、cold読込、実配送、通常UI/HAR描画、DLC固有進行、旧1.6互換、製品統合と1.7正式対応宣言は未完。

## 次の作業

**`work/rimworld17-four-dlc-prepared-cold-stage/NextSession.md` から続ける。** 次stageは入力事実と読取契約のみ。Source/build/Runtime/Nativeは未実施。

- 今回のPrepared **4E7C9C7E…** を元bytesのまま別PIDで読み直す。scope0404ba4a、allocator81/entity80/op40、Prepared1、75Pawn、時計1/72500/72501。
- 保存型の旧2observerとwarm新observer、計3DLLを保持。別cold observer追加ならactive11→12。新生成・配送・新Prepared作成を無効にし、既存原因のreplayだけを許す。
- 入力→Before→Replayの全Core/Pawn/Caravanを厳密比較。全保存差分は別記録。差分が出ても元certificate/digestや原本を書き換えず初回失敗を保持。
- Source/build/静的Root・独立レビュー→Runtime1コピー→prepared現物レビュー→host0/先行wrapper0→Root AppContainer直列→wrapper全閉鎖/privateコピー→原本raw/XML/PEの正式レビューを維持する。

今回のwarmや完了済みDLCなし試験・空表移行matrixを再実行しない。詳細はwarm stageのNextSession.md。

## 保持した先行成果と制作方針

DLCなし取消cold **1305CAEB…**、Prepared stable cold **4A3E40C5…**、warm **F2F3E3D…**、空表Core9→10 matrix **7FDA4965…** はそのまま保全。旧1.6住民入りRun001〜004/certificate不一致、元warm→初回cold完全一致失敗は未解決記録を維持。

六文化HAR種族・建築・武器の個別導入、六国家と還元群、外交・地域・四段階都市・首都・占領/退避/奪還、六文化＋混成開始、定住・直接2〜3都市＋多数委任、約300遺伝子、外観M1〜M6男女Species-v10-stronger、Sol中心を維持。

## ローカルLLM

一括160件は処理終了、151草案の形式検査通過・9件Label format保留。worker停止中。製品未統合・実ゲーム未検証。今回の監査提案は人数/Armed基準の誤りで不採用、原文と採否をwarm stageに保存。正式レビューの代替にしない。[運用](LocalAssistance.md)／[一括依頼](LocalBulkAssignment.md)。

直前の引き継ぎ資料はDocs/Historyへ保存。通常設定・セーブ・過去ZIP・全旧証拠を削除していない。
今回の[確認済み制作報告](../../../work/rimworld17-four-dlc-fresh-prepared-stage/CompletionReport-Checked.md)はローカル草案をSolが原本と照合・修正したもの。草案と採否記録も保持。
