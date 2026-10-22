# 使用済み食品保持量削減：実装前の構造レビュー

2026-10-08。**未実装の設計レビュー**。対象はP04SpentWitnessRetention.mdと現行Core7の保存・食料構造。0.4.11の最終検証・配布は本書の対象外であり、完了を表明しない。製品ソース・DLL・既存証拠を変更していない。

**本書で提案する最小範囲も、現時点では実装しない。** 直近0.4.12は通常の委任地点登録と元隊列からの入出庫画面を先に整え、既存維持費を通常プレイから利用可能にする方針である。Core8移行や実物処分をその工程へ混ぜない。下記は、保存入口の原子性と途中証・Unresolvedの検査責任を閉じてから着手を判断する、将来工程の契約案である。

## 実装前に閉じる三つの境界

### 1. 現行の検査順では、正常な処分済み保存も拒否される

`CoreWorldComponent.Initialize`はcandidate台帳を作り、PawnCustody、Rationsを検査した後に`candidate.Recover()`を呼ぶ。`RationDebits.Validate`は全Applied／Committed食品行へ`CheckWitnesses`を適用し、実物の数と全行の一対一対応を要求する。保存時も同じ実物検査を行う。

Core8では、実物を削除するだけでなく検査責任を分ける必要がある。推奨する読み取り順は次のとおり。

1. header、module payload、台帳のshape・件数・Core request/hashを検査する。
2. 元食品の第一・第二証、行metadata、元入庫証を**実物の存在に依存せず**検査する。
3. 処分意図・適用・確定・保留の証を、検証済みの元食品行へ照合し、退役／途中／保留indexを作る。
4. Live行と現在の専用holderを厳密に照合する。退役行の実物が残っていないこと、処分journalで説明できない欠落・余分な実物がないことを確認する。
5. 既存Core transactionの復元、module復元へ進む。loadからDestroyや新証割当を行わない。

この分割で、Prepared／Applied食品のnative検査を省略しない。対象候補はCommitted食品だけである。Core内部candidate検査中は`Core.Rations`のready専用accessorを使わず、検証用の内部読取りを使う。Colonyが処分証を所有するなら、bootstrap用`Read...` APIとColonyのknown/orphan検査も追加する。

### 2. mutation guardだけでは、削除後の例外から安全な保存へ戻せない

guardはnative削除中の保存を防ぐ。しかしDestroyは巻き戻せない。途中callbackの例外や、削除成功後のCore適用証Commit失敗でguardを解除すると、現行の実物一対一検査は保存を拒否する。元Thingを作り直して帳尻を合わせる修復は禁止する。

最小案の「不明なら通常保存を拒否する」は、欠落を処分扱いにしない点では正しい。ただし、ユーザーのそれ以降の進行も保存できなくなるため、**通常の自動処分として公開できる安全な初回実装には不足する**。削除前の独立した保存があっても、この制約は消えない。

本運用へ進む前に、少なくとも次の途中状態の保存契約を定義する。

| 状態 | 必要な根拠・許可すること | 禁止すること |
|---|---|---|
| 未着手 | 全元実物と確定した消去意図が一致 | 別対象への選び直し、欠落行を候補に追加 |
| 削除成功後・Core適用証がPrepared／Applying | 同じ不変request/specとResultId、成功を記録した行の対応を確認。既存Core opの明示再開を設計する | missingだけで成功を推定、新ID割当、同じThingへ再Destroy |
| callback失敗で元実物が残る | 同じID・holder・数量の現在物を保留対象として記録 | 例外を無視して続行、補給・返却 |
| callback失敗で元実物が消える／所有が不明 | 消去意図と元行に結びつく専用のUnresolved保留。正常Disposed証とは区別 | 欠落を正常処分として確定、再生成、再削除 |
| 確定済み | 元食品証、消去証、compact indexが一対一対応し、対象実物が残らない | 元食品証・使用済みIDの書換え |

現行`CoreLedger.Recover`はPreparedを自動Commitしない。ApplyingをPreparedへ正規化するだけである。処分の未確定Core証を扱う場合も、読み込みで既存IDの参照を補完し、明示Resumeで同じoperationをCommitする契約が必要になる。

未確定Core適用証のrequestがあるだけで、正常Disposedの最終証とみなさない。途中状態として保存する根拠と、確定条件を分離する。Unresolvedを通常の食品不足・維持費再開から自動解決してはならない。保留証さえ作れない例外は、明示した保存拒否の限界として残る。

