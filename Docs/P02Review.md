# P02 保存・抽象台帳 独立レビュー

日付：2026-10-07（日本時間）。担当：制作指示書の独立レビュー担当として親から割り当てられたサブエージェント。実装担当とは別に仕様・コード・実ゲームDLLの呼出順・試験ログを読んだ。実装ファイルは変更していない。

対象：`Source/Core`、`Tests/SmokeMod/Source`、`Build/RunSmoke.ps1`、`Docs/CoreArchitecture.md`。基準は RimWorld 1.6.4871 rev591、Core 0.2.0、保存 schema 2 / protocol 2。

## 判定

修正後コードについて、今回の限定範囲を阻害する未解決のP1は見つからなかった。限定範囲は恒久ID、所有・文化・管理・保管の別欄、物理個体への保存参照、抽象物資の予約・解放・分割移管・再実行、保存版と復旧ゲートである。ゲーム本体の実物を運ぶアダプター、輸送の業務全体、税を含む占領の一括処理を合格とする判定ではない。

全LongEvent完了まで台帳を公開しない修正後、基本ゲームと四DLC構成の別プロセスWorldLoad/CompletedLoadで、同GUID、Prepared/Applying復旧、元Pawn一体の参照、抽象物資100個の発送・到着と再実行のPASSを実ログで確認した。旧版移行、前状態不一致の保留、未来版・12種の破損拒否についても下の範囲で確認した。

## 指摘と修正確認

| 指摘 | 再現条件・影響 | 修正確認 |
|---|---|---|
| P1：ヘッダー欠落時の台帳破棄 | schema 2のヘッダーだけ欠落すると、新GUID・空台帳で続行する初期実装だった。 | `Initialize`は台帳が存在するヘッダー欠落を拒否。両方が存在しない既存バニラへの追加だけを新規状態として扱う。schema 1に台帳が存在する場合も拒否。 |
| P1：参照解決前の初期化 | WorldLoadで保存済Pawn参照が未解決と判断され、正常セーブが拒否された。 | 読込時の`WorldComponent.FinalizeInit`から台帳初期化を外し、LongEvent終了後へ移した。基本ゲーム・四DLCのWorldLoad PASSを確認。 |
| P1：欠落した数量・状態を既定値へ置換 | 保存済lotのquantity欠落が0個、status欠落がAllocatedとなり、確定済資産を失った状態で検証を通せた。 | 強制保存する数値・enumの読込既定値を不正値へ変更。全registry行の数量・revision・status・authorityを検査する。 |
| P2：操作の前提リスト欠落 | Prepared/Applyingのbefore欠落で、ロックと再開時の前提照合を失った。 | kindごとの必要stamp集合を検査。revisionに加えて各対象・予約の全保存内容fingerprintを保存・照合する。 |
| P2：snapshotから実物の可変参照を公開 | BoundThingを通じて実ゲームの個体を変更でき、読取専用APIの説明と合わなかった。 | 公開snapshotをBoundThingLoadId文字列へ変更。台帳recordと履歴はコピーし、可変リストを公開しない。 |

参照解決順の根拠は実DLL `Verse.Game.LoadGame` のILで確認した。`World.FinalizeInit(true)`がIL166、maps読込がIL214、`Scribe.loader.FinalizeLoading`がIL439、`Game.FinalizeInit`がIL633。世界コンポーネントのFinalizeInitが物理Pawnを含むマップの読込・参照解決より先である。修正前のXMLにもEntityと作成要求の両方に同じ`binding`があり、保存漏れではなかった。

## 台帳契約の確認

- 全kindで共有する正の64bit連番を保存する。名前空間・kind・正規化済番号・重複・nextSequence後退を検査する。失敗・中止で消費した番号を再利用しない。
- 台帳変更はゲームスレッドに限定し、コピーへ適用・検証してからstate参照を一度だけ置き換える。抽象変更の途中状態を保存する経路を公開しない。
- 操作IDの再利用時は長さ付き正規化要求の全文とSHA256を比較する。異なるIDでもactor・操作kind・causeの重複を拒否する。Committed再実行は既存結果を返す。
- 予約は在庫の内数として扱う。発送で元ロットを減らし、同量の子ロットを便の保管へ移す。予約は消費済となる。到着はその子ロットの恒久IDを保った保管変更である。
- Applyingは今回外部副作用を行わない段階である。読込時に前状態が一致すればPreparedへ戻し、一致しなければNeedsReconciliationとして関係対象のロックを保持する。物資を補充して合わせない。
- 実Thingを持つPhysical資産は抽象移管から拒否する。Scribe参照・一体一登録を検査し、Pawnの代用品を作らない。
- moduleSchemaVersionを保存し、登録module、保存所有module、版、提供元Defを検査する。CoreにWorld/Colonyや任意DLCの型の必須参照を追加していない。

