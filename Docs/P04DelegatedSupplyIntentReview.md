# Core8食品補給意図：Prepare／Cancel限定の独立契約レビュー

2026-10-09。**未実装の設計**。資料P04DelegatedSupplyReview／NativeApiと現行CoreWorldComponent、CoreLedger、RationDebits、PawnCustody、Colonyの貨物・予約経路を読取確認した。編集は本書のみ。製品・Tests・DLL・ゲーム・保存を変更していない。実ゲームはRootが直列で扱う。

## 推奨する初回範囲

Core8に独立した供給意図journalを作り、Prepare、現在状況の照会、明示Cancel、同要求replayだけを扱う。Apply／Remove／TryAdd／SplitOff／Destroy／専用食品holder／need更新を一切追加しない。選択数量はPrepare時の元stack全量で固定し、部分選択を受け付けない。

**今回は非予約の補給意図として実装する案を推奨する。** 未取消意図同士の重複は排他するが、食料を確保・配達した証とは呼ばない。通常Caravanの食事・腐敗や、個人出庫・隊列移動で意図は古くなり得る。初回に食料予約を正式に約束するなら、後述するCore／Colony全入口への排他を追加するまで公開しない。Apply前に改めて予約・原物・保存契約を閉じる必要がある。

## 必須P1／P2

### P1：正常な源物変化を保存破損にしない

元食品は通常Pawn inventoryに残る。immutable count／HP／def／IDは**Prepare時に見た要求**を証明し、現在食品が存在することの永久保証にはしない。Restore／Saveの証hash・shape検査と、現在の適用可能性検査を分ける。

Prepared意図のsource食品が食事・腐敗・結合・移動で変化しても、意図は保存可能である。現在の照会理由はSourceChanged／SourceUnavailable／CaravanMoving／CaravanAway／RecipientReleased等として導出し、正常な保存として復元する。原因が証明できない欠落を「食べた」「腐った」「正常処分」として確定しない。Cancelは現在食品やsourcePawnの存在を要求せず、原物に触れず責任だけを閉じる。

同様に、recipientの出庫・再入庫・死亡や元隊列消滅後も、旧入庫証は歴史参照として検査する。現在residentへ自動付替えせず、別入庫への供給は取消後に新causeで作る。Coreの既存個人／死亡／binding契約がその保存全体を拒否する別の異常まで、このjournalで正常化するものではない。

新意図Prepareは現在の原物一致・食品対応・元resident・隊列位置を厳密に検査する。staleな同cause replayは旧意図を返し、現在食品を選び直したり数量を更新したりしない。immutable metadataやCore証の改竄と、native現在値の自然変化を区別する。

### P1：証作成の途中状態をorphanへ落とさない

現行CoreのNewOperationId→PrepareCreate→Commitは単一の不可分APIではない。Commit後にjournalへ行を追加するだけだと、途中例外でCore op／leaseだけ残り、保存／loadのorphan検査で拒否される。

全事前条件をID割当前に確認する。PrepareCreateが作ったimmutable requestとResultIdをjournalに対応させてからCommitし、証が未確定の場合も同じop IDを明示再開できる保存状態を用意する。loadはrequest/spec/resultの照合と既存参照補完だけを行い、新ID割当・自動Commit・Cancelをしない。Core RecoverはPrepared／Applyingを検査しApplyingをPreparedへ正規化するだけという現契約を維持する。

少なくとも「意図証未確定」「Prepared意図証確定」「取消証未確定」「Cancelled証確定」を区別する。Cancelledは元意図証と取消証の両方Committedを要求する。取消証Prepared／Applyingは同じopの再開を許可し、別証へ置換しない。NeedsReconciliationは勝手にCommitせず保留する。意図第一証が未確定なら、その同opを確定してから明示Cancelする簡単な経路を候補とする。

NewOperationId後／PrepareCreate成立前の例外は、他APIにもある割当のみの難所である。割当IDを消してNextSequenceを戻す復旧はしない。今回ここまでsaveableにするならjournalへopIDを先に固定する追加の契約が必要。未対応なら故障点と限界を明示し、途中orphanを正常再開したと報告しない。通常の拒否条件でこの窓へ入らないことは必須である。