#### Core8より先に検証する保存入口の原子性

現行Coreの`ExposeData`は、Saving中にcounts同期・native検査・module snapshot生成を行ってからCoreの各要素をScribeへ渡す。この箇所の例外だけでは、ゲーム全体の保存開始前に止まったことや、既存の保存先が無変更であることを証明できない。別componentやScribe自身の後続例外も対象にする。

- 保存開始前の全component検査を、出力先へ書き始める段階から分離する。処分中・不正保存・未保存可能な保留はこの入口で拒否し、拒否によって既存正常保存を置換しない。
- module snapshot・counts・header変更を含め、検査失敗で一部だけ永続化用snapshotが更新される窓を列挙する。検査成功後の一つの整合したsnapshotから出力する契約が必要であり、Scribe途中で証を新規作成しない。
- 保存開始、Coreの前後、他component、writer flush／close、保存先置換の各境界へ例外を注入する。例外後の前回正常保存のbyte/hash、再読込、ゲーム内の状態、再保存可能性を別々に確認する。ゲームの既存保存処理が既に一時出力・安全な置換を保証するかは、実APIと実試験で確認し、未確認の保証を前提にしない。
- 保存拒否は破損を成功扱いにしない防壁であり、実物処分の例外回復策の代わりにはならない。安全なUnresolvedを保存できない段階では、通常プレイのDestroy処分を有効にしない。

検査責任は、食品側が旧不変証と未確定食品の全原物、処分側が意図・途中進捗・同op証・Unresolvedの根拠、共同収支検査がLive／確定Disposed／途中／Unresolvedの排他所属を持つ。Unresolvedは「欠落が説明済みの正常処分」ではない。意図に固定された対象であることを証明できても、削除成功の証明を欠く状態として保存し、通常のCommitted replayと明確に分ける。

### 3. 保存容量削減とnative物品数削減は別の合格条件

1物品につき意図・適用・確定のCore証を作れば、複数のlease、operation、request、revision stampが増える。小さな食品ThingのXMLを消すより保存量が増える可能性がある。

初回の1物品処理は契約試験には適しているが、容量削減の達成とは扱わない。複数物品のbatchで証を共有し、元食品行に既にあるmetadataの重複を減らす設計を比較する。Core永久ID・過去の日次食品証を削除して容量を減らす方法は含めない。

## 将来の最小実装範囲案（まだ実装しない）

**先行工程A**は、Core8の空journal移行、対象Preview、確定した消去意図、厳密な検証と試験fixtureまでに限定する。実物を削除しない。この段階は移行と証の契約を検証する成果であり、保持量削減完了とは呼ばない。

**処分工程B**は、途中状態の安全な保存・既存Core op復旧を実装してから進める。最初は明示操作、同actorの少量batch、Committed食品、Core専用holderの元実物に限定する。自動Tick処分、stack合併、未確定食品、共有倉庫、輸送貨物、Pawn・生体needsの変更を含めない。

一つのbatchを実行する前に全対象を確認し、保留／途中処分に属するIDを除外する。全量成功、部分成功、未知の消失を別々に扱う。使用済み食品は既に消費された資源であり、保留によってinventoryへ戻さない。

処分leaseはcity／community参照を持たず、関係をidentityに固定する。既存食料証による都市ロック中でも、処分の保留を記録できるようにする。原入庫証や元隊列は履歴の確認に用い、確定済み証のreplayに現在の隊列存在を要求しない。

## Core7から8への移行契約

- schema7の元header・module stampでpayload、台帳、custody、Rationsと全Live witnessを検査してから8へ移す。producerやstampの更新を先に行わない。
- scope、Core次ID、元個人・食品ID、inventory、旧第一・第二証のhashを保持する。空の処分journalと移行履歴だけを追加する。
- 旧Ration第二証の既存ResultId補完は、現行の既存証復旧として検査する。新ID割当やnative mutationとは区別する。
- Core8で必須journalが欠けた保存は拒否する。旧版へのdowngradeやversion数値変更で、退役実物を再生成しない。
- 旧schema1〜6の既存移行の最後にも空journalを追加する。旧保存版に非空処分記録や処分Core opが混入した場合は拒否する。

## 追加する試験行列

各ケースは基本ゲームと四DLCの別保存先で行い、段階ごとに別processを用いる。製品DLLが変わった場合は、既存の適切な保存・日次維持費回帰も同じ最終DLLで確認する。

