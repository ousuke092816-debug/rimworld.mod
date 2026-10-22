# 補給準備と既存輸送の競合保護 — 0.4.16

2026-10-09。同じ最終製品DLLで39試験PASS（基本19／四DLC20）。Core9／Colony1／World1／protocol2を維持し、producerだけ0.4.16に進める。Evidence/P04SupplyCompetitionGuards16へ44ファイルを保存し、全試験Runtimeの製品ファイルをSHAで照合した。

## 変更した動作

補給準備の責任がArmedの都市と競合する抽象輸送は、stage変更、操作ID発行、証の準備より前に止める。自動更新も待機する。取消は予約を持つ出発都市を確認し、到着都市だけがArmedなら安全な取消を許す。

Coreの未確定証が都市を直接参照せず、都市内の在庫・住民をpreconditionとして保持する場合も、新しい補給準備を始める前に検出する。予約そのものにはcustodyが入らないため、公開PrepareDeparture／PrepareReleaseではReservationRecordの元custodyとlotを解決して検査する。

確定済み抽象departureの途中module保存は、既存証と要求を厳密に照合して参照を復元する。確定済み証の再生は、新しい在庫操作やIDを作らない。確定証の過去のbefore fingerprintを現在の在庫と比べて拒否する変更は加えない。

## 再現入口

Build/ValidateSupplyGates.ps1は、最終製品Core／ColonyのDLLを使って隔離Runtimeを作る。GroupはAbstract、Physical、Legacy、ReservationScope。対象4DLCありはWithDlcで切り替え、Anomalyは有効にしない。ゲームは必ず直列で実行する。

- Abstract：start → load → final → checkpoint。checkpointは実際のArmed保存を複製して、確定済みdepartureのmodule stageだけを前段に戻す。
- Physical：start → load → final。有効な直接マップ出発候補を使い、準備解除後に同じ元住民と同じSteel全量を一度だけ出発させる。試験で明示生成したSteelは食品ではない。
- Legacy：load → final。実際の旧0.4.15が残したArmed＋低水準Prepared reservationを使う。旧版fixtureを新製品で作ったとは扱わない。
- ReservationScope：load → final。実16の確定予約保存から、Armed中の公開Core出発・解除が無変更で拒否されることと、disarm後の正例を確認する。呼出側のoperation IDは測定前に発行する。

InputDataPathは原入力のSaveDataを指定できる。原saveのSHAを前後で照合し、再読込用cloneと原入力を分ける。WorkPathはworkspace/work内に限定し、既存の祖先・下位パスにリンクやjunctionがあれば、ディレクトリ作成前に拒否する。原ゲームの設定・セーブを操作しない。

## 未完成の範囲

真の未確定Colony transport checkpointとArmedが同居する旧保存は、まだ厳密なHeld復旧を実装していない。上記Legacyはmodule transport recordを伴わない低水準予約であり、その成功をmodule checkpoint対応へ流用しない。

NewOperationIdからPrepareまでの割当窓、未所有食品の深い保存、native移管通知の例外、Scribe全体の保存原子性は未解決。実物食品移管、通常補給画面、委任住民の生活・治療・仕事・生産も後続工程である。T0/T1全体、P04全体の完成とはしない。

修正前candidate18試験と修正後最終試験を別に保全する。各記録の製品DLL・構成・原入力・成功条件を確認し、件数を異なるDLL間で合算しない。
