# Core8 食品補給意図：Prepare／Cancel — 0.4.14

2026-10-09。独立ステージの実装・コンパイルと追加必須P1/P2なしのレビューを経て、Rootが0.4.14製品へ統合し全モジュール・試験DLLのbuildに成功。Core8／Colony1／World1／protocol2。元の0.4.13両ZIPは全項目照合済み確定版として保持する。同じ最終製品DLLで37試験（基本18／四DLC19）が成功。新規24と既存13回帰で、Prepare/Cancel、同cause再実行、都市競合、取消後および未取消staleの保存再読、不正保存4種を確認。Evidence/P04DelegatedSupplyIntentsの39ファイルを保存した。食品の実物移管・通常UI公開・途中証の故障注入は未確認。

## 今回の機能

食品の全量 stack を将来の補給候補として記録し、明示的に取り消す。食品予約・確保・配達・共有倉庫・人口移動はしない。食品・Pawn・Caravan を生成、移動、分割、merge、消費、Destroy するコードは無い。Thing の deep/reference 保存も無い。

新規 Prepare は元 committed caravan store、現在の exclusive Core-held recipient、停止中の元 player Caravan／surface tile、唯一の元 source Pawn 所属、食品 owner、`wholeCount == food.stackCount`、元 Core binding／provider、custody／ration pending、city lock、既存 native Quote を ID 割当前に検査する。未登録 source を登録しない。未取消 intent の target entity／target native Pawn／source native Pawn／food native ID の重複を全 actor 横断で拒否する。source と別 intent recipient の交差も保守的に拒否する。

**Prepared は非予約。** 既存 ration、custody、通常の caravan 食事・腐敗・移動・出庫を止める hook を追加しない。原物の変化は動的 `Inspect` が stale 理由として返し、manifest を上書きしない。現在の照会が `None` でも、将来 Apply を許可する token ではない。

## 公開 API／固定名

```csharp
public enum RationSupplyIntentStage { Prepared, Cancelled }
public RationSupplyIntents(CoreWorldComponent owner)
public IEnumerable<RationSupplyIntentSnapshot> Snapshots { get; }
public RationSupplyIntentSnapshot Prepare(string actor, string cause,
    string entity, string city, Caravan caravan, Pawn sourcePawn,
    Thing food, int wholeCount)
public RationSupplyIntentSnapshot Cancel(string actor, string cancelCause, string id)
public RationSupplyIntentSnapshot ResumeProof(string actor, string id)
public RationSupplyIntentStatusSnapshot Inspect(string actor, string id)
internal RationSupplyIntentSnapshot[] ReadSnapshots(CoreLedger candidate)
internal void Restore(CoreLedger ledger, SaveHeader header)
internal void Validate(CoreLedger ledger, SaveHeader header)
internal void SynchronizeCounts()
internal bool IsWriting { get; }
```

`RationSupplyIntentSnapshot` の readonly 字段：`Id / Actor / Cause / ScopeId / EntityId / NativeId / CityId / StoreTransferId / CaravanId / SourceNativeId / SourceEntityId / SourceKind / FoodNativeId / DefName / StuffDef / PrepareOperationId / CancelCause / CancelOperationId / CancelReceiptId`、`Tile / WholeCount / HitPoints / PreparedTick`、`UnitNutrition / DailyDemand`、`Stage / PrepareProofStage / nullable CancelProofStage`。

`RationSupplyIntentStatusSnapshot` は `Intent / Problem / IsCurrent / CanCancel / CanResumeProof`。`IsCurrent` は保存時の固定 metadata と現在 native 観測の一致を示すだけ。`CanCancel` は第一証 Committed・まだ取消証無しの Prepared、`CanResumeProof` は Prepared／Applying の既存証のみ。NeedsReconciliation は勝手に再開しない。

`Snapshots` は active ready-world API。Colony bootstrap は Core owner の `ReadRationSupplyIntents(actor)` → `CheckCustodyRead(actor)` → `supplyIntents.ReadSnapshots(privateLedger)` を使う。低水準内部読取りの gate は owner に置き、native Prepare／Inspect／Cancel／Resume は ready Core8 のままにする。owner 用の members snippet は別 `.cs` に置いたが compilation unit ではない。

## 証と replay

既存 CoreLedger の `create / lease` だけを使う。新 op kind／新 op 表／native holder は追加しない。第一 proof cause prefix は **`supply-intent/`**、取消は **`supply-cancel/`**。第一 lease は custody city 参照＋固定 manifest digest、取消 lease は参照無し identity seal（scope、actor、cancel cause、第一 lease ID、manifest digest）。正常 Prepare は +2 Core IDs、正常 Cancel はさらに +2。cancel proof は city/community/source/recipient を stamp しないため、別の city pending があっても取り消せる。

同 actor＋prepare cause は固定 entity／city／caravan ID／source Pawn native ID／food native ID／明示 wholeCount が一致すると歴史 snapshot を返す。現在の食品が消費・腐敗・移動しても、旧 count を渡す replay は旧 manifest のまま。現在 count へ変更した request は矛盾として +0 拒否。HP／nutrition／day 等を replay 時に再取得して更新しない。取消済み prepare cause の replay も旧 Cancelled snapshot のままで、再開・再補給しない。

cancel cause は別 intent／prepare cause／既存 cancel cause へ再使用できない。取消の同 cause replay は +0・自動 Commit 無し。別 cancel cause を同 intent に付け直さない。原物が無くても歴史第一 proof＋元 committed store history が合えば取消できる。

## proof checkpoint と保存境界

