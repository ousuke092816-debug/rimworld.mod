# 委任個人への食品補給：独立設計レビュー

2026-10-09。**未実装の必要条件**。P04DelegatedLifePlan.mdのL1を、現行PhysicalTransport／DelegatedReturn、Core RationDebits、PawnCustody、Core保存入口に照らして読取レビューした。編集は本書だけ。製品・試験・DLL・ゲーム・既存証拠は変更していない。

## 結論と先に出せる最小範囲

食品を元隊列inventoryに残すPrepared補給は、凍結した個人inventoryを使う既存RationDebitと同じ現物検査にできない。通常の食事、腐敗、運搬・stack結合、隊列移動によって要求が自然に古くなる。元数量やHPとの不一致をRestoreの破損判定にすると、正常な進行後の保存を読めなくする。

したがって、**今すぐの実物移管より、読み取り専用の補給previewを先行する案を推奨する**。以下の状態・証・保存版・競合契約が未充足のうちは、previewの先へ進めない。全量stack firstは分割の問題を減らすが、Preparedの自然変化、未確定移管の二度適用、保存全体の原子性を解決するものではない。

previewは同じ元隊列が現地で停止中か、対応する食品と供給元同行者、その現在数量、対象個人が今受け取れるか、食べ手別栄養を照会する。貨物・予約・未確定処理を除外し、可能な全量stackを表示する。個人登録・Core ID割当・予約・新証・永続表の追加・食料移動・維持費再開を行わない。「現在の候補」であり予約や配達完了ではないと表示する。次の表示では変わり得る値を、クリック時の確定manifestとして扱わない。

## 現行契約から引き継ぐものと、引き継げないもの

- Core RationDebitsは元入庫証と個人を固定し、Prepared／Appliedでは原物と数量・metadataが一致することを要求する。これは対象Pawnが凍結Core holderにある現行会計の条件であり、通常tickを受けるsource Caravanへそのまま移植しない。
- Committed食品証は、日次控除後に元stackがさらに減っても過去残量へ戻さない。補給もCommitted後の現在残量や現在holderを歴史的確定の条件にしない。
- PhysicalCaravanAdapterは元物品・分割片を使い、`TryAdd(...,false)`で結合を避ける。既存物品を作り直さない方針と原物rollbackを参考にするが、出発時のmap在庫と補給Preparedの動的なCaravan在庫は別の条件である。
- PhysicalTransportとDelegatedReturnはcargoNativeIdを含むimmutable manifestと帰還時の貨物照合を持つ。到着済みを理由に、その食品を利用可能な補給へ変更しない。JourneySupply、損失受容、食品退役の証も補給取消の代替にしない。

初回は既存physical cargoのsourceNativeId／cargoNativeIdに該当する食品を、完了便を含め保守的に除外する。元隊列の未予約の個人食品だけを扱う。到着貨物の正式な受入・消費は別の証と契約が必要である。

## 歴史的証と現在の適用可能性を分離する

第一意図証にはactor／cause、元入庫証、target entity/native、city、元Caravan ID/tile、source Pawn native IDとholder種別、source food ID、元def/stuff/HP/数量、選択数量、対応方式の版を固定する。栄養を固定するなら、その食品と受取個体から測定した値であることを示す。受取生体のneedsを改善した証ではない。

同causeの違う食品・数量・受取個体への再利用は拒否する。意図がstaleになってもmanifestを新しい数量へ上書きしない。新たな食品を選ぶ場合は元意図の取消後に新しいcauseで作る。

**Restore／保存の不変検査**は意図hash、Core証のactor/spec/result、件数・一意性、元入庫証との関係、操作履歴の順序、段階ごとの所有責任を確認する。**Apply事前検査**は現在のsource原物・quantity・HP・食品適格性、現在の隊列位置、target保管、競合を確認する。Preparedの源物が今一致しないことだけでは、前者を不正保存として拒否しない。