### P1：旧保存の検査前にheaderを8へ刷新しない

schema7のheader／module stampのまま既存ModulePayload、CoreLedger、PawnCustody、RationDebitsを厳密に検査した後、空の供給journalへ移行する。新stampやproducerで検査し直すだけでは、旧版の所有モジュールやデータ不一致を隠す可能性がある。

旧schema1〜6の既存移行を通る保存も、各版の必要条件を検査し、最後に空の新表を明示作成する。旧schemaに供給表／供給Core opが混入した場合は拒否する。旧版だから新XMLを読まないだけでは混入を見逃すので、予約した供給要素の存在検査とprefix op検査を設ける。旧保存にはその要素がないことを通常条件とする。

schema8は供給journal必須。LoadingVarsで欠けた表をconstructorの空表で補完してはいけない。旧版からの空表作成と、新版の欠落拒否は別経路にする。RationDebitsの旧Prepared原物検査・spent一対一証を緩めず、食品退役機能を混ぜない。

### P2：非予約意図と実際の排他を混同しない

今回の非予約案では、同actor／causeは永久に一意、未取消意図はtarget entity、source Pawn native ID、source food native IDごとに一つにする。source食料を二つの計画へ数えず、最初はsource Pawn単位の保守的な重複禁止でもよい。scopeとactorを含む鍵の責任を明記し、違うactorを使った重複で逃げられないよう原物の独占キーは全journalで検査する。

既存custody／ration／physical予約とcity lockは**新Prepare**の事前拒否条件とする。一方、この段階の意図を作っただけで既存維持費・出庫・通常食事を止めない案なら、そのことをUIとAPIで明示する。生じたstale意図を保存でき、取消できることが前提である。journalをPreparedというだけで「資源は保持されている」と表示しない。

予約方式を採る場合は、Core RationsのPrepare／Apply、PawnCustodyのPrepare／Apply／Commit、Colony source入庫、physical新予約・出発、帰路開始・復旧等へHasPendingSupplyを接続する必要がある。city Core証だけをロックしても、native source食事・腐敗は止まらない。途中cancelの復旧入口を閉じてプレイヤーが返還不能になる設計は避ける。実Applyがない今回、非予約案の方が既存生活への影響を狭くできる。

## 固定manifest案

意図のformat版、scope、actor／cause、target entity／native ID、city、元store transfer ID、元Caravan ID／surface tile、source Pawn native ID、任意の既存source Core ID、source holder種別、food native ID、def／stuff／HP／全量count、必要なら受取別単位栄養をsealする。

未登録sourceを補給意図のために人物登録しない。既登録source Core IDを記録するなら、その時点のnative／provider／kindとの一致をPrepare時に確認する。Core entity IDとnative load IDは別namespaceであり、文字列の見た目やnameを一意性の根拠にしない。Thingをdeep保存／referenceで拘束して、source食品を証物として複製保持しない。

manifest hashはlength-prefix、InvariantCulture、有限数、明示null、版prefixで固定する。actor／causeだけ一致しても、別recipient、別store、別source／food、別全量countを同じ要求として認めない。cancel causeとintent IDの組を固定し、同cancel causeを別意図へ再使用しない。

第一証は例えば`supply-intent/<cause>`、取消証は`supply-cancel/<cancelCause>`の別namespaceとする。取消identityに元意図ID／digest、actor／cancelCauseを含める。どちらも本文にcityを固定できる。特に取消証はcity／community等への参照を付けない案を推奨し、同都市に別の未確定食品証がある時も取消責任を確定できるようにする。実物移管をcity lockから迂回するものではない。

## PrepareとCancelの事前検査

### Prepare

