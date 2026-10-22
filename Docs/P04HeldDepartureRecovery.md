# P04 保留された抽象輸送の復旧

2026年10月10日。**製品0.4.16へ未統合の試作**。Coreの保存版9、Colonyの保存版1、module protocol2を維持する。製品の39件成功やP04全体の完成に加算しない。対象は元Core証がPreparedの抽象輸送Departingだけであり、実物食品移管、委任生活、全種類の途中操作への対応ではない。

## 解決する場面

旧0.4.15では、食品引き継ぎのArmed責任と抽象輸送の出発が重なると、出発途中の元証と予約が残る場合があった。この実例を隔離ゲームで発生させ、ネイティブSaveGameによる原入力を保全している。元入力を正常形に書き換える代わりに、その元証・予約・割当済み貨物を保留として読み直す。

保留中は元在庫100、予約10、割当済み未実体貨物0を維持する。引き継ぎ責任を解除しても自動出発せず、通常の出発APIの再呼出しも拒否する。新しい明示再開だけが元出発証を一度確定し、元在庫90＋元貨物10へ進める。貨物・証・恒久IDを作り直さない。

## 保存と検証の境界

新しいHeldフラグや表は保存しない。Colonyの既存DepartingとCoreの同じPrepared出発証から保留を導出する。元manifest、stock・shipment生成証、予約証、出発証、result ID、元在庫の数量と所属、予約の状態、割当貨物の空字段、registry、参照の所有、一意性、orphan、品目別総量を厳密に照合する。

Coreの`ValidateExistingBatch`は閉じた6種類の既存要求をscalar DTOで受け、fresh detached ledgerの全検証を一回行う。読み取りはID割当・Commit・native変更を行わず、Savingと該当module bootstrapでも同じ検証を使う。返却した配列を書き換えても元記録は変わらない。読み取れない記録を「準備なし」と扱わない。

constructorは保留の完全な事前照合を済ませてから、他の既にCommittedの四段階の参照だけを復元する。未対応Prepared Planning/Arriving/Cancellingは拒否する。Coreが明示的に復旧可能と判断する証を、Colonyが読み込み中に勝手にCommitすることはない。

## 通常画面と操作

拠点と輸送の一覧で、保留と理由を示す。元拠点か到着拠点の引き継ぎがArmedなら再開ボタンを無効にする。自分のColony actorの関連準備があれば「引き継ぎ準備を確認」から専用画面を開く。他actorの責任も出発を阻止するが、他actorの準備をこの画面で解除しない。

専用画面は拠点名と受取住民名を示し、確定済みarmを明示解除できる。解除causeは`overview-disarm/<元transfer ID>`に固定する。解除は2IDと1metadata証だけを追加し、荷物・住民・食品を動かさず、輸送は保留のまま残る。同じ解除の反復は元証を返し、別のcauseで既に解除済みなら拒否する。

Prepared/Applyingのarm/disarm証には「中断した操作を復旧」を用意している。押下時は元状態を読み直して既存証だけを再開する。通常の画面描画は証を確定しない。この復旧正例の実ゲーム試験は別の故障注入stageを準備中であり、現時点の画面表示成功と混同しない。

各操作は表示時の可否を信用して書き込まず、現在のworld、actor、輸送と準備の関連、証と全状態を再検査する。準備を解除した後は概要へ戻り、利用者が別操作で出発を再開する。

## 試作の固定対象

| 対象 | 固定値・変更 |
| --- | --- |
| Core | `AB36A6BAA4AEE444696564EB0DCD174F598300512C308532B274F10C73055170`。既存証の単体／一括read追加、CoreLedgerのpartial化 |
| 現在のColony | `E2BEBC89F44581F4E24D293731D6E4EA2C18D20DB503CCDD0816CCC924F3BAEC` |
| 追加Colony source | HeldDepartureCheckpoints、HeldDepartureResumePreview、HeldSupplyPreparationManagement、HeldSupplyPreparationWindowの4ファイル |
| 変更Colony source | ColonyService、ColonyMod、TransportCommandsの3ファイル。他20ファイルは凍結製品とSHA一致 |
| 言語 | English/Japaneseに保留5キー＋準備画面8キー。計13キー |
| producer | 試作では0.4.16のまま。新製品producerでの同一最終DLL検証は未実施 |

準備と固定sourceのmanifestは各work stageに置く。後から書き換えて過去のPASSを新しいバイナリに適用しない。

## 確認済みの範囲