未着手Preparedの現在食品が欠落・数量減少・HP変化・別holderになった場合、SourceChanged／Unavailable／CaravanAwayなどの適用不能理由を表示し、補給しない。原因を証明できない限り、「食べた」「腐った」と断定する確定証を作らない。現在の観測理由は表示用に導出可能だが、正常Cancelledや正当Transferredへ自動昇格させない。Core不変証の改竄、重複ID、途中適用で説明できない両側所有などは引き続き拒否・保留する。

## 全量stack firstの状態契約案

| 状態 | 保存・明示操作の契約 | 自然変化の扱い |
|---|---|---|
| Prepared／未着手 | 元食品はまだsource側。補給計画の責任だけを保持し、現物を移した証はない | sourceの食事・腐敗・移動でApply不能になっても、意図証を維持して保存可能 |
| Preparedの明示Cancel | 未着手であることを履歴・適用進捗で確認し、取消証を元意図へ固定。現物操作なし。取消後は補給排他を解除 | 消えた食品を戻さない。残った食品をtargetや別holderへ移さない |
| Applying／移管途中 | 同一Thingがsource、移管専用holder、targetのどこにあるかを進捗と原物で排他的に照合。native実行区間中は保存再入を防ぐ | 通常の源物staleと混同しない。位置不明やcallback例外は専用Unresolvedで扱う |
| Applied／食品到着・第二証未確定 | 全量の同じID・数量をtarget inventoryへ渡した根拠と、既存第二Core opを保存。targetの消費・出庫をブロックし、明示Resumeは証の確定だけ | 元sourceの残りinventoryや隊列の後日の変化は、移管結果の再検査条件にしない |
| Committed | 受取IDと数量、第一・第二証、移管時結果を歴史として検査。同cause／id replayは追加物品・分割・返却・栄養加算なし | target維持費による消費・分割、出庫、通常食事で食品が減る／消えることを許す |
| Cancelled | 取消証と未適用履歴を固定。元sourceとtargetの現在食品を要求しない。replayは無操作 | 取消食品は通常native在庫のまま。消費や腐敗を食品退役の正常処分証に変換しない |
| Unresolved | 元意図・進捗・観測された原物を保持し、正常なApplied／Committed／Cancelledと区別して保存可能な契約を先に閉じる | 自動補充・再生成・再転送・再Destroy・欠落だけの取消成功は禁止 |

単なるPreparedフラグだけでは「一度も移していない」の証明にならない。native移管より先に適用開始・進捗を記録する順序と、保存入口が途中状態を部分保存しない根拠が必要である。既にtargetへ渡した可能性がある場合は、Preparedの取消として無操作完了にしない。

全量移管はsourceOwnerから同じThingを取り出し、targetOwnerへ結合なしで受け渡す案とする。失敗時はその原Thingだけを安全な元holderへ戻す。移管専用Core holderを使う場合は新しい保存責任・child holder検査を追加する。専用holderにあることは配達完了ではない。foreign ownerからの奪回や、失った物品を似た食品で置換するrollbackは禁止する。

最初は一人／一頭・一種類の一stack・全量に限定する。通常ゲームの源物変化を許すために、少なくなった現stackを勝手に「残り全部」として補給しない。再選択は新意図で行う。

## 部分stackは後続に分ける

部分移管はsource before/after、使用数量、実際のsplit ID、source／移管holder／targetの進捗を固定する。source inventoryは通常tickを受けるため、適用完了後未確定の元stack残数を永久にexactで要求できない。移管時の不変第二証と、target受取片の排他を根拠に確定し、元stackの後日の食事・腐敗は別のnative変化とする。

SplitOff失敗・移管先TryAdd失敗・rollback失敗・ID発行後の証失敗を個別に試験する。取得済みsplitを再生成せず、再実行で新splitを増やさない。元stackとsplitのmetadata、rotなどのcomp状態を確認する。全量版の証から分割版への自動変更はしない。

## 排他はCoreのnative変更入口にも必要