1. active validated world／main thread／Scribe inactive／actor ownershipを確認。causeと要求shapeを検査し、既存同causeのexact replayを先に扱う。
2. targetが指定storeのdelegated resident、独占Core holder、対応需要／食品であり、別custody／rationの途中でないことを確認。
3. 同じ元player Caravanがsurface tileで停止し、source Pawnがその隊列の唯一のnative個体で、対象foodがsource inventoryの同じ非Destroyed物品であることを確認。
4. source／targetの活動中physical予約、source custody／ration、未取消供給意図重複、city pending referenceを検査。別providerのbindingは拒否。
5. 全physical履歴のsource／cargo ID、JourneySupply、到着・取消・帰路履歴との一致をColonyで新Prepareの事前条件として除外する。Core低水準APIの成功だけでcargo除外済みとは扱わない。Colonyの保存known/orphan検査は、供給第一／取消証と元入庫の歴史関係を逆照合する。現在のcargo履歴との一致を保存拒否条件にはしない。
6. 全量数とmetadataを固定し、食品やPawnを変更せず意図証を準備・確定する。前提拒否ではCore ID／NextSequence／payloadが無変更であること。

### Cancel

1. active validated world／actorと既存意図証・journalを確認。明示cancel要求のexact replayを先に扱う。
2. 未着手の意図であることを検査。今回Applyなしのstageなので、Applied／moved／spent／split ID等のフィールド混入は拒否する。
3. source食品の現在数量・HP・holder、現在Caravanの位置、現在recipient residencyを取消の必須条件にしない。
4. 元意図への固定した参照なし取消証を作り、同op復旧でCancelledへ閉じる。原食品へのRemove／TryAdd／Destroyや数量補正はない。
5. Cancelledのreplayはhistorical証検査だけ。欠落・腐敗を処分証へ変換せず、食品が残れば普通の在庫としてそのまま扱う。

## 保存検査とID一意性

Core8候補検査中はready専用APIを使わず、内部candidate ledger／custody検査用読取りを使用する。Core stamp8とmodule schema宣言、producer、MigrationSevenと旧各移行終点、ExposeData必須表を同時にそろえる。Colonyに新永続表を持たせないならColony1を維持できるが、供給第一／取消leaseをknown集合へ追加し、供給prefix全opからjournalへの逆照合が必要である。

journal件数、intent ID／op ID／cancel ID、cause鍵、元store関係、未取消の原物重複を検査する。CoreLeaseのkind/provider/status/revision、request/spec/hash/result、余分なownership／custody／bindingがないことをexactに確認する。未確定証はAllocated resultとimmutable requestを検査し、Committed証のActive resultと区別する。未参照prefix op、余分な供給表行、重複証・取消だけ存在する行を拒否する。

取消済みの過去意図はsource食品の現在生存を要求しない。旧食品metadataを元にThingを生成しない。Core8が過去intentを読めることは、元入庫を現在residentとして再設置してよいという意味ではない。

## 保存入口の限界と今回の完了条件

Prepare／Cancelはnative操作なしなので、Remove通知や未所有Thingの保存難所には入らない。しかしjournalとCore証の書込み、Core／Colony保存順、Scribeの例外捕捉は依然として存在する。ExposeDataのthrowだけでsave全体の原子的拒否を保証しない。安定した証途中を保存・再読できることと、native無変更を今回の完了条件にする。食品移管・不明原物のUnresolvedまで解決したと主張しない。

## 最低限の追加fixture

