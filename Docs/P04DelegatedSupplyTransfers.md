# Core9 食品移管準備・解除証 — 0.4.15

2026-10-09。Core9／Colony1／World1／protocol2。準備した食品補給意図を実物移管へ進める前に、対象都市・元個体・元食品に紐づく責任を保存する。食品・Pawnの生成、移動、分割、結合、消費、深い保存は追加していない。通常の食事・腐敗・キャラバン移動を止める物理予約ではない。新Beginは開発flag `inh-supply-transfer` 限定。

## 保存と操作

`RationSupplyTransfers` はCore所有のmetadata journal。状態は `Armed` と `Cancelled` のみ。元intent全manifest、元intentのdigest、Core scope、actor/cause、arm/disarm proofを固定する。元のCore-held recipient、元入庫、source native、任意source entity、食品native ID、全量、HP、栄養、日需要、都市とtileを保存する。

- `Begin(actor,cause,intentId)`：元intentがcommitted・未取消・現在の原物と一致すること、既存city proofと全actorの活動責任を割当前に検査。+2 IDs／+1 create proof、city revision +1。食品は動かさない。
- `CancelBeforeMove(actor,cause,id)`：armがcommittedなら、原物が古くなった場合にも参照なしの解除証を作る。+2 IDs／+1 proof、旧city/entity不変。元intentはPreparedのまま。intent取消は別の明示操作。
- `ResumeProof(actor,id)`：既存Prepared／Applying proofだけを同IDで確定し、+0 IDs。未確定disarmは確定するまでArmed責任を保持する。NeedsReconciliationを勝手に確定しない。
- `HasActiveForIntent/City/Entity/Native/Food`：全actorを横断する純粋照会。source／recipientの役割を交差照合する。壊れた表を空とみなさない。

Colonyは `BeginDelegatedSupplyTransfer`、`DisarmDelegatedSupplyTransfer`、`ResumeDelegatedSupplyTransferProof`、`InspectDelegatedSupplyTransfer` を提供する。Inspectは現在の原物一致、問題、解除可能、既存証再開可能を返す。自身の責任を理由に自身の食品候補を誤ってstaleにしない。元intent・維持費・custody・輸送等とcaller causeの再利用を拒否する。厳密な同cause過去replayは現在原物を再選択せず、追加IDや暗黙Commitを行わない。

## 競合処理

CoreLedgerの新Prepare／Commit、custodyのPrepare／ApplyNative／Commit、rationのPrepare／Apply／Commit、補給intentの新Prepare／Cancelへ責任照合を接続。Colonyでは人物登録より前、native出発／到着より前、返送・維持費の処理前に確認する。Core proof自身の除外は実際のjournal・immutable request・resultを照合し、prefixだけで許可しない。

競合する輸送・返送の自動処理はその便を待機させ、例外で通常更新を止めたりNeedsAttentionに書き換えたりしない。読み込み時の競合便についても既存証参照・stageを自動補完しない。bootstrap中はvalidated candidateを用いたCoreのread APIで照会する。参照なしのStop/Holdや完了済みreplayを全面禁止しない。解除後に維持費や旅程を勝手に再開しない。

## 保存版移行

元Core8 header／module stampのまま、旧ledger・custody・ration・intent・module payloadを検証してから空Core9表へ移行する。旧1〜7も宣言した8→9手順を追加する。Core9表欠落をconstructor defaultsで補完しない。旧版への新node／proof prefix混入、未知field、record count、固定manifest、proof identity、重複、reverse orphan、未定義stageを拒否する。

読み込みで元manifestと現在の食品を一致必須にしない。Armed後に食品を通常消費しても歴史保存は壊れず、Inspectがstaleを示す。Restoreは既存proofとのrefs・確定済みdisarm stageだけを復元し、新証の割当やCommitを行わない。

## 検証と残作業

最終製品DLLで63試験が成功（基本31／四DLC32）。通常10、不正保存16、metadata checkpointと別process再読20、Armed後通常消費と別process解除4、既存回帰13。Evidence/P04DelegatedSupplyTransfersに63抜粋・README・Runtime SHA256の65ファイルを保存。過去版の成功を合算しない。新画面画像はない。0.4.14の両ZIPも保全する。

checkpoint fixtureは原成功saveから、名前を固定したproof/resultとarm city revisionだけを合法な途中形へ戻す独立clone。Preparedを保持し、Applyingが一度Preparedへ復旧すること、明示Resumeが同proofだけを確定することを検査する。実際の例外通知・Commit中断のfault injectionや保存全体の原子性の証明とは区別する。

元fixtureに正規の直接map出発候補がなく、輸送予約の正常候補との競合拒否は未検証。未登録sourceの実API正例、別actor操作、cityを共有する独立便の自動更新と保存再開も追加検証が必要。限定独立レビューの自動輸送例外指摘をRootが修正したが、修正後の独立再レビューは未実施。

NewOperationId→PrepareCreateの割当窓、Scribe個別throwの保存全体原子性、未所有Thingの保存責任、native通知例外後の唯一所有は未解決。T0/T1を全面完了とせず、T2/T3の現物移管やT4通常画面は後続。P04の通常生活・空腹・治療・仕事・生産・共有倉庫・多数拠点性能も別の残作業。

参照：[工程と受入条件](P04SupplyTransferImplementationGate.md)、[非予約意図](P04DelegatedSupplyIntents.md)、[native API調査](P04DelegatedSupplyNativeApi.md)。
