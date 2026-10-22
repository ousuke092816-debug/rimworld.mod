# 使用済み食品の退役：native API 一次確認

2026-10-08。次工程向けの調査資料。**未実装・実ゲーム試験未実施**。0.4.11の製品ソース、DLL、試験、配布物、既存証拠は変更していない。[保持量削減の設計案](P04SpentWitnessRetention.md)を前提とする。

## 調査対象と方法

- RimWorld 1.6.4871 rev591 の `RimWorldWin64_Data/Managed/Assembly-CSharp.dll`。
- SHA256: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`。今回の読み取りで再確認した。
- 既存 `work/food-api-inspect.exe` による反射・IL、PowerShellからの反射による引数名・既定値・virtual属性・例外節・metadata tokenの型解決。native APIを**呼び出して破棄する試験はしていない**。
- 現在の `Source/Core/RationDebits.cs`、`CoreWorldComponent.cs`、`LedgerRecords.cs` と `Source/Colony/DelegatedMaintenanceService.cs` を読み取った。以下では確認事実と実装提案を分ける。

## 実DLLで確認した署名

```csharp
// Verse.Thing。ThingWithCompsはoverrideする。
public virtual void Destroy(DestroyMode mode = DestroyMode.Vanish);
public bool Destroyed { get; }
public bool Discarded { get; }
public bool Spawned { get; }
public ThingOwner holdingOwner;       // public field
public int stackCount;                // public field
public string GetUniqueLoadID();

// Verse.ThingOwner<T>。基底ThingOwnerのvirtual契約を実装する。
public bool Remove(Thing item);
public void ExposeData();

// Verse.ThingOwner / ThingOwnerUtility
public void Notify_ContainedItemDestroyed(Thing item);
public static bool ShouldAutoRemoveDestroyedThings(IThingHolder holder);

// 以下は実DLLのgeneric public static署名。ctorArgsはparams。
Scribe_Deep.Look<T>(ref T target, string label, params object[] ctorArgs);
Scribe_Deep.Look<T>(ref T target, bool saveDestroyedThings, string label,
                   params object[] ctorArgs);
Scribe_Collections.Look<T>(ref List<T> list, string label,
                          LookMode lookMode = LookMode.Undefined,
                          params object[] ctorArgs);
Scribe_Collections.Look<T>(ref List<T> list, string label, bool saveDestroyedThings,
                          LookMode lookMode = LookMode.Undefined,
                          params object[] ctorArgs);