| 対象 | 最低限閉じる入口 |
|---|---|
| target個人／元入庫 | 同個人の別補給、未確定食品debit、custody出庫／新入庫、活動中physical移動。補給Appliedが確定するまで通常維持費tickから控除しない |
| source Pawn | 同Pawnの別補給、custody入庫、Colonyの輸送・帰路の開始。元Pawnを勝手に登録せず、登録済みなら永久bindingとnativeを照合 |
| source食品 | 同native IDの競合補給、physical cargo予約／受入、他のColony物品移管。modの論理予約で通常Caravanの食事・腐敗が止まると仮定しない |
| city | 既存HasPendingReferenceを割当／native変更前に検査。補給側の途中責任も各入口が照会し、certificateがCommittedだから排他なしとしない |
| sourceCaravan | 同じ実在player隊列・表面tile・停止状態をApply時に再確認。元隊列が動いたら保留。別隊列へ自動retargetしない |

画面でボタンを無効にするだけでは不足する。Core Rations.Prepare／Apply、PawnCustody.Prepare／Apply／Commitと、Colonyのsource入庫・transport／return入口に同じ競合を接続する。維持費は補給待ちとしてHoldにできるが、既存未確定debitを破棄して補給を先行しない。原物/source変更でPending補給が取消された後も、既存Held維持費は勝手に再開しない。

既存pending Core opと補給controlを同じcity参照で重ねると、保留・取消証までcity lockに阻まれる可能性がある。保留／取消証はcityをidentityに固定する参照なしcontrolを候補とするが、ロックを迂回して食品を移すものではない。Appliedの第二証Prepared／Applyingは同じrequest/spec/resultを検査し、明示Resumeで同じopを確定する。Core Recoverが自動Commitしない現行契約を維持する。

## 保存版と所有責任

previewだけならCore7／Colony1の永続表を変えずに実装できる。補給の不変意図・実物移管・第二証・取消・UnresolvedをCoreが所有する場合は、**専用のCore保存版を上げる必要がある**。版番号8を採用するかはRootが他工程との順序から決定する。使用済み食品退役のCore8案を、そのまま実装済みとして同居させない。

Coreはnative所有、供給journal、移管receipt、競合、candidate段階の検査を持つ。Colonyは食品選択方針・予約貨物除外・画面・維持費との接続を持つ。Core内の供給journalだけで完結しColonyが新しい永続状態を持たないならColony1維持を検討できるが、known/orphan検査とactor-access読取は必要。Colonyにも独立表・controlを保存するならpayload版と必須表／件数の移行を明示する。

旧Core7＋Colony1を先に既存規則で厳密検査し、その後に空の新journalへ移行する。scope／NextSequence／元Pawn・食品・入庫・食品証のIDとhashを維持し、loadで補給・新ID割当・原物生成をしない。新schemaの必須journal欠落と旧schemaへ新op混入は拒否する。新しいPreparedの自然staleを認めるために、旧RationDebitsの凍結対象検査を緩めない。

Committed補給の歴史検査は、後日食品が既存Rationのspent holderへ移ったことを排他的に認める。供給receiptが永久に元食品を専用holderへ残すと、その食品を食べられない。従って食品実物を永久証として保管する既存spent方式を転用せず、移管時の結果を不変証で固定する。後日欠落を処分扱いへ変換しない。

## 保存全体の原子性を公開条件に含める

P04DelegatedLifePlanのnative調査ではScribe_Deep.LookがExposeDataの通常例外を捕捉し、SaveGameもvoidで例外を記録して戻る。CoreWorldComponentのmutation guardによるthrowだけで、全componentを含む保存失敗や前回正常save保全を保証しない。

可逆的な全量移管でも、source／target片側だけの保存、適用済みなのにPreparedのjournal保存、結果証なしのロードを防がなければ二重補給や永久保存拒否へつながる。保存開始前の整合検査、一つのsnapshot、native移管中の再入拒否、別componentのScribe例外、flush／close／置換の失敗を検証する。Game.Save成功はvoidが返ったことだけで判断せず、生成されたsaveの独立process読込と不変検査で確認する。

callbackで所有が不明になった場合に、Unresolvedの保存と復旧入口を保証できないうちは通常の補給Applyを公開しない。取消は未着手の責任解放であり、移管の結果不明を正常化する逃げ道ではない。

