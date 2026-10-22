# 通常委任管理 0.4.12：独立ソースレビュー

2026-10-09。読取レビュー。対象はDelegatedCustodyManagement、DelegatedCustodyWindow、ColonyMod、DelegatedCustodyService、Core PawnCustody、およびDelegatedCustodyUiProbe／DelegatedCustodyUiCityProbe。既存LocalCustodyService、CoreLedger、物理予約の参照も確認した。製品ソース・テスト・DLL・ゲーム・既存試験証拠は変更していない。この文書は実ゲーム試験の実行結果や配布完了を表明しない。

## 指摘

**P2修正済み：CityLockedの表示理由が汎用表示になる。** 初読時はDelegatedCustodyWindow.Problemに`CityLocked`がなく、都市ロック中の各ボタンは無効になる一方で理由はUnknownへ落ちていた。Rootへ連絡後、switchのCityLocked追加と既存の日英キーへの接続を再読して確認した。Rootは修正後DLLを再ビルドし試験をやり直す方針である。都市ロックfixtureの実画面で具体理由を確認するとよい。

今回の範囲で、新たなP1、原物複製・返還先差替え・都市ロック拒否後のID割当を起こす不具合は見つからなかった。

## 確認できた契約

| 境界 | ソース上の確認 |
|---|---|
| 設定無効 | 新規登録・入庫はDelegatedAllowedを使う。出庫と既存途中処理のResumeはCore readyに基づき、設定offだけでは閉じない。管理画面入口は既存の地点／保管／途中処理があれば残る |
| 都市の未確定Core処理 | 候補・居住者・途中処理へCityLocked。Colony入庫はRegisterCustodyPawn前、出庫はPrepare前に拒否。CoreもPrepareのNewOperationId前、Applyのnative操作前、Commitのresident更新前に検査する |
| 読取API | 管理snapshot生成は照会のみ。候補表示のために個人を登録せず、元隊列・Pawn・所持物を参照する。Core allocatorの呼出しなし |
| 最後の護衛 | 新規入庫の候補・命令双方が別の適格humanを要求。入庫途中のsnapshotにもNoEscortを追加。CoreはPrepare、Apply、Commitで実際の隊列の適格護衛を再確認する |
| 予約 | 元Pawnの未確定custody、活動中の物理輸送予約を新規入庫前に拒否。出庫・途中再開は個人の活動中予約を拒否。未確定食品はCore出庫Prepare／Applyでも拒否される |
| 実物同一性 | 保存したnative Pawn binding、入庫証、元Caravan IDとtileを使う。独占holder・WorldPawns・隊列所属をCoreで確認。別隊列を自動生成しない |
| 設定offの返還 | 居住者snapshotの返還先は選択中の別隊列ではなく元入庫証の隊列。画面はresident.Caravanを実命令へ渡す。元隊列不在・移動中・別地点では理由を示し返還しない |
| 原隊列の現在地点で登録 | 表面layerの有効な停止中player Caravanだけ。map、既存base、Caravan以外のworld objectと競合するtileを拒否。画面のnameはtrim、backendは空白・制御文字・長さを検査する |
| 登録replay | 同じkey／name／tileは占有検査前に既存cityを返す。name変更・別tileで同じ安定keyを使う場合は拒否。新causeの同tile重複も割当前に拒否する |
| command cause | controllerは失敗した同じ要求のcauseを保持し、成功後は取り除く。途中処理は保存済みintent IDで再開する。Coreのcause再利用はnative・entity・city・Caravan・tile・方向との照合で異なる要求を拒否する |
| 完了証の再実行 | Committed intentのResumeはnative操作前に終了する。過去のstore／releaseを再実行して現在の原物を過去のendpointへ戻さない |
| 古い座標なし拠点 | UIに旧拠点を残し、Unlocatedとして入庫を閉じる。現在の隊列位置を勝手に旧拠点へ付与しない |

食品の通常生活化、Pawn tick、health／skill更新、食料補給・処分はこの変更に含まれていない。維持費画面への導線は既存の日次事前払いへ接続するもので、凍結原物の生命シミュレーション完成とは扱わない。

## 試験fixtureと手順のレビュー

### 通常UI probe

DelegatedCustodyUiProbeは元の帰還済みfixtureからnative IDs、Core scope、元隊列、個人bindingを引き継ぐ。開発用`inh-delegated-custody`を使わず、テスト専用のreflectionでnormal settingだけを切り替える。ユーザー設定ファイルへ書き戻さない。

入庫・出庫・途中再開は製品controllerを通る。未確定入庫／出庫の準備だけはCore APIで状態を作り、別processで保存から再開する。所持物のID／def／stuff／数量／HP、Pawnのneeds／技能／健康／遺伝子等を操作前後と別process読込で比較する。普通の移動は生体tickと食料消費を伴うので、登録操作の直前に取り直したbaselineだけを不変条件とする。native tileの直接変更を使わず、経路移動によって新地点へ到達する手順は妥当である。

readonly・拒否の判定はCore EntityCount、Colony payload、custody history、Pawnと所有を比較する。City probeはさらにNextSequenceと全entity／operation ID集合を比較するため、未使用operation IDだけが残る漏れも検出できる。

描画probeは英語／日本語の実ゲームwindow repaintと画像出力を確認する。controller経由の命令試験と描画試験が中心であり、全ボタンのポインタ操作・スクロール・各FloatMenu選択の網羅を証明するものではない。

### 都市ロック probe

DelegatedCustodyUiCityProbeはRootが別名に複製した食品第二証Prepared／Applying fixtureを使用し、自身ではXMLや隊列tileを改変しない。未確定証のリロード後正規化とRecoveryCountを確認し、同じ食品証IDを明示ResumeでCommitして解除する。解除前のCore Prepare／Apply／Commitと通常controllerは具体的な拒否理由を照合し、解除後の既存動物出庫は新IDなし・原食品の再分割なしを確認する。

読取時点の修正版は、元escortが既に物理移動で登録された可能性を認める。元保存の`inheritanceLedger/entities/li/binding`から既存IDを読み、0件なら未登録を維持、1件なら同ID／provider／kind／authority／identityを維持する。以前の「護衛は未登録」という誤った前提を除去しており、意図したfixtureと整合する。既登録の護衛を用いたcity試験は、拒否時の全ID・allocator・既存binding不変の証拠となるが、未登録個人の登録処理が実行されなかったことの直接試験とは呼ばない。新規入庫のcity guardがRegisterCustodyPawnより前にあることは別途ソースで確認した。これはソースの修正確認であり、修正版DLLの試験PASSをこのレビューで代行してはいない。

各checkpointで元食品証・spent witness・native holderを保持し、正常な欠落を装う緩和はない。city lock中に増える2IDは、停止中humanの再請求ではなくanimalのPending Hold controlとして明示している。Held planが解除後に自動再請求されず、Stopped humanの計画も再開されないことを検査する手順は仕様に合う。

## 最終確認の限界

Rootが直列で実行中の旧回帰と追加city試験、最終DLLの英語／日本語表示確認が配布判断の根拠となる。本レビューはその証拠を上書きしない。CityLockedの文言対応を修正した場合は、更新した最終DLLで該当表示を確認する。使用済み食品の処分とCore8は引き続き別工程である。