GameDataSaveLoader.SaveGame(string fileName); // static void、成功boolなし
```

`DestroyMode`は `Vanish=0, WillReplace=1, KillFinalize=2, KillFinalizeLeavingsOnly=3, Deconstruct=4, FailConstruction=5, Cancel=6, Refund=7, QuestLogic=8`。`LoadSaveMode`は `Inactive=0, Saving=1, LoadingVars=2, ResolvingCrossRefs=3, PostLoadInit=4`。

## DestroyとRemoveの実際の順序

`Thing.Destroy`のILから確認した順序は次のとおり。

1. `def.destroyable`を検査する。破棄不可ならエラーログを出し、そのまま戻る。既にDestroyedでもエラーログを出して戻る。**例外が無いだけでは成功といえない**。
2. Styleの `Notify_ThingLost`、必要なDespawn／選択解除を行う。unspawned物品でもstyle等への通知は残る。
3. `mapIndexOrState=-2`にする。`Destroyed` getterは状態が `-2` または `-3` の場合trueを返す。defの条件により `Discard(false)` を呼ぶ。
4. spawnedだった場合だけleavings関連処理を行う。候補を未spawnの専用holder内食品に限定する理由になる。
5. `holdingOwner != null`なら、そのholderへ `Notify_ContainedItemDestroyed(this)` を呼ぶ。
6. reservation/designationを除去し、Pawn以外は `stackCount=0` にする。
7. `QuestLogic`以外ではquestのDestroyed signalを送る。KillFinalizeでは追加のKilled signalを送る。

`ThingWithComps.Destroy`は最初にMapHeldを取得し、**base `Thing.Destroy`の後に**各compの `PostDestroy(mode, map)` を順に呼ぶ。base内通知やcomp/Harmony callbackの例外は、Destroyed化前・holder除去前・数量ゼロ化前・全て完了後のいずれでも起き得る。今回のIL確認はHarmony等が適用された実行後挙動を保証するものではない。

`ThingOwner.Notify_ContainedItemDestroyed`は `ShouldAutoRemoveDestroyedThings(owner)` がtrueのとき `Remove(item)` を呼ぶ。例外型はmetadata tokenを解決して確認した **`Verse.Corpse` と `RimWorld.Planet.Caravan`**で、それらのholderだけfalseとなる。現在の `RationDebits` はどちらでもないので、通常のDestroyは専用holderから同じThingを自動除去する。

`ThingOwner<T>.Remove`はContainsを検査し、未所持ならfalse。所持していれば、`item.holdingOwner == this`のときnullへ変更し、innerListから削除し、`NotifyRemoved`を呼び、trueを返す。**Destroyed化・数量変更はしない**。NotifyRemovedにはholder通知と変更通知があり、削除後に例外が出る可能性を設計から排除できない。

実装案は、事前検査を済ませた**同じ元Thingをholder内に保持したまま** `Destroy(DestroyMode.Vanish)` に渡す方式。先にRemoveしてからDestroyする方式は、未所有のlive Thingを生み、途中例外で証と所有が離れるため初回には採らない。`ClearAndDestroyContents`、`RemoveAll`、stack吸収は行ごとの観測・証付けを飛ばすためbatch退役に使わない。

## 成功観測と不確定状態

Destroy前に、元object参照・native ID・def・stuff・HP・count・holderを保存する。成功条件の提案は、呼び出しが正常に戻り、同じobjectがDestroyed、unspawned、`holdingOwner == null`、元holderのContainsがfalse、通常食品のstackCountが0であること。破棄後のcountを使用量の根拠にせず、意図証で固定した破棄前manifestを使用する。

この条件を満たしても、外部callbackによる別物品生成等までnative API一個で検査できるわけではない。対象IDの残存・二重所有・収支は別の全体検証で確認する。Destroyed objectへの参照をbatchやcompact indexに永続保持せず、metadataへ置き換える。使用済みID文字列とCore証は消さない。

例外を捕捉した際に `Destroyed && holderに不在` でも、正常成功へ読み替えない。例外時の同object観測を不変のRetentionHold証に固定し、Unresolvedとして止めるか、保存検査を維持して独立した破棄前saveへ戻る。再生成、inventory返却、同じIDへの再Destroyは行わない。`Destroyed == false`でもRemove通知が済んだなどの中間状態があり得るため、例外時に安易に元holderへ戻して正常化しない。

## Scribeと保存排他で注意する事実

現在の `RationDebits.ExposeData` は `Scribe_Deep.Look(ref spent,"spent",this)` と `Scribe_Collections.Look(ref debits,"debits",LookMode.Deep)` を使用し、debitCount/spentCountを同期する。`ThingOwner<T>.ExposeData` は基底holder設定を保存し、innerListを **`saveDestroyedThings=true`** で保存する。LoadingVars／PostLoadInitでは各ThingのholdingOwnerを自身へ設定する。

PostLoadInitのinnerList整理はnullとInnerThingがnullのMinifiedThingを除く処理である。**Destroyed食品を全て自動削除する処理ではない**。保存フラグやnativeの掃除に「退役証なしの実物消失」を代行させない。

現Coreは二箇所で `RationMutation` 中の保存を拒否する。

- `CoreWorldComponent.ExposeData` のSaving分岐：guard → PawnCustody/Rations検査 → module保存。
- `RationDebits.ExposeData` のSaving分岐：guard → counts同期 → Deep保存。

しかし実DLLの **`Scribe_Deep.Look<T>`はIExposable.ExposeDataのSystem.Exceptionを捕捉し、`Exception while saving ...`をログへ出してExitNode後に戻る**。OutOfMemoryExceptionだけはこの節でrethrowする。さらに `GameDataSaveLoader.SaveGame`もSystem.Exceptionをログへ出してvoidで戻る。SafeSaverのファイル置換処理があっても、Deep層で既に捕捉された整合エラーを保存全体の失敗へ自動変換する保証はない。

従って、現guardはCore整合性に必要だが、**ExposeDataでthrowするだけでは保存ファイル全体の原子的拒否を保証できない**。次工程ではnative処理と適用証／保留証を確定する間の再入防止に加え、通常保存入口での事前拒否または保存対象を正常な安定状態だけへ限定する方法を決める。Scribe.modeがInactive以外の際に退役APIを開始しない。main threadの小batchとし、Scribeコールバックや別threadから実行しない。

試験ではSaveGameの戻りを成功根拠にしない。独立した保存名を使い、エラーログ、生成XMLのCore必須node・件数・証・退役表、別process再読み込み、元fixtureのhash保存まで確認する。guard故障試験には「ログだけ出て部分XMLが生成される」場合も含める。通常saveのhook選定と第三者save入口の扱いは**まだ未確認の実装課題**である。

## 現Rationsへの接続箇所

| 現在の箇所 | Core8で追加する契約案 |
|---|---|
| `RationDebitRecord.Hash()` / `AppliedIdentity()` | 旧canonical内容を維持。第一証digest、第二証ID、spent IDを変更しない |
| `CheckWitnesses(record, seen)` | Prepared／Appliedは従来のexact live witnessを必須にする。Committedだけ各行をLive／CertifiedDisposed／Unresolvedの一集合に解決する |
| `Validate` のwitnesses数とspent.Countの一致 | Live集合だけをnative holderと一対一照合。退役／不明の行は別journal・index・証との逆照合を追加する |
| `Apply` / `Commit` のCommitted replay | 現行はCheckCertificate後に同snapshotを返す。旧食品を再選択／再生成せず、履歴と退役表をglobal Validateで照合する経路を維持する |
| `CheckBefore` / `CheckAfter` | 未確定の日次食品の所有・元stack残量・栄養検査を弱めない。退役対象はCommitted限定 |
| `SynchronizeCounts` / `ExposeData` | live spentCountに加えbatch/line/index件数を保存。空欠落を自動補完しない。既存の食品deep保存を残し、退役metadataはThing参照ではなくDeep recordの値で保存する |
| `CoreWorldComponent` load/save検査 | Core8版・retention payload・全体検証・未確定native処理の保存排他を追加。Core7 strict検査完了後に空journalへ移行し、読み込みでDestroyしない |
| Colonyの日次維持費 | 退役APIを日次Tickから呼ばない。Stopped／Held、出庫済み個人、pending city lockと独立した明示操作にする |

新規責務の提案はCore内 `RationWitnessRetention`。元Rationsへのアクセスはinternalで限定し、Colonyがspent ThingOwnerを直接Remove/Destroyする公開経路を追加しない。API候補は `Prepare(actor,cause,固定対象manifest)`, `Apply(batchId)`, `Commit(batchId)`, `Resume(batchId)`。操作leaseはcityへのreference edgeを持たず、元city・個人・store・食品証をidentity hashへ固定する。これなら別residentのcity lock中でも保留制御の証を保存できる。

native Destroyを始める前に全対象の原証、second proof Committed、exact holder所有、metadata、batch未割当を検査する。意図証Preparedまではnativeを変えず、native区間から適用証またはRetentionHold証確定まで保存排他を保つ。既にAppliedのbatchは元objectへ戻らず、既存Core operation/result IDだけを復旧する。boolフラグのfinally解除だけで故障後を正常扱いにしない。保存を許すには、正常Applied/Committedまたは証付きUnresolvedの全体検証が必要になる。

Core lease証の確定失敗もnative破棄をrollbackできないので、削除済み行のmetadataと同じ未確定operationを保持する。新しいcause・新しいIDで成功証を作り直さず、元requestのPrepared／Applyingを検査して再開する。保持するのは小さい不変metadataで、退役したThingやcompの参照ではない。

## 次の実装前に確定する項目

1. Core8移行とLive／CertifiedDisposed／Unresolvedの保存契約、及び逆照合を先に実装する。
2. 消去意図・適用・確定・保留のCore operation/specと、途中失敗時の同一operation復旧順序を決める。
3. 通常保存の入口guardを選定し、Deep例外の捕捉を含む再入保存試験を実施する。API反射だけで原子的保存保証を宣言しない。
4. 基本ゲームとAnomaly以外の四DLCで、正常Destroy・Destroy前例外・Destroyed化後例外・holder除去後comp例外の独立fixtureを用意する。全targetID／収支／biology／旧証hashを追跡する。
5. 明示小batchでnative Thing/comp数とXML量を別々に測る。Core7履歴・個体・食品再生成なしでの移行と、0.4.11回帰を終えてから自動化を検討する。