| ID | fixture／操作 | 主要な合格条件 |
|---|---|---|
| M01 | Core7、使用済み物品なし | 空journalへ移行、Core次ID・元header scope不変、native操作なし |
| M02 | 0.4.10／0.4.11の複数日食品証・使用済み物品あり | 全旧証・実物・inventory・Pawn一致。移行読み込みで処分なし |
| M03 | 第二食料証Prepared／Applyingの旧保存 | 同op復旧契約を維持。未確定食品を処分候補にしない |
| M04 | Core7に非空処分表混入、Core8必須表削除 | 検査拒否、元保存byte/hash不変 |
| D01 | 消去意図Preparedで保存・再読み込み | 全元実物保持。意図再実行で新ID・新候補なし |
| D02 | 正常適用→Applied保存→別process Resume | 削除成功の行へ再Destroyしない。既存証を用いる |
| D03 | Committed保存→再読み込み→Prepare/Apply/Commit replay | 追加割当・削除・返却なし、旧食品証不変 |
| D04 | 同元stackを日跨ぎ消費→旧pieceを一部処分→次日消費 | 元stack IDと残量、全日別栄養・数量収支が一致 |
| D05 | 出庫・帰還・元隊列消滅後の履歴処分 | 元Pawnや隊列を新生成せず、旧証のreplay可能 |
| L01 | 同cityの別個人の第二食料証が未確定 | 処分対象は確定行のみ。参照なし処分controlの保存が都市lockに阻まれない |
| L02 | 維持費Stopped／Held、処分実行 | 日次予定・未払い日・通常Pawn凍結状態を変更しない |
| F01 | 最初のDestroy前に中断 | Preparedと全実物が一致し、再開で一度だけ処理 |
| F02 | batchの一部だけ正常削除後に停止 | 確認済み行と未着手行の区別を保存。既処理IDの再Destroyなし |
| F03 | 全削除後、適用Core証Prepare／Commit前後に停止 | 未確定proofを新IDで置換しない。missingだけのDisposed確定なし |
| F04 | compact index更新前、確定証前に停止 | 同じ対象と証へ再開。一部退役を二重計上しない |
| C01 | callback例外、元Thingが元holderに残る | 物品があるのにDisposedにせず、専用保留または明示した保存拒否 |
| C02 | callback例外、元Thingが消えた後 | 自動補充・再Destroy・正常Disposed扱いなし。Unresolved保存契約を検証 |
| C03 | callbackで別holderへ移る | missing扱いにせず、foreign ownerを奪わない |
| S01 | 保存入口の事前検査で拒否 | 前回正常保存byte/hash不変、出力開始なし、通常処分は始めない |
| S02 | Core／別componentのScribe途中で例外 | 前回正常保存を置換しない。未完成出力を正常保存として採用しない |
| S03 | writer flush／close／保存先置換で例外 | 前回正常保存を再読込できる。成功通知なし、次の保存で証の新規発行なし |
| S04 | Unresolvedを含む整合snapshot保存・別process再読込 | 正常Disposedへ昇格せず、元物再生成・同ID再Destroyなし、食品側／処分側の検査を両方満たす |
| X01 | batch／行／意図／適用／確定証削除、件数変更 | Core op・leaseとjournalの逆照合で拒否 |
| X02 | 使用済みID・数量・HP・def・第二証・行番号差替え | 元食品証との照合で拒否 |
| X03 | 二重batch、Liveと退役の二重所属、Disposed実物残存 | 一対一収支・実物検査で拒否 |
| P01 | batch件数1／少量／増量、保持する履歴数を増やす | native Thing/comp数、XML byte数、ロード・検証時間を別々に測定 |

例外注入は製品の欠落検査を緩める方式にしない。テスト専用の停止点・callback故障を用い、削除前・各段階の独立したfixtureを保持する。旧保存・配布ZIP・0.4.11の最終証拠は上書きしない。

## 実装へ進める判定

現時点では工程A・Bとも実装しない。保存入口とScribe途中例外の契約を実証し、途中証・Unresolvedの責任分離を決めてからCore8へ進む。将来の工程Aは旧保存の厳密移行と新journalの不正保存拒否を、工程Bは破棄途中を保存可能にする状態と既存Core証再開を、それぞれ実装・試験してから有効にする。少量正常系だけが成功しても、自動処分や長期容量削減の完成として扱わない。
