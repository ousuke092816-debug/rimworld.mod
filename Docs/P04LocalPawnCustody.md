# P04 拠点内の元個体保管 — 0.4.7

2026-10-08、Asia/Tokyo。通常操作から、登録した直接拠点の住民・動物を個別に凍結保管し、同じマップへ戻す機能を追加した。最終0.4.7 DLLで、新規保管・復帰のSave / Load / Finalと、未確定操作を設定無効のまま復旧するPendingSave / PendingLoad / PendingFinalの計6段階を、基本ゲーム・四DLCの両構成で確認した。通常画面の基本ゲーム英語・日本語、四DLC日本語の3描画も成功し、画像を目視した。四DLC英語と通常マウス操作は未実施。P04全体の完成や委任生活の実装完了は示さない。

Core保存schema 5、Colony保存schema 1、module protocol 2を維持する。0.4.6のCore保管基盤をColonyの通常操作へ接続する段階で、保存形式の変更や新しい移行処理は追加しない。過去のCore単体試験の成功を、この接続と画面の合格結果へ置き換えない。

## 通常操作と凍結保管の範囲

読み込み中のプレイヤー本拠地を連絡局へ登録し、MOD設定で「凍結保管の試作を有効にする」を選ぶ。「拠点内の凍結保管」は設定画面と拠点・輸送一覧から開く。既存の輸送ボタンとは別のボタンとして並べ、一覧と重ならない配置にする。

一つの拠点を選び、マップ上の候補とその拠点で保管中の個体を左右の一覧で確認する。各行には名前、人・動物の別、装備・衣服・所持品の個数と、個別の保管またはマップ復帰ボタンを表示する。個数は各子ホルダーのstackCount合計であり、別の抽象物資へ変換した数量ではない。完了待ちの操作は下の一覧からその操作を再試行する。

設定と画面には「本試作の保管中は食事・老化・生産が進まない。」を明示する。Coreコンテナが元Pawnを保持し、通常マップやWorldPawnsの更新へ二重登録しない。食事、老化、健康、治療、需要、生産を保管中に代行更新する処理はない。長期間の委任生活、委任生産、維持費、地区運営の実装として扱わない。

元のPawnをそのままCoreへ移し、装備・衣服・所持品も元の子ホルダーごと保持する。名前や種族から個体を作り直さず、元Pawn参照、native負荷ID、Core恒久IDを維持する。復帰先は保管した拠点の元マップに限定する。別拠点への移動や、キャラバンを介さない拠点間輸送は提供しない。

## 対象、残留住民、輸送との排他性

候補は読み込み中の登録済み直接拠点のプレイヤー本拠地に出現し、マップのspawnedThingsに属する利用可能な元住民・元動物である。ゲーム本体と同じ対象判定に加え、WorldPawnsとの重複、他providerのbinding、保管移管の予約、活動中の実物輸送の予約を検査する。死亡、ダウン、徴兵、精神状態などで利用できない個体を保管対象にしない。

住民の保管直前に、同じマップへ保管条件を満たす別の入植者が一人以上残ることを検査する。最後の利用可能な入植者は保管できない。候補一覧に表示されていても、押下時点の条件を満たさなければサービスが拒否する。個別操作のみを提供し、マップ全体を一括で保管する操作は追加しない。

活動中の輸送に予約された元個体は保管できない。未確定のCore保管移管がある元個体は、輸送の候補・新規予約・出発から除外する。保管の再開時と保存整合性検査でも、同じ個体の輸送予約と保管を重ねない。候補画面だけに依存せず、サービスを直接呼ぶ経路にも同じ排他性を適用する。

復帰時は、保管在籍の拠点と元マップを照合する。元の位置を基準に半径40セル内で、占有範囲全体がマップ内・通行可能・無占有となる空き位置を探し、その位置をCoreの復帰意図へ保存する。空き位置がない場合は拒否する。記録済みの復帰先が後から塞がれた場合も、別の位置へ意図を書き換えず同じ操作を再開する。

## 意図の再利用と設定を無効にした後の復旧