| ケース | 必須確認 |
|---|---|
| schema7正常・旧未確定食品証あり | 元header/stampで厳密検査後空表8、旧scope/ID/food/Pawn不変 |
| 旧1〜6、旧版へ供給XML/op混入、新8必須表欠落 | 正しい空移行と不正拒否を分離、元save不変 |
| 正常Prepare、同cause exact／別要求 | 原物無変更、一意証だけ、別要求はID割当前に拒否 |
| Prepared保存→通常食事・腐敗・結合・隊列移動→再読 | 保存正常、stale表示、metadata上書きなし、Cancel無補充 |
| recipient出庫・再入庫後 | 原store歴史維持、現在の別入庫へ付替えず、取消可能 |
| target/sourcePawn/foodの二重意図、既存予約/city pending | 意図重複・新PrepareをID割当前に拒否、取消は参照なしで可能 |
| source/cargo両ID・全貨物履歴一致 | cargoを書換えず新Prepare拒否、直接Core APIの範囲を明示 |
| 意図Prepare後、同じ食品を正常な後発輸送へ使用 | 輸送を意図で予約しない。保存／再読成功、Inspectで履歴一致または原物変化のstale理由、元intent証不変 |
| 第一証Prepare/Commit間・取消証Prepare/Commit間 | 同op/result再開、load新ID/自動Commitなし、原物無変更 |
| NewID後Prepare前の強制例外 | 対応した安全状態か未対応制約を正確に記録。ID巻戻しなし |
| 第一/取消表・証・ID・数量・store・source改竄 | natural staleと区別してstrict拒否、orphan逆照合 |
| Cancelled replay、city lock・source missing時の取消 | 二重証なし、原物・clock・需要・食品証を変更しない |
| 基本＋四DLC同DLL、日英の意図／取消画面 | 未配達・非予約の意味とstale理由を示し、成功を実補給と表示しない |

今回のAPI・journal成功は補給意図管理の完成であり、実物補給の完成ではない。原物移管へ進む際には別のApply／Applied／Committed／Unresolved、target消費との排他、保存全体の安全性、通知副作用を追加レビューする。

## 2026-10-09：統合ステージの独立読取レビュー

対象は、同じステージ内の`CoreWorldComponent.Integrated.cs`、`ColonyService.Integrated.cs`、`DelegatedMaintenanceService.Integrated.cs`、`DelegatedSupplyIntentService.cs`、`RationSupplyIntents.cs`。現行のCoreLedgerとColonyのpreview／return／physical／custodyの入口も照合した。製品への統合前の静的確認であり、このレビューではコンパイル・ゲーム・保存試験を行っていない。変更したのは本書のみである。

### cargo履歴の契約訂正

非予約のPrepared意図を作った後、同じ食品が正常な通常輸送に使用される可能性がある。この後発履歴を現在のsave/load検査で拒否すると、許可した通常操作が保存破損扱いになる。本書の旧Prepare項目5はこの二つを混同していたため、上記の通り訂正した。

新Prepareは既存の全cargo履歴を除外する。既存意図のInspectは、現在の履歴一致を`HistoricalCargo`／`HistoricalJourneySupply`等で表示する。一方、保存検査はintent本文とCore第一／取消証、元の確定入庫証、元地点の歴史関係を確認し、現在のcargo一致・現在のfood数量／owner・現在residentを要求しない。未取消意図を、既存輸送・出庫・維持費の原物予約へ追加しない。

### 確認した実装境界

- Core7の移行は元header／module stampsのままModulePayload、CoreLedger、PawnCustody、RationDebitsを検査してからMigrateSevenを行う。旧版に供給XML nodeまたは`supply-intent/`／`supply-cancel/`のCore cause prefixがあれば、移行前に拒否する。schema8のLoadingVarsでは供給componentをnullにし、missing deep nodeを空表へ自動補完しない。
- 新しい空表は旧schemaの明示移行でのみ作る。schema8のRestore／Validateはproducer更新より前に行う。Core Recoverは既存opのPrepared／Applying／NeedsReconciliation検査・正規化のみで、新たな供給証の作成やCommit、native操作をしない。
- Coreのpublic Snapshotsはready専用だが、bootstrap用ReadRationSupplyIntentsはprivate ledgerを渡すinternal ReadSnapshotsを使う。Colony復元中のknown集合追加がready専用getterを踏まないことを確認した。Snapshotは既存証と歴史入庫の読取検査だけを行う。
- 第一／取消証はPrepareCreate成立後、op ID／ResultIdとjournal対応を固定してからCommitする。第一証または取消証がPrepared／Applyingの場合、既存IDを明示ResumeProofでCommitする。NeedsReconciliationはValidateで存在を許可し、ResumeExistingで無断Commitを拒否する。Cancelled行には確定取消証が必要である。
- CheckLeaseはrequest specification、result lease、provider、kind、authority、quantity、revision、status、identity、custodyと余分な属性を確認する。CoreLedger側のrequest digest／operation shape検査と組み合わせる。prefix全opからjournalへの逆照合、actor／causeおよび証IDの一意性、未取消target／source／foodの重複検査がある。
- manifestはnative Thingをdeep/reference保存しない。current freshnessはInspectに分離され、source数量／HP／def、source binding、Caravan位置、recipient再入庫等の変化は理由として導出される。Cancel／ResumeProofは現在の原物を検査せず、食品を補充・移動・削除しない。
- Colonyの新Prepareは開発用引数とpreviewのsummary／選択行／全量countを事前確認する。同cause replayは既存Core要求との一致確認に進み、新previewで旧manifestを上書きしない。Cancel／Resumeは新Prepareの開発引数を要求しない。
- Colonyの新causeはRejectReturnCauseを通り、既存ration／maintenance／return／custody／physical／abstract transportとの衝突を割当前に拒否する。既存入口側はRejectMaintenanceCauseからRejectSupplyIntentCauseへ接続され、第一cause／取消causeの再使用を拒否する。保存側でも既存履歴とのcause衝突を検査する。
- Colony known集合には第一leaseと、存在すれば取消leaseを追加する。ValidateDelegatedSupplyIntentsには現在cargo履歴の保存拒否条件がない。Inspectだけが現在cargo履歴とsource／recipientのphysical予約を追加表示する。