| 系列 | 実ゲーム結果 | 限定 |
| --- | --- | --- |
| [Held-v2](Evidence/P04HeldDepartureV2/Result.md) | 基本／四DLC各4工程、8成功 | Colony9D38。旧実例の保存、責任解除、明示再開と別起動 |
| [不正入力](Evidence/P04HeldDepartureNegativeV2/Result.md) | 2成功、各14拒否 | ready-world detached constructor。破損cold保存28本の試験ではない |
| [混在](Evidence/P04HeldDepartureMixedV2/Result.md) | 2成功、各2順序＋16拒否 | 元保留と既にCommittedの四段階の混在 |
| [混在cold](Evidence/P04HeldDepartureMixedCold/Result.md) | 4成功 | 四つのColony stageだけを戻したcontrolled derivative。自然発生した四中断保存とは区別 |
| 概要のみ | [英語4](Evidence/P04HeldDepartureOverviewEnglish/Result.md)／[日本語4](Evidence/P04HeldDepartureOverviewJapanese/Result.md)成功 | ColonyACC11、準備解除は試験API経由 |
| 準備解除画面 | [英語4](Evidence/P04HeldSupplyRecoveryEnglish/Result.md)／[日本語4](Evidence/P04HeldSupplyRecoveryJapanese/Result.md)成功 | ColonyE2BE、関連準備の共通controller解除と別操作の出発、Saving・別起動 |
| [解除直後の保留保存](Evidence/P04HeldSupplyRecoveryCold/Result.md) | 基本／四DLC各3工程、6成功 | 共通UI解除直後の実Saving、別起動でHeld維持と明示再開、第三起動で全耐久照合 |
| [通常・旧保存回帰](Evidence/P04HeldRecoveryNormalRegression/Result.md) | 基本8／四DLC9、17成功 | 同stageの通常補給・維持費・P01移行・Orphan拒否、四DLCの六国家3module再読。新producerの製品回帰ではない |

各系列は独立した試験DLLと入力manifestを維持する。英日描画はUnity Repaintと画像で確認し、操作は同じUI controllerを試験から呼び出している。実マウスclickの成功とはしない。日本語は13キーを全文／名前入りrowで比較し、既存公式翻訳エラーは基本36／四DLC28の集合と件数の完全一致だけを許可する。

新UIで解除した直後のHeldを実保存し、別起動から明示再開する3工程も、基本／対象四DLCの各3工程を確認した。二つの出力保存はいずれも実APIとネイティブSaveGameで作成し、stage/proofを書き換えた入力ではない。

## 統合前に残す作業

- Prepared/Applying metadataを実APIのCommit前故障で作り、保存・明示復旧・出発を別系列で確認。試験用hookを含むCore DLLは通常Coreと区別する。
- 新producerの統合候補で必要な同一最終DLL回帰を実施する。現在stageの通常・旧保存17工程は採取済みであり、producer変更後のバイナリへ成功を引き継がない。
- 他actor・別world・古い画面、複数の関連準備、多数行／長い名前／小解像度、直接2〜3拠点と多数委任の負荷、通常マウス操作。

独立静的レビューは通常到達経路の新P1/P2確定指摘なし。Committed disarm証とArmed metadataがliveで分離する状況は、通常例外で自然発生を確認できていない故障注入課題として残す。Coreのcold復元可能性をlive UI復旧の保証に読み替えず、strict readやfresh write guardを緩めない。

この工程が完成しても、全量の実物食品補給、生活時計、飢餓・健康・治療、仕事と生産、共有倉庫は未完成である。次の機能の受入条件は[P04DelegatedLifePlan](P04DelegatedLifePlan.md)と[P04SupplyTransferImplementationGate](P04SupplyTransferImplementationGate.md)を維持する。

2026-10-10 PendingProof-v3完了: 基本/対象four各5工程=10実PASS。CoreEFC test-only/ColonyE2BE/HarnessC303固定。Evidence/P04HeldRecoveryPendingV3へ10工程Result/Attempt/raw抜粋/console/外console、8actualSave SHA、6PNG、seal/source/game/input/artifact/論理Coremanifest、故障diff、v2/v3レビュー、外wrapperとEvidenceHashesを採取。各NewSaveDataOutputs現在+保全SHAも一致。Prepared実Save6→Applying実Save7→cold既存二字段回復比較→元解除proofUI復旧+0新ID/証→HeldSave5→明示元出発Save4→第五起動exact。原rws編集なし。AB36通常stage/製品39/未来17へ成功件数混合なし。guardedRuntime2のみcleanupJSON、元SaveData/raw/source/manifest/証拠維持。
0.4.17候補work/p04-release17-candidate compile/seal完了・game未実施。Seal35DEAB49DC06827BE0BF658A1D1EABDFE51C45FAB62AAE7B99797E9F1EB2B6A2、CoreF58154CEC9E1C6CA90A7C26E12F556B8CED69B5067815F534C031BD6EC523930、Colony10978484420D4C3A56E6EC81E244BBBD84849D56FF3D650CA50D5FB3992FE62D、World6659C401ABE0FF95148E2466FD1AD65CF9239B6AB90262E54EFF6C879D9BBD72。Core14/Colony27/World4論理source一致、Version宣言のみ17、hookなし、schema9/module1/1/protocol2維持。独立review Source45/非DLL22/Tests28確認新P1/P2なし。
次のnormal17準備で実INH_SupplyIntentPreparedはschema8/producer14と判明。sealed helper17は意図どおりcurrent9入力のproducer15/16→17だけを扱い、Armはこのままでは拒否。候補sealを拡張せず、normalrunner独立テスト側で既存8→9 migrationのexact一stamp/一history/全他record/原物契約にactualnamedsource schema8producer14→9producer17限定flagを加える案をSolへ依頼。Root独立review後のみ実起動。current9や再保存17には全packexact/一producer字段契約維持。別Solは候補17固定3DLLのHeld UI-cold3工程×2準備中。いずれもgame未起動、旧stagePASSの使い回しなし。公式usage primary74%weekly59%ordinarytrue確認、reset/購入不使用、全枠まで継続。
