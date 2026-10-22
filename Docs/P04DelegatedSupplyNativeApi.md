# 委任住民への全量stack補給：native API確認

2026-10-09。**読取調査のみ・未実装・実ゲーム試験未実施**。対象は停止中の元CaravanにいるPawnのinventoryから、Core保管中の元住民Pawnのinventoryへ、選択した食品stackを全量移す補給。消費・分割・merge・共有倉庫・抽象在庫化は初回対象に含めない。

## 一次確認の範囲

実DLLは RimWorld **1.6.4871 rev591** の `RimWorldWin64_Data/Managed/Assembly-CSharp.dll`。今回再取得したSHA256は `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`。

既存 `work/food-api-inspect.exe`、同`.cs`、`food-api-transfer-il.txt`、`food-api-generic-add-il.txt`、`food-api-remove-il.txt`、`food-api-splitoff-il.txt`を読み、反射とILで再確認した。新出力は `work/delegated-supply-api-{inventory-notify,holder-notify,comp-splitoff,signature-confirmation,inventory-scribe}.txt`。元調査出力は上書きしていない。製品ソース・DLL・Tests・build・実ゲーム・既存証拠は変更していない。

以下の呼出し順は、このDLLの未patch ILの確認結果である。第三者MOD/Harmony適用後のcallbackを実行検証したものではない。native実装の複写はせず、署名と順序だけを記す。

## 使用できる署名

```csharp
// Verse.ThingOwner。ThingOwner<Thing>からも利用可能。
public bool TryTransferToContainer(Thing item, ThingOwner otherContainer,
    bool canMergeWithExistingStacks = true);
public int TryTransferToContainer(Thing item, ThingOwner otherContainer, int count,
    bool canMergeWithExistingStacks = true);
public int TryTransferToContainer(Thing item, ThingOwner otherContainer, int count,
    out Thing resultingTransferredItem, bool canMergeWithExistingStacks = true);

// ThingOwner<T>にはout Tのwrapperもある。
public bool TryAdd(Thing item, bool canMergeWithExistingStacks = true);
public bool Remove(Thing item);
public bool Contains(Thing item);
public bool CanAcceptAnyOf(Thing item, bool canMergeWithExistingStacks);
public event Action OnContentsChanged;

// Verse.Pawn_InventoryTracker
public Pawn pawn;
public ThingOwner<Thing> innerContainer;
public void Notify_ItemRemoved(Thing item);
public bool NotForSale(Thing item);
public void ExposeData();
```

`TryTransferToContainer`、generic `TryAdd`、`Remove`、holder `NotifyAdded/NotifyRemoved`、`Thing.SplitOff`、`ThingWithComps.SplitOff`、inventory `Notify_ItemRemoved`には、今回の反射で**例外処理節が0件**だった。nativeメソッド自身によるcallback例外のrollback保証はない。

## 全量でもTryTransferはSplitOffを呼ぶ

out付きint版の通常経路は、source.Contains、別container、宛先CanAcceptAnyOf、正数量、Map直下holderでないことを確認してから、`min(item.stackCount, count)`を求め、**virtual `item.SplitOff(amount)`**を呼ぶ。その結果を必要ならsource.Removeし、宛先.TryAddへ渡す。TryAddが正常にtrueを返した後でoutへ転送Thingを設定し、MapHeldのresource counterを更新し、転送ThingのstackCountを返す。

全量時の基底 `Thing.SplitOff`は、DeSpawnOrDeselect、元holder.Removeの後に**同じThingを返す**。ThingMakerを呼ぶのは部分分割側だけである。したがって、正常な全量経路ではIDとobjectを維持できるが、source側Remove通知は既にこの呼出し内で起きる。

さらに **`ThingWithComps.SplitOff`はbaseから戻ったThingへ各compのPostSplitOffを呼ぶ**。全量で戻りが自分自身でもこの通知を行う。全量補給を選んでも、TryTransferにはSplitOff callbackが残る。「新Thingを分割生成しない」と「SplitOffメソッドを呼ばない」は区別する必要がある。

宛先TryAddが正常にfalseを返す場合、nativeは戻り片が未所持・未Destroyed・正数量なら、全量時には元source.TryAdd(false)を試す。ここでのTryAdd戻り値は捨てられ、追加通知からの例外にも対応しない。**false戻り経路の回収試行を完全なrollback証として使えない**。例外で抜けた場合には、その回収分岐へ到達しない。

宛先追加後にNotifyやresource counterが例外を出した場合、既に移った元物品が宛先にあってもAPI全体は戻らない。outはTryAdd成功後まで設定されないため、例外時のout=nullも「移っていない」の証拠にならない。source==destinationのno-op分岐もあるため、補給側は異なるholderを明示検査する。

## 初回whole-onlyで選ぶ実装候補