### 必須修正と未実証範囲

この再読時点で、追加の必須P1／P2修正は見つかっていない。Rootが発見したbootstrap-read問題は、internal ReadSnapshotsとCoreの専用読取経路によりステージ上で修正されたことを確認した。

静的確認は、実際のScribe再読や故障注入の成功を証明しない。少なくとも通常Prepare／Cancel／exact replayの原物・clock・既存台帳不変、先述した第一／取消証のPrepared／Applying checkpointの同ID再開、schema7厳密移行、schema8表欠落拒否、通常食事・腐敗・source移動・recipient出庫後の保存とCancel、後発cargo使用後の保存とstale表示をRootの試験で確認する必要がある。

NewOperationId後／PrepareCreate成立前の割当窓はコメントにも未対応制約が残る。これは普通の事前拒否と同じ無変更保証ではなく、ID巻戻しやorphan自動削除で救済していない。Save内部のthrowだけで保存全体の原子性が保証されたとも扱わない。native Apply／移管／Destroyや使用済み食品の退役は、このステージの実装・承認範囲外である。

## 2026-10-09：SupplyIntentProbe 初回読取レビュー

ステージの`SupplyIntentProbe.cs`と、再利用する製品TestsのSupplyPreviewProbe／DelegatedMaintenanceProbeの比較関数を読んだ。製品への統合・試験実行はRootが行う。本レビューは製品、Probe、DLL、ゲーム、保存を変更していない。

### 必須修正P2：別process証拠に実行時参照番号が混入

初版のDurableCoreは、既存SupplyPreviewProbeのCanonicalをExcept経由で再利用していた。そのledger entitiesおよびoperation specificationにはnative binding Thingがあり、CanonicalはThingをItemへ渡す。ItemはRuntimeHelpers.GetHashCodeとholdingOwnerの実行時Identityを含む。そのため、PersistentNativeを専用の永続stampに分離しても、外部証拠のcore欄にprocess固有値が残り、正しい別起動Restoreが不一致になる。

in-process比較は既存Canonicalの厳密さを維持し、別process用に専用canonicalを設け、Thingはnative load IDと必要な永続値だけを比較する必要がある。循環参照、ThingOwner、Delegateにも実行時hashを持ち込まない。担当custody_uiとRootへ連絡後、修正ステージを再読して解消を確認した。詳細は後述する。

### 保存再読時の生物年齢

初版PersistentNativeはMaintenanceProbe.BiologyのAgeBiologicalTicksを完全一致要求するが、既存MaintenanceProbeのSameSavedBiologyはこの一欄に限り再読時+0〜3を明示許容している。既存の保存再現差に基づいた同じ限定許容、または実測でexact成立することの確認が必要である。ゲーム内のAPI前後年齢とclockは厳密不変を維持し、一般的な生物状態変更まで許容してはならない。担当とRootへ注意点を共有した。

