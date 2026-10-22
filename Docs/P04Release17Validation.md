# 0.4.17候補の検証計画

候補は `work/p04-release17-candidate` に独立保存。製品0.4.16と二つの配布ZIPは凍結維持。同じ候補DLLで予定した57工程の実ゲーム試験が成功した。系列別の採取と全体の最終照合を進めており、新しい製品snapshot・ZIPはまだ作成していない。過去stageや失敗した別環境の成功を、一つの全回帰成功数へ加算しない。

| 候補の固定製品 | SHA256 |
| --- | --- |
| Core | F58154CEC9E1C6CA90A7C26E12F556B8CED69B5067815F534C031BD6EC523930 |
| Colony | 10978484420D4C3A56E6EC81E244BBBD84849D56FF3D650CA50D5FB3992FE62D |
| World | 6659C401ABE0FF95148E2466FD1AD65CF9239B6AB90262E54EFF6C879D9BBD72 |

保存schema9、Colony1、World1、protocol2を維持。故障注入hookを含まない。新規の保存版情報は0.4.17。製品Source/Modsは全45論理sourceと非DLL資産を基準に照合済み。

| 系列 | 予定工程数 | 主な条件 | 現在地 |
| --- | --- | --- | --- |
| 通常・旧保存回帰 | 基本8/4DLC9、計17 | Header17、P01移行/再読、補給準備/解除/再読、生活精算再読、不正孤立記録拒否、4DLC六国家保存 | v4全17実PASS、Evidence/P04Release17NormalV4/Final17採取済み。v1/2失敗とv3未使用準備は別保全 |
| 競合防止回帰 | 各11、計22 | 抽象輸送4、旧保留2、実物輸送3、予約範囲2の各系列。元荷物・予約・住民・在庫総量・責任・時計を保持 | v6全22実PASS、Evidence/P04Release17SupplyV6/Full22採取済み。外部v6collectorで全source/input/current＋kept保存・raw・10branch完成証を照合。旧variantsは別保全 |
| 通常準備解除後の保存再開 | 各3、計6 | 概要→準備画面解除→Held実保存→別起動の明示出発保存→第三起動exact | 6実PASS。Evidence/P04Release17HeldColdへ採取済み。Runtimeだけ保全検査後に整理 |
| Held不正payload拒否 | 各1、計2 | 候補固定DLLで各14種以上の不正独立payloadを原世界無変更で拒否 | v3で2clean実PASS。v2の旧保存型欠落による実FAILは別保全・未加算。v3では保存の空の開発用componentに対応するconstructorだけを追加。各14例はdetached constructor試験であり、破損保存14回のcold起動ではない |
| 保存証の一括検証 | 各load/final、計4 | 6種Prepared正例、無変更、別起動再読、入力alias/異常形状拒否 | v2全4clean実PASS、per-config採取・完成証付きRuntime整理済み。新Additional-v3 final6はこの4とNegative-v3の2だけを結ぶ |
| 日本語UI | 各ui-load/ui-final/ui-resumed、計6 | 13キー全文、公式翻訳エラー基準exact、準備画面Repaint、解除直後保存・別起動出発・第三起動再読 | 全6clean実PASS、13キーと公式基準36/28全文exact、6PNG、4actual保存＋付属証、per-config/final6採取・完成証付きRuntime整理済み。実pointer試験とは区別 |

計画上の件数は成功件数ではない。新しい候補DLLを変更した場合は当該候補variantを保全し、新variantで必要な検証をやり直す。

current9の保存を読むテストでは、実入力名の単一Core headerと入力SHAを基準にして、0.4.15/16→17のproducer一字段だけを許可する。他header全字段、全ledger/custody/ration/intent/transfer記録、移行履歴、nativeとColony保存証拠はexact。候補で再保存した17→17は全packexact。

通常回帰の `INH_SupplyIntentPrepared` は実schema8/producer0.4.14。これをcurrent9用helperへ無条件に通さない。Arm専用の追加テストflagを設け、実入力名/単一header/schema8/producer14を照合し、既存の移行契約（8→9、一つのCore stamp変更、一つの移行履歴追加、他全字段/全record不変）を維持する独立テスト側adapterを用意する。候補の製品DLLとsealを変更しない。実ゲーム開始前にRootが差分を確認する。

全系列の起動はRootが直列実行する。試験用ゲーム設定とSaveDataはwork内のみ。工程別attempt/raw/console/出力と失敗例外を保全し、次工程は前工程PASSと現在/保全rws・sidecar SHA一致が必要。実mouseclick、Committed解除証とArmed journal間の例外、多数行・多数都市性能、委任生活/治療/生産、HAR完成素材はこの候補回帰だけで完成扱いにしない。

採取済みのPrepared/Applying故障復旧10工程は別test Coreによる結果であり、上記予定数や候補の成功へ合算しない。証拠は `Evidence/P04HeldRecoveryPendingV3`。


2026-10-10 Candidate17 HeldCold6完了: work/p04-release17-held-cold-validation同CoreF581/Colony109784/harness7DE3、基本ui-loadPID6852/ui-final39420/ui-resumed15144、four13940/37472/13008、各3の6actualResult全PASS。actualschema9producer16 ArmedMixedからUIcancel→HeldSave5、別起動17→17全packexact/再実行+0/普通Depart拒否→明示元証出発Save4、第三起動final。constructor/IDs/証/食品/個体/時計とoldallrecords比較、6PNGありRootfourPreparation目視正常。証拠collector・fourRuntimecleanupはSol準備中、未採取段階。製品39やAB36結果へ合算せず、17候補の新系列成功として限定。
ディスク不足回避で基本3PASS後base RuntimeだけRootがguarded削除。source/seal660inputs/7artifact/game・3Result/native/console/全保全新出力/currentactualrws+sidecarSHA確認、絶対path/祖先子reparse/rws拒否、planJSONとdelete後Completed=trueの別completionJSON(PlanSHA/Resultpins/runtimepins)を保存。元キャッシュは860MB級、原SaveData/raw/immutable/source/manifestを保持してfour再試験環境の容量を確保。現在C空き約684MB、次のゲームは残存fourRuntimeの採取後整理を先行。
normalv2はworking将来出力を除外してsource/test固定F009をcopy済み、rws ANDsidecar前工程samevariant/config/stepPASS＋After/Raw/current/kept SHA整合guard追加、最終seal/独立review待ち。supply22は別sealed未使用版を保全し、checkpointXMLのparse表現差確認修正を新supply-validation-v4へ作成中。frozenAbstractはdeclared InTransit→Departing一字段だけのcontrolled derivative、wholepayload/proofs/history/digests＋原payload戻したouterdocumentexact比較、未実ゲーム。旧variantを上書きしない。