Colonyの公開操作は次の三つである。通常画面にCore IDや移管段階の実装用名称を表示せず、完了待ちと再試行として案内する。

| 操作 | 契約 |
| --- | --- |
| StoreLocalPawn(cause, baseId, pawn) | 新規保管は設定の有効化と利用可能なCoreが必要。同じ原因の同一要求は既存の入庫意図を再開する |
| ReleaseLocalPawn(cause, baseId, entityId) | 元の拠点・元マップだけに復帰意図を作る。設定を無効にしても、利用可能なCoreなら保管済み個体を戻せる |
| ResumeLocalCustody(transferId) | 保存済みの同じ意図をApplyNative、Commitの順で再開する。設定を無効にしても既存の未確定操作を回復できる |

CoreのPrepared、Applying、Committedという段階と、在籍表・現物の所有先の照合を利用する。Colony側に別の元Pawn保管容器や、Core移管表の複製は持たない。同じ原因を別の個体、拠点、方向、マップに使うことは拒否する。再試行のために元個体や証明を追加生成する方式ではない。

新規保管は初期値falseのcustodyEnabledで任意に有効化する。通常の画面入口は設定が有効、またはColonyに保管済み個体・未確定操作がある場合に表示する。無効化後は新規保管を止める一方、保管画面の入口、復帰、既存操作の再試行を残す。画面の案内もこの状態を説明する。試験専用のinh-colony-custodyフラグはサービスの有効化条件に含むが、通常の画面入口を開く設定の代わりにはしない。

## ロード時の読み取りとCore資産の照合

CoreWorldComponent.ReadPawnCustodyTransfers / ReadPawnCustodyResidentsは読み取り用snapshot配列を返す。通常は利用可能な現在のCoreから読み取る。モジュール復元中は、その時点で復元中のactorに限ってメタデータを読めるため、Colonyの検査がCore全体のIsReadyより先に実行されても成立する。現物の変更は従来の利用可能なPawnCustody APIを通す。bootstrap中の読み取り許可を現物変更の許可として扱わない。

在籍snapshotのSourceMapIdは、保存済みの入庫移管を参照してmapIdから導く読み取り項目である。在籍表へ新たなsourceMapIdフィールドを保存せず、既存の移管意図と元マップの関係を復帰・整合性検査に使う。

Colonyは自分のactorの移管を読み取り、現在の在籍だけでなく確定済みの過去の移管もすべて既知資産へ計上する。Coreの歴史的leaseが出庫後も残るため、現在保管中の行だけを数えて正常な証明を孤児資産と誤判定しない。各移管の直接拠点、mapId、provider、登録済み人物・動物、輸送との競合を照合する。

native個体の検索はマップ、WorldPawns、キャラバンに加えてCoreの直接保管容器を含む。同じ参照は重複除去し、負荷IDに一致する元Pawnを一つだけ解決する。マップから取り除かれた保管個体も、既存の人物・動物登録と照合できる。

## ソースへ反映したレビュー修正

次の三点を現在のソースへ反映した。ソースの読み取りレビューは担当者一名による範囲を絞った確認と、取りまとめ側の修正後の再確認である。二名による独立レビュー完了やP04全体の承認とは扱わない。

- 画面のcontrollerは保管・復帰の失敗時に原因を保持する。完了待ちの操作をResumeして成功したときは保持中の原因をクリアし、その後の新しい保管・復帰が以前の確定意図を誤って再利用しないようにした。
- DispatchPhysicalは既にDepartingの輸送でも、現物の出発や復元へ進む前に保管の未確定予約を再確認する。BeginPhysicalDepartureを経由しない再開経路にも、輸送と保管の排他性を適用した。
- 未確定保管の予約を調べるNativeCustodyReservedは、元Pawnが見つからない場合にnullを参照しない。既にDepartingの輸送に欠落した同行者がいるときも、通常の出発検査で安全に拒否できるようにした。