### 初版で妥当と確認した境界と不足

Prepare／Cancelの差分検査は直接のCore ledger stateから取得し、旧entity／op完全記録を照合する。既存Core save countが通常writeのCopyで0へ戻る点を別途観測し、SaveSnapshotで後から隠さない。Colonyのwarm SaveSnapshotは最初の境界前に一度だけで、反復・Inspect・取消後の再基準化はない。

stale経路は既存原物food.Ingestedを明示実行し、元食品の減算またはDestroyを確認する。その前後でraw intentのdigest／数量／栄養／recipient等が完全一致することを検査し、native変化を除いたCore／Colony／新旧journal／clockの無変更を確認する。Cancelはこの通常消費後を新しい境界としてnative・旧journalの厳密無変更を検査する。通常消費の生体副作用を、製品補給APIの副作用なし証拠と混同しない。

正入力は元のMaintenanceResumed系の人／動物residentとoriginal Caravanを読み取り同定する。city-lockは別のDelegatedUiCityLock系であり、既存Applied食品とPrepared第二証による都市ロックを利用する。原fixtureのsidecarに対してscope／entity／native／元Caravanを確認する。専用別process証拠は保存名ごとにintent ID、scope、Core、Colony、native、clock、固定manifestを保持し、ロード比較で使用する。

六modeは通常Prepare／人animal取消／保存再読／通常消費stale／取消後再読／都市ロック拒否を扱う。第一・取消証のPrepared／Applying故障注入、後発cargo、source移動、recipient出庫、旧schema混入、新必須表欠落等まで実証するProbeではない。負のload試験はRootの別fixture、exact拒否ログと元source SHA照合が必要である。

### 修正ステージ再読：必須P2解消

担当が修正・ステージ再compile成功を報告したSupplyIntentProbeを再読した。DurableCoreは専用DurableCanonical／DurableExceptだけを使用する。Thingのbindingはnative load IDと永続値、ThingOwnerはCore custody／Core witness／元Pawn inventory／元Caravanの安定キーで表す。再参照はfield／indexの参照経路、Delegateはmethodと専用durable target経路を用いる。RuntimeHelpers.GetHashCodeはgraph訪問の比較用にのみ存在し、別process証拠へ出力されない。初回のruntime identity混入P2は解消した。

生物年齢は専用ages外部証拠へ分離された。Restoreは元個体集合を照合し、その欄だけ+0〜3を個体ごとのログ付きで検査する。他のbiology欄、food／Pawn／holderの永続値、clockはexactであり、in-processのNativeStampは既存の年齢を含めて厳密比較を維持する。paused通常更新2frame後にBindとwarm baselineを一度だけ行う。通常消費後にstale同causeの旧全量replayを行い、+0割当・manifest／原物無変更を確認する経路も追加された。

修正後の静的再読で追加の必須P1／P2は見つかっていない。この記載は実ゲーム六modeや負のload fixtureの成功を意味しない。Rootの実行結果、exact拒否理由、元source SHAと併せて最終試験範囲を判断する。

### 統合後の限定再レビュー（2026-10-09）

Rootは永続owner判定へMap.spawnedThingsの参照一致とMap.uniqueIDを追加した。第一create証がcityをStampしCommitがrevisionを一つ進める既存Core契約に合わせ、Prepareの差分試験でcityのrevisionだけ+1を許し、それ以外の全city字段・既存entity／opを厳密比較する。Cancel証は参照無しで、既存entity変更を許さない。

読込ではFinalizeInitが元serialized ledger countを検証した後、RecoverのCopyがraw save用countを0へ戻す。Supply journalはCopyされずrecordCountを保持する。Restore Probeの期待値をこの既存仕様へ修正した。製品DLLは変更せず、失敗を消すためのSaveSnapshot正規化も追加していない。food_contract_reviewがこれら三点を現行Coreと照合し、追加必須修正なしを報告した。途中第一／取消proofの故障注入・再開は引き続き別の未検証範囲である。