**提案**：全量stackだけなら、Core側のmanifestと保存排他を設けた上で、元Thingを明示的に `source.Remove(original)` → `destination.TryAdd(original, false)` と移す候補を優先する。これはTryTransfer経由のSplitOff/PostSplitOffを避け、全量であることをコード経路でも表現できる。ただしRemove/TryAddの通知は残り、これを無故障・原子的と扱ってはいけない。

宛先に同defのstackがあってもfalseを渡す。generic TryAdd(false)はmergeループを飛ばし、capacity確認後にholdingOwnerを宛先へ設定し、innerListへ同objectを追加し、NotifyAddedの後にtrueを返す。RemoveはContainsを確認し、holdingOwnerをnullへ変更、innerList削除、NotifyRemovedの後にtrueを返す。どちらも通常経路でstackCount、def、stuff、HP、native IDを変更せず、split/absorb/destroyを行わない。

補給側の事前条件は、元sourceと宛先の実holder、同一object参照、native ID、数量>0、非Destroyed・非spawn、食品metadata、宛先受入可否を固定する。正常終了後には**同じobject・同じID・同じ数量・no merge・宛先の唯一所有・source不在**を現物で再検査する。戻りboolや転送数だけを成功証にしない。

## inventory通知と副作用

native `Pawn_InventoryTracker`は `IThingHolder` と `IExposable` を実装する。ctorは自身をownerとして `ThingOwner<Thing>`を作り、oneStackOnly=false、LookMode.Deep、removeContentsIfDestroyed=trueを指定する。確認したctor ILにはOnContentsChangedへの購読追加がない。しかしholderのevent自体はpublicなので、MOD等から購読できる。

generic holderの通知は、ownerが対応するgeneric holder-event interfaceを実装していればその通知を先に呼び、その後基底holder通知へ進む。現在のPawn_InventoryTrackerはその追加interfaceを実装していない。基底通知には次がある。

- **Remove後**：ownerがPawn_InventoryTrackerなら `Notify_ItemRemoved(item)`。同メソッドはprivateのitemsNotForSaleとunpackedCaravanItemsからitemを除き、必要ならunloadEverythingを再計算する。その後holder.OnContentsChangedを呼ぶ。
- **Add後**：条件に応じてFire attachmentの消去、designations除去、EverSeenByPlayer更新、holder種別の通知を行い、最後にOnContentsChangedを呼ぶ。player PawnのinventoryではEverSeenByPlayer更新の経路がある。

従って、same-ID食品が元holderに戻っただけでは、inventoryのNotForSale／unpacked／UnloadEverything等の状態まで完全復元されたといえない。正常移動に伴うcache更新と、失敗時rollbackを分ける。private cacheの反射書換えを製品の復旧APIとして採用する根拠は今回確認していない。

供給対象をplain foodに制限しても、OnContentsChanged・外部patchの任意の副作用を消せない。無attachment等の対象条件や、NotForSale／unpacked状態をどう扱うかは初回仕様で明示する。生体needや年齢、健康、技能を更新するAPIとして使わず、補給前後で凍結条件を検証する。ただし「全callbackが生体へ一切触れない」という保証はこの反射調査からは出せない。

## callback例外時にあり得る観測

| 発生箇所 | native ILの順序からあり得る状態 | 必要な扱い |
|---|---|---|
| 事前検査／Remove前 | 元holderに元stackが残る | 元manifestとの一致を確認。新しいstackや新IDへ差替えない |
| Removeの通知 | holder=null、source innerListから削除済み。source inventory cacheは一部／全部更新済み | 返値が無くても未所有状態を観測する。自動にPreparedへ戻さない |
| TryTransferの全量PostSplitOff | 同元Thingが未所有。comp通知の途中 | whole-onlyでも発生する。回復済みと推定しない |
| 宛先TryAddの通知 | 宛先holdingOwner／innerListは既に同元Thingへ変更済み | 例外を「未転送」とみなして再Addしない。宛先だけにあることを同objectで確認 |
| TryTransferのresource更新 | 転送とout設定は済んでいてもAPIが例外 | 戻りがないこととnative結果を区別する |
| 外部callbackの変更 | 数量変更、Destroyed化、別holder移動、別item生成等はMOD次第 | 未patch ILが正常時に行わない操作でも、異常観測として拒否・隔離する |

source/destination双方に元IDがある、双方から消えた、holdingOwnerとinnerListが食い違う、数量／metadataが変わった場合、欠落を成功証へ変換せずUnresolvedにする。Destroyed食品の再生成、同IDでの置換、数量の帳尻補充は行わない。例外後にnative状態だけが一致していても、通知が全て終わったという証拠にはならない。

## Scribeと保存guardの限界

inventory.ExposeDataはitemsNotForSale／unpackedCaravanItemsのreference一覧、innerContainerのDeep保存、unloadEverythingを保存する。holderはinnerListをsaveDestroyedThings=trueで保存する。未所有のlive Thingは両inventoryのDeep保存から外れるので、callback途中を許可して保存すると復元できない物品になり得る。