欠落同行者の回帰試験では、実際にBeginPhysicalDepartureした便の元住民を一時的にDeSpawnし、DispatchPhysicalを拒否させた。finallyで同じ元オブジェクトを元のマップ・位置・向きへGenSpawnし、予約を取り消して再利用できることを確認した。拒否中にキャラバン生成、積荷分割、Core証明の増加はなく、元の積荷も維持した。この経路は最終DLLで基本ゲーム・四DLCの両構成に成功した。別個体による補充を試験の復旧方法にしない。

## 最終検証の結果

最終0.4.7 DLLを用い、基本ゲーム構成とRoyalty・Ideology・Biotech・Odysseyの四DLC構成で以下を確認した。Anomalyなし。検証ログ、画像、使用DLLのhashと旧機能の回帰結果は [Evidence/P04LocalCustody/README.md](Evidence/P04LocalCustody/README.md) にまとめる。

| 試験群 | 確認する内容 | 基本ゲーム | 四DLC |
| --- | --- | --- | --- |
| LocalCustodySave / Load / Final | 元住民・元動物の個別保管、別プロセスの保存再開、同じ拠点・元マップへの復帰、恒久ID・native状態・所持品、証明の再利用 | 3段階成功 | 3段階成功 |
| LocalCustodyPendingSave / PendingLoad / PendingFinal | 未確定保管の保存再開、輸送との排他性、設定を無効にしたまま同じ意図を再試行し、同じ元個体を復帰する | 3段階成功 | 3段階成功 |
| 既にDepartingの便の同行者欠落 | 通常の拒否経路、同じ元オブジェクトの復帰、キャラバン・積荷・Core件数に副作用がないこと | 成功 | 成功 |
| LocalCustodyRenderEnglish | 実ゲームの英語描画、左右の個体一覧、個別操作、凍結警告と配置 | 成功・画像目視済み | 未実施 |
| LocalCustodyRenderJapanese | 実ゲームの日本語描画、左右の個体一覧、個別操作、凍結警告と配置 | 成功・画像目視済み | 成功・画像目視済み |

3枚の画像を目視し、左右の一覧、ボタン、完了待ち一覧、凍結警告に重なりや文字切れがないことを確認した。画面の描画試験と、通常マウス操作による一連の利用試験は区別する。後者は未実施である。

Core保管・旧schema移行・既存Colony/輸送などの回帰は上記の新規6段階と別に整理する。基本ゲーム・四DLCの旧機能回帰と四DLCの六国家保存再開も完了した。全42件のログ抜粋・原本SHA256・使用DLLと3画像は証拠一覧を参照。

長期保管中の時間進行、委任生活、委任生産、拠点間の保管個体移動、エンジン内部の強制例外を含む総合運営は今回の確認範囲外である。元個体の一時DeSpawnを用いた欠落試験は、spawn内部の失敗や例外を強制注入した試験ではない。

## 対応ソース

- [Source/Colony/LocalCustodyService.cs](../Source/Colony/LocalCustodyService.cs)：候補、個別保管・復帰、同一意図の再開、元マップと輸送予約の検査。
- [Source/Colony/CustodyWindow.cs](../Source/Colony/CustodyWindow.cs) と [Source/Colony/ColonyMod.cs](../Source/Colony/ColonyMod.cs)：通常画面、任意設定、無効化後の復旧入口。
- [Source/Colony/TransportCommands.cs](../Source/Colony/TransportCommands.cs) と [Source/Colony/PhysicalTransportService.cs](../Source/Colony/PhysicalTransportService.cs)：輸送候補・予約・出発の排他性、Core保管を含む元個体検索。
- [Source/Colony/ColonyService.cs](../Source/Colony/ColonyService.cs)：Coreの過去の移管leaseを含む既知資産検査。
- [Source/Core/CoreWorldComponent.cs](../Source/Core/CoreWorldComponent.cs) と [Source/Core/PawnCustody.cs](../Source/Core/PawnCustody.cs)：復元中の読み取りsnapshot、SourceMapId、元Pawnの保持と既存schema 5の移管契約。
- [Tests/ColonySmoke/Source/LocalCustodyProbe.cs](../Tests/ColonySmoke/Source/LocalCustodyProbe.cs)：保管・復帰・中間保存・画面試験と、既にDepartingの便の同行者欠落の回帰試験。