## 完成範囲の限界

1. 実物のスタック・人物を別コンテナへ運ぶ、実物の部分移動後から復旧する、死亡/消失/統合へ対応する処理は未実装。今回のPawn試験は元個体参照の保存再開までであり、P04の実物移管合格ではない。
2. `PrepareControllerChange`は管理欄と履歴を保存する原語。税・生産時刻の境界精算、未出発予約の処遇、占領中の到着の一括決着はP03/P04以降の業務実装が必要。元保管都市と出発を含む占領全体の直列化も今後の対象。
3. ModuleCatalogueは提供元・保存版の登録検査である。外交・通行・所有に基づく業務権限と、各都市の生産を更新する唯一のサービスの選択は未接続。
4. 地区訪問、再訪時の枠精算、部隊の実体化、プレイヤー都市の失陥・奪還、未実装モジュールの導入組合せは未検証。今回の全レコードコピー・全体検査方式の長期性能もP13で計測が必要。
5. 読込拒否・保留は安全な業務停止であり、破損を修理する機能ではない。schema 1移行の初回保存は別ファイルに行い、元ファイルを保持する。

## 試験追跡

この欄はレビュー担当が実際に読んだログに限定する。実行担当の報告だけをPASSへ置き換えない。

- 基本ゲーム `WorldSave-Player.log`：操作ID/原因重複、要求不一致、過剰予約、予約済lot移動、中止、pendingロック、別スレッド書込拒否、Applying前状態保存、ALL PASSを確認。
- 基本ゲーム・四DLC `WorldLoad-Player.log`：同GUID、Applying→Prepared、Prepared保持、元Pawn一体、発送後70/30、到着後総量100、所属保持、ALL PASSを確認。
- 基本ゲーム・四DLC `CompletedLoad-Player.log`：確定後の別プロセス再開、発送/到着の再実行、同個体一体、ID連番継続、管理履歴一回、ALL PASSを確認。
- 基本ゲーム `LegacyLoad-Player.log` / `LegacyReload-Player.log`：P01 fixtureのGUIDを保持し、schema 2・空台帳・nextSequence=1・一回の移行履歴を確認。別名の移行済セーブを再開しても同じ期待値でALL PASS。
- 基本ゲーム・四DLC `ReconcileLoad-Player.log`：前状態を100→99へ変えたfixtureでNeedsReconciliation、便在庫0、確定拒否、関係対象のロック保持、ALL PASS。
- 基本ゲーム・四DLC `FutureLoad-Player.log`：schema 99を拒否してメニュー到達、ALL PASS。
- 基本ゲームの12種の破損fixture：`HeaderMissing`、`LedgerMissing`、`NextBackwards`、`ModuleFuture`、`QuantityMissing`、`StatusMissing`、`PreconditionsMissing`、`ForeignScope`、`OwnerMissing`、`TablesMissing`、`DuplicateId`、`MissingModule`。すべて原因に対応する拒否、メニュー到達、ALL PASSを確認。
- 上記WorldSave/WorldLoad/CompletedLoad/Legacy/Future/Reconcile/Rejectの最終ログから、試験FAIL、例外、保存読込エラー、cross-reference解決エラーは検出されなかった。隔離runtimeのSteamAPI初期化警告とUnity終了時のメモリ統計は台帳試験エラーと区別する。
- P01原fixtureと、移行で読んだ`INH_LegacySmoke.rws`のSHA256はともに`A2C866EA6A1312DFBDAFB2AECFC11AAF30C0BA37525C31CD7EC153891FA926AE`で一致した。拒否fixtureの前後SHA256と終了コードの検査は`RunSmoke.ps1`の必須処理であり、このレビュー担当が独自に起動した試験ではない。