通常は `NewOperationId → PrepareCreate → journal refs 固定 → Commit`。第一／取消とも **PrepareCreate が戻った直後、Commit 前に既存 op ID／result ID と対応 journal を記録**する。第一 pending では Stage Prepared、取消 pending でも Stage Prepared ＋ cancel cause/ref を保持し、snapshot の proof stage で区別する。Cancelled は両証 Committed が必須。

同 cause replay 自体は pending proof を Commit しない。明示 `ResumeProof(actor,id)` が既存 op の request/spec/result を照合して同じ op を Commit する。第一 proof pending は先に ResumeProof してから Cancel。取消 pending は ResumeProof で同じ取消証を確定する。新 ID を発行しない。Core の Applying→Prepared／RecoveryCount の既存復元契約は Root の Core recovery に任せる。

`Restore` だけ既存 proof の欠落参照と、Committed 取消証に対応する Stage を補完できる。load 自動 Commit・Cancel・native 補給はしない。`Snapshots / ReadSnapshots / Validate / Inspect` は journal を補完・正規化しない。`Validate` は format／count／scope／provider／manifest hash、元 committed store、全 proof spec/result、二重 ID／cause／未取消原物キー、両 prefix の逆 orphan 照合を行う。保存 journal と record は未知 XML field／重複 field を拒否し、Apply／moved／split／spent 等を受け入れない。

**未対応の割当窓**：NewOperationId 成功後、PrepareCreate が成功して対応 ref を保存する前に例外が起きると、Allocated operation entity が残り得る。その ID を消す／NextSequence を戻す処理は無い。この状態を正常な intent checkpoint として復元したとは主張しない。通常の拒否条件は全部この窓の前に置いた。メモリ不足や Core validation 故障など、この窓の故障注入を行うときは別の recovery 契約が必要。

`IsWriting` と ExposeData guard は in-method save を拒否する補助であり、Scribe_Deep／SaveGame の例外捕捉があるので save 全体の原子的拒否を保証しない。安定した pending proof へ戻った後の保存・再読を今回の checkpoint 対象とする。

## stale と歴史検証の分離

Validate／Restore は native 食品・source Pawn・Caravan の存在、現在の recipient residence を要求しない。旧 store transfer の immutable 歴史だけを照合する。出庫後に別 store で戻った recipient に自動付替えしない。既存 Core 個人／binding／PawnCustody が保存全体へ要求する別の契約は緩めない。

`Inspect` は `RecipientReleased / RecipientResidenceChanged / CaravanUnavailable / CaravanMoving / CaravanAway / SourcePawnUnavailable / SourceMovedOrIneligible / SourceBindingChanged / SourceUnavailable / SourceChanged / SourceNutritionChanged / RecipientDemandChanged` 等を導出する。欠落原因を食事／腐敗／正規処分と断定しない。食品が見つからなくても生成や補充をしない。第一／取消 pending や NeedsReconciliation は別 proof 理由を優先する。

## Core と Colony の責任分界

Core は Colony の physical XML へ依存しない。**Colony wrapper は全 stage の physical cargo history（source ID と cargo ID、journey supply 含む）、transport reservation、delegated site の登録、cause namespace の cross-API 使用を新 Prepare 前に検査する**。今回の新Prepareは開発用flag inh-supply-intentを必要とし、通常画面の実行機能はまだ公開しない。Core の native Quote が返るだけでは historic cargo を補給予定へ使ってよいとは限らない。

意図作成後の通常輸送は非予約の契約上許可する。後発のcargo履歴一致はInspectのstale理由にし、歴史意図のSave/Restoreを拒否しない。歴史証の改竄・orphanと、現在の資源変化を分離する。

Cancel／Resume wrapper は新 Prepare の freshness／setting／native source 検査を流用せず、歴史 proof と actor/cause の対応で呼ぶ。取消は任意の city pending の解消を待たせない。第一 proof pending の明示復旧自体は、その同 Core op の precondition を守る必要がある。

## Core8 移行／owner 統合

Root 所有の `CoreWorldComponent.Integrated.cs` が正本。snippet/helper はこれを上書きしない。旧 schema1〜7 は、元 header/module stamps のまま既存 ledger/custody/rations/module payload を先に厳密検査する。旧 schema に新 XML journal 要素／両 supply proof prefix が混入していれば拒否。検証後に同 scope／allocator の空 supply 表へ明示移行し header/stamp を8へ更新する。

schema8 は journal 必須。Deep 要素欠落・records 欠落・count/format 欠落を constructor の空表で補完しない。Core recovery後に `Restore(candidate,header)`、以後は無変更 `Validate`。loaded count を検査する前に SynchronizeCounts を呼ばない。新規 runtime 操作は必要な count のみ更新する。

Colony known receipt 表へ第一 ID と optional cancel receipt ID を加える。原 Pawn と legacy ration/spent/custody 証はそのまま。Core ledger の比較・保存参照は copy-on-write 後の `ledger.SavedState` を使い、古い `savedLedger` field を根拠にしない。

## 確認済み／残る試験

`CompileSupplyIntentStage.ps1` は current Core source とこの新 class を実ゲーム DLL 参照で別 `AssembliesAgentCheck/Inheritance.Core.SupplyIntentStage.dll` にだけコンパイルする。Core8 migration／owner／Colony wrapper の実行検証ではない。コンパイルは成功し、現行 Framework コンパイラと game DLL の既存 Span/ReadOnlySpan 参照警告だけを出した。

実ゲームでは通常 Prepare/Cancel/同 cause矛盾・全原物キー二重拒否、正常 stale save/reload、出庫/再入庫の旧 store 維持、source missing / city locked Cancel、第一／取消 Prepared/Applying checkpoint同ID再開、未知XML/count/metadata/proof/orphan改竄、新8必須表欠落・旧1〜7混入拒否、英日UIが非予約・未配達を示すことを Root が確認する。実物 Apply と食品保持量削減は次段階。