Scribe_Deep.LookはExposeDataのSystem.Exceptionをログへ出して捕捉し、GameDataSaveLoader.SaveGameもSystem.Exceptionをログへ出してvoidで戻る。詳細と例外節の一次確認は [使用済み食品native API資料](P04SpentWitnessNativeApi.md) を参照。**ExposeDataからthrowするだけで保存ファイル全体の原子的拒否を保証できない**。

現在のCore.RationMutation／ExposeData guardをそのまま補給の永続回復契約と扱わない。main threadの小batch、Scribe.mode==Inactiveでの開始、補給native変更中の再入防止、保存入口での事前guard、安定したjournal状態だけの保存が必要となる。失敗後finallyでboolを下げただけで普通のsaveを通さない。第三者保存入口まで含む原子的保存は未確認の課題である。

## 必要なCore journal：実装前の契約案

native API自体にCore永久ID・cause・replay・原入庫証の保持は無い。最低限、Core管理の独立した補給journalが必要になる。

1. **Prepared**：actor/cause、world scope、元city、住民entity/native IDと原store証、元Caravan IDとsource Pawn native ID、元stack ID、全量count、def/stuff/HP、両inventory holder identity、固定manifest digest、意図operation/result IDを保存。元stackはsource唯一所有、宛先不在。再実行で対象を選び直さない。
2. **Applied**：正常通知終了とsame-object/no-mergeの結果を現物検査し、同元stackの宛先唯一所有を固定した適用証を保存。適用証Core operationがPrepared／Applyingの途中なら同requestとResultIdだけを再開し、Remove/TryAddを繰り返さない。
3. **Committed**：意図・適用・確定の証を逆照合する。元隊列移動／消滅、住民出庫後でも履歴replayは証だけを読み、新供給を行わない。補給済みstackは後日消費・追加移送され得るので、完了済み古い証で現在の宛先残量を永久固定しない。後続操作とのID・数量履歴の整合契約は別途必要。
4. **Held／Unresolved**：例外時の元object観測、所在、数量・Destroyed、成功通知が確認できない段階を固定する。sourceにもdestinationにも属さない物品を保存可能にするなら、Core専用の明示holderと一対一journalを設計する。単にmetadataだけ保存してlive Thing参照を失わない。未知の副作用まで「元に戻せた」と宣言しない。

全量stackを一度供給するための原物移送証であり、Rationsの消費証やspent holderへ入れない。recipient inventoryがそのまま後日の日次維持費の実物食品になる。追加食料needシミュレーション、元個体再生成、抽象stock追加を行わない。

入出庫、輸送貨物、日次食料、同cityの未確定Core証との競合をnative変更前に検査する。新供給journalがPrepared／不明の間に、他の操作が元stackを消費／再移送しないロックも必要になる。現在のcity reference guardだけで全てのnative inventory競合を覆えるとはいえない。source Pawnが未登録の場合もあり得るため、補給のためだけに人物登録を増やすかどうかを勝手に決めず、native IDと元Caravanの原物関係を契約へ固定する。

callback失敗時の安全な同元object rollbackを実装する場合も、cache・通知副作用を含む保証範囲と故障後保存方式を先に定義する。今回の調査だけでは、任意callback後の完全rollbackを保証できない。意図証と適用証だけ用意して、例外を「たぶん移った／たぶん戻った」と自動確定する短縮実装にはしない。

## 後段に分ける部分SplitOff

部分分割ではThingMakerで新pieceを作り、元stack数を減らし、HP等をコピーし、ThingWithComps.PostSplitOffが追加で動く。新native ID、元ID／piece IDの二者、数量配分、callback途中のpiece所在、rollbackのabsorb／Destroyを全て証明する必要がある。**全量の調査結果から部分分割のID保持・例外復旧を保証しない**。初回whole-onlyでは数量選択を受け付けず、count==manifest.beforeCount以外を拒否する。

## 最小の実ゲーム試験案

- 同defの宛先stackが存在する状態で、全量originalを別stackとして移す。元ID、全量数、no merge、双方owner、食品総数を確認。
- Prepared／Applied／Committedの独立save/load、各replayで追加native移送・Core ID割当なし。後日の食品消費と旧補給replayの整合は別試験にする。
- source Remove前・Remove通知後・宛先Add通知後・適用証前／Core Applyingで故障させ、元object観測と保留契約を確認。Scribe捕捉による部分saveも調べる。
- 元Caravan Moving／Away、住民出庫、未確定Rations、native貨物予約、city lockで無変更拒否。fixtureは既存原物のみを使う。
- 基本ゲームとAnomaly以外の四DLCで、needs・bio age・health・skillsの補給前後不変を確認。実DLL/hash、元fixture、生成XML、ログを保持し、製品化前に通知故障と保存契約の限界を解消する。