## 追加試験行列

| ID | fixture／操作 | 必須結果 |
|---|---|---|
| Q01 | preview反復、設定off、移動／別地点／元隊列不在 | 元個人・食品・ID allocator・payload無変更、理由表示 |
| Q02 | 通常食品／corpse／drug／outcome／食事不適格 | 既存食品方針に沿う候補のみ。特殊gene資源を生活対応済みとしない |
| Q03 | 完了／活動中cargo、journey supply、帰路予約 | 既存manifest／損失証を変えず補給から除外 |
| S01 | 全量Prepared保存、解除して通常食事で数量減少 | load成功、SourceChanged保留、Apply無移動、Cancel無補充 |
| S02 | Prepared後の腐敗HP変化／消失 | save/load可能、正常Disposed証なし、選び直しは新cause |
| S03 | Prepared後の結合・別holder移動・Caravan離脱 | 不変意図は維持、別stack／holderへretargetしない、未着手取消のみ |
| S04 | Prepared後のsource Pawn死亡／隊列消滅 | 適用不能を表示、原物や代替隊列を新生成せず、供給意図の取消保存を検証 |
| A01 | 全量Apply／Applied保存／同op Resume | 同Thing ID／数量が一度だけtargetへ移る。第二証再開はnative無変更 |
| A02 | Apply後未確定でmaintenance tick／出庫を要求 | 食品消費・Pawn移動・追加証割当前に拒否、補給の復旧を案内 |
| H01 | Committed補給→日次控除→次日控除→補給replay | 旧数量／holderへ戻さず、移管・食料・請求が重複しない |
| H02 | Committed→出庫→通常食事で消失→replay | 過去証は有効、食品生成・返却なし、原物endpointを要求しない |
| C01 | stale／正常Preparedの取消、同cause反復 | 対象原物無操作、取消証一回。cause別要求へ再使用拒否 |
| C02 | Applying／Appliedを取消要求 | 未着手取消として成功しない。原物進捗に応じた復旧のみ |
| X01 | 同target／sourcePawn／foodの二重補給 | 複数意図を重ねず、新ID割当前に拒否 |
| X02 | city lock／未確定ration／custody／physical／return競合 | 表示とCore/Colony実命令が一致、食品・ID・所有無変更 |
| F01 | Remove前／専用holder中／TryAdd後の停止 | 各段階が独立saveで排他的所有。再開で一回だけ移管 |
| F02 | TryAdd失敗／rollback失敗／foreign owner | 同じ元物品以外を戻さず、不明状態はUnresolved、成功偽装なし |
| F03 | 第二証NewID／Prepare／Commit前後の停止 | 既存opを再開。新証への置換・native再適用なし、orphanを拒否 |
| F04 | 保存開始／Core/別component Scribe／flush/close/置換例外 | 前回正常save保全と再読込、未完成saveを成功扱いにしない |
| M01 | 旧Core7＋Colony1、食品・原物・未確定debitあり | 空の供給表へ厳密移行、native/ID mutationなし |
| M02 | 新版必須表欠落／旧版へ供給op混入／証改竄 | 通常staleと区別して拒否、元save無変更 |
| P01 | 部分SplitOff→適用→源stack自然消費→replay | 新split一回、元sourceの後日残数を旧数へ戻さない（後続段階のみ） |
| U01 | 日英描画と通常マウス操作一巡 | source名／食品名／今の数量／受取名／stale・取消・未確定理由が見える |

Preparedの自然変化試験は通常tickと実際のnative食事／腐敗を使い、食品XMLの欠落編集を自然現象の代わりにしない。改竄fixtureは別に保持する。元save、最終0.4.12証拠、既存配布ZIPを上書きしない。基本ゲームで前提を閉じた後、四DLCを同じ最終DLLで確認する。

previewの完成は補給の完成ではない。実物移管を公開する判定は、未着手の自然staleが正常に保存・取消でき、途中移管の排他・同op復旧・Unresolved保存・Committed歴史replay・保存全体の境界を試験できた時点とする。
