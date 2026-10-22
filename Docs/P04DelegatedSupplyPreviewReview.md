# 個人食品補給preview：未統合コードの独立レビュー

2026-10-09。対象はworkspaceの`work/p04-supply-preview-staging`内のCore member snippet、RationSupplyCandidateQuote.cs、DelegatedSupplyPreviewService.cs、READMEとGenerated検証用ソース。現行Core／Colonyの参照先も読み取りで確認した。編集は本書だけ。製品・stage・試験・DLL・ゲームは変更せず、buildも実行していない。

**今回の成果は読み取り用の候補照会であり、補給の実物移管は未実装。** [実移管の必要条件](P04DelegatedSupplyReview.md)で示したPrepared自然stale、取消、移管途中、第二証、Unresolved、保存全体の原子性を満たしたことにはならない。

## 結論

読取範囲でP1／P2の修正必要点は見つからなかった。新しいCore ID、予約、cause、journal行、食品分割・移管、摂取、維持費支払、保存migrationを呼び出す経路はない。通常のメモリ上のsnapshot／List生成と、Coreの永続ID割当を区別する。

Rootはステージング専用Assembliesでコンパイル成功、および製品Core／Colony／Worldの3 DLLについてコンパイル前後のSHA不変を報告している。Runtime／Testsへは書込み経路がないことをスクリプトで確認したが、その前後SHA照合の実測は含めない。本レビューはそのビルドを再実行せず、実ゲームでの非変更性を実証してはいない。README冒頭の「未コンパイル」は作成時点の記載なので、最終資料ではRootの成功記録と時点をそろえるとよい。

## Core照会の確認

- 既存Core ready／world／main thread／actor／Scribe inactive gateを通る。AssertWriteAccessという名称でも、ここでは確認だけで台帳を更新しない。
- CheckResidentを再使用し、元入庫証とresident、元native binding、独占Core holder、隊列非所属、custody途中処理のないことを確認する。CheckResident用の一時RationDebitRecordは保存表へ追加しない。
- DailyDemandを再使用し、food needなし、既存判定の有効Gene_Resource、有限正需要なしをUnsupportedRecipientとして返す。新しい生体更新や特殊資源対応を追加しない。
- PlainFoodを再使用し、category、corpse、drug／drugCategory、outcomeDoer、IngestibleNow、食べ手のWillEat方針を保つ。食べ手別NutritionForEaterが正の有限値か確認する。Colonyへ同じ食品適格性コードを複写していない。
- sourcePawn inventoryの同じfoodとholdingOwnerの一致を確認する。null source／foodは受取個体の需要照会のための既知のSourceInventoryMismatchであり、summaryはこの食品側理由だけを採用しない。受取側の拒否を無視してtrueにしない。
- Core quoteは食品・受取個体の観測用primitiveで、元隊列やcargo予約の全条件を判定するAPIではない。それらはColony summary／各行のProblemsへ合成される。将来のtransfer許可としてCore quoteのCanPreviewだけを使わない。

## Colony照会の確認

| 項目 | 読取結果 |
|---|---|
| 設定off | previewを閉じない。補給の設定／命令を追加していないため、実移管の許可を意味しない |
| Core unavailable | WorldUnavailableのsnapshot、需要未取得、食品0行、CanPreview false。readyでないCoreからresident／journalを読まない |
| 古いworld／load/save/thread不一致 | Gateで拒否。無効な世界を照会成功として返さず、IDを追加しない |
| 地点・元隊列 | located delegated site、元入庫Caravan ID、現在の表面地点と停止状態を確認。不在・移動・別地点・別隊列は共通Problemsと各食品行へ引き継ぐ |
| 受取個体 | delegated residentの存在、活動中physical予約、custody pending、ration pending、Core mutationを区別する |
| 都市 | HasPendingReferenceによるCityLockedをsummaryと食品行へ付与。都市lockを解除する操作はしない |
| source個人 | 元隊列の人／動物inventoryだけを列挙。CaravanOriginal、native custody予約、活動中transport予約、pending ration、既存provider／永久bindingを確認 |
| 未登録source | RegisteredPawnIdによる照会のみ。SourceEntityIdはnullのまま、RegisterCustodyPawnを呼ばない |
| source食品 | 栄養食品を広く発見し、対応不可食品もUnsupportedFood等の理由で残す。候補が移管・消費により変われば次回照会で観測し直す |
| immutable貨物 | 全physical履歴でsourceNativeId／cargoNativeId両方を照合。Cancelled／Arrived／別return済みを解放しない。JourneySupplyも対象。manifestや損失証を変更しない |
| 受取inventoryの貨物 | 現在の栄養食品にcargo履歴があればRecipientHistoricalCargo／RecipientHistoricalJourneySupplyで保守的に止める |

sourceのTransportReservedはCheckDelegatedOriginalでも拒否されるため、SourceTransportReservedとSourceIneligibleが同時に残る場合がある。単一理由に潰さない現在の契約と整合する。foodsが0行ならsource個別の拒否を表示する行もないので、summary trueから食品あり・配達可能を推定しない。

## snapshotとCanPreviewの意味

Problems／Foods／CargoHistoryは、元Listを公開しない固定コピーのReadOnlyCollectionである。名称・count・HP・def／stuff・栄養は照会時の値として保持する。一方、Pawn／Thing／Caravan参照はnative識別用の可変オブジェクトであり、その物品自体が予約・凍結された意味ではない。

summaryのCanPreviewは地点・元隊列・受取個体の照会条件、行のCanPreviewはその食品を含む観測条件を満たすことだけを意味する。需要より栄養が少ないstackも候補になりうるし、食品0行でもsummary trueになりうる。「不足が解消する」「補給できる」「操作を実行済み」という表示へ流用しない。不明栄養／需要の-1を0の栄養として合計しない。

古いsnapshotのFood参照から現在countを読むと、保存済みCountと食事・腐敗後の現在物が異なる場合がある。将来の画面は観測時の値を表示し、更新・実命令時は新たな照会と不変意図契約を使う。保持した原物参照へ直接TryAdd／SplitOffを行う機能は、このpreview APIの契約に含まれない。

## 統合前後の必要な実測

| 確認 | 合格条件 |
|---|---|
| 同じ照会の反復 | NextSequence、全entity／operation ID、food quantity／HP／ID／owner、custody／ration／maintenance、payload、Tick・need・age・healthが無変更 |
| 未登録sourceと空inventory | person／animalの永久IDを増やさず、null SourceEntityIdと候補0行を正しく扱う |
| 人→人、動物→人、人→動物 | 既存NutritionForEater／DailyDemandと一致し、元食品を元holderへ残す |
| unsupported recipient／food | needsなし、有効特殊Gene、薬・遺体・outcome、食事方針を既存判定と同じ理由で拒否 |
| city／source／recipient競合 | summary・行に複数理由が残り、照会だけで保留証や予約を作らない |
| 全cargo履歴・source／cargo ID一致 | 過去の取消・到着・帰還で候補を自動解放せず、manifest無変更 |
| source通常食事／腐敗後の再照会 | 古いsnapshotの観測値を保持し、新照会は現物から再算定。復旧・取消・処分証を生成しない |
| world未準備／古いworld／Scribe中 | 未準備のfalse snapshotまたはgate拒否。未検証世界から食品を許可しない |
| readonly collection | 変更が拒否され、元snapshotと別照会のListが共有されない |

実測を行う場合は独立したsave先と同じ最終DLLで基本ゲームを先に閉じ、四DLCを追加する。Core member snippetは単独compilation unitではなく、Generatedも検証用である。製品統合時はsnippetを既存クラス内へ挿入し、検証用Generatedを重複ビルド対象に入れない。

このレビューの合格対象は、未統合の読み取り設計に限る。通常UI、クリック、補給Prepare／Apply／Commit／Cancel、部分stack、生活・食料need、保存新版の成果は含めない。

## 追加：候補確認Windowと日英53キーのレビュー

同日、未統合DelegatedSupplyPreviewWindow.csとEnglish／JapaneseのSupplyPreview.xmlを追加で読取レビューした。RootはWindowを含むステージングコンパイル成功を確認済み。本担当はcompile・ゲーム起動・製品取込みを行わない。

**追加P1／P2なし。** Windowには食品を選択・予約・分割・移管するhandlerがなく、一覧・理由・tooltipの描画だけがある。通常の画面入口への統合はこのstageのWindowだけでは実装していない。実物補給の未実装条件は前節から変わらない。

### 接続と表示

- 指定city／entityをnull隊列で照会して元入庫anchorだけを得る。現在の隊列一覧から同じCaravan IDを厳密一致で探し、存在すればその元隊列で再照会する。別隊列を選ぶ機能や代替生成はない。
- 受取個体名がquoteから得られない場合は既存resident snapshotで読取補完し、確認できない場合は局所化したUnavailableを使う。Core・世界の整合性拒否はReadUnavailableの表示に閉じ、成功や空の有効候補として扱わない。
- summaryと食品行はProblemsの全要素を局所化して箇条書きにし、同じ文言だけを重複排除する。都市／受取個体／source／cargoの理由が複数あっても最初の一件に潰さない。
- negative sentinel、NaN、InfinityはNumberで日英のUnknown／不明を返す。正常値だけを現行cultureと小数3桁までで表示する。表示値は概算であり、確定栄養や請求の証ではない。
- Text.WordWrap=true、描画と同じwidth／fontでCalcHeightし、長い名前と各理由の高さをscroll内容へ足す。固定行高で理由を隠す構造ではない。Text.Font／WordWrapとscroll終了はfinallyで復元する。
- cargo tooltipは輸送IDを露出せず、旅程食料／貨物と元source／運ばれたcargo／両方の一致を説明する。記録件数と、同内容をまとめた説明行数は同じとは限らないが、履歴を削除・解放する操作はない。

### 誤解を避ける文言と非変更契約

日英のtitleは候補確認／preview、固定footerは「食品はまだ移せない」「保管個体は凍結・普通の生活は未実装」と明示する。summaryの見込説明も予約・移管保証なしと示す。理由なしの行にも「候補確認のみ」を添えており、CanPreview=trueを「補給実行」のボタンや完了通知へ変換しない。

WindowはforcePauseを使うため、開いている間の通常ゲーム進行を止める画面上の作用がある。この作用は食品・Pawn・Core台帳を更新する照会とは区別する。scroll位置、Rendered、tooltip、表示blockなどの画面状態は変わる。非変更性の実測はCore ID／payload／原物／生体状態を対象とし、画面状態そのものが不変とは主張しない。

### 静的な文言検査の結果

本担当はゲームを使わずXMLを読取り解析した。日英とも53 element／53 unique key、key集合差0、数値placeholder集合差0、Windowの直接参照key欠落0。Problem switchの25コードに対応するキー欠落0だった。XMLの構文・参照集合を確認した結果であり、Verseによる実翻訳読込や実画面描画のPASSではない。

### まだ実測していない事項

1. 日英の実ゲーム描画でtitle・固定footer・長名・複数理由・0候補・Unknown値のscroll表示が重ならず、読めること。tooltipの画面外補正、GUI scale、解像度変更も必要な範囲で確認する。
2. 通常管理画面から対象residentのpreviewへ開く導線と、設定off／pending resident／元隊列不在時の読取表示。現stageに導線を付けた結果は含めない。
3. repaint／scroll／tooltip反復の前後で、Core NextSequence・ID集合・payload・食品数量／HP／holder・Pawn need等が無変更であること。WindowのforcePauseと通常UIの局所状態は分けて測定する。
4. 食品が通常食事・腐敗で変化してから新しく開いた時、最新の観測を表示し、古いsnapshotを予約／確定証にしないこと。
5. 実翻訳読込でエラーがなく、未知理由は局所化したfallbackへ閉じること。コンパイル成功とXML静的一致だけで実ゲーム描画を合格扱いにしない。

## 追加：未統合SupplyPreviewProbeの妥当性レビュー

同日、ステージングSupplyPreviewProbe.csだけを読み取りレビューした。Rootは`-WithProbe`によるステージングcompile成功を報告している。本担当はbuild・製品変更・試験変更・ゲーム起動・保存変更を行わない。初版の必須修正2件をRootへ連絡後、修正後stageを再読し、**両件の解消を確認した。** Rootも修正後のステージングcompile成功を確認済み。実ゲームでのProbeのALL PASSをこのレビューで実行・採用したわけではない。

### 初版の不変試験に対する必須修正（解消済み）

1. **現在の台帳を測定する。** Journal(core)が`Field(core,"savedLedger")`をCanonical化する初版は不十分。CoreLedgerはCommit／BeginApply／Recover等でprivate `state`をcopy-on-writeし、CoreWorldComponent.savedLedgerはInitialize／Savingの時に更新する。このため、既存operationの状態やentity revisionを変えても古いsavedLedgerだけが不変という偽PASSの窓がある。`Field(core.Ledger,"state")`を直接読み、現行の全record／spec／stamp／digest／result／recoveryを測定する。HeaderのNextSequenceだけでは、新IDを割り当てない既存証変更を検出できない。
2. **測定用snapshotで状態を正常化しない。** SaveSnapshotはValidateとUpdateCountsを実行し、食品証参照の補完などの経路もあり得る。照会後にSaveSnapshotを先に呼んでから比較すると、count-only変更等を修復して偽PASSにする。baseline準備に一回snapshotを得ることはできるが、その後の不変検査は`Field(service,"state")`の全fieldをCanonical化し、SaveSnapshotより前にraw Colony状態を比較する。rendererの複数frame前後にも同じ規則を用いる。Probe測定自体が行う補完を、照会が無変更だった根拠に含めない。

ここでの指摘はProbeの観測設計であり、現行previewがその変更を実際に行うという不具合報告ではない。製品コードの静的レビューではID・journal・food mutation経路を見つけていない。

修正後のJournalはCore.Ledgerのprivate stateを直接Canonical化する。Bindのsite取得もraw Colony stateから行い、SaveSnapshotは照会前のbaseline準備に一回だけ呼ぶ。反復照会・追加翻訳照会・描画後の判定はRawColonyを最初に比較する。BeginRenderには最初のrawState／journal／native／clockを渡し、追加照会や画像準備後に基準を取り直さない。これにより今回指摘した観測上の2つの窓を閉じた。再読範囲で追加の必須修正は見つからなかった。

### 入力fixtureとassertの確認

- 通常の正入力はINH_MaintenanceResumed。INH_MaintenanceStoppedは住民返還済みのため正入力にはできない。Bindはresident2件、person／animal、元入庫Caravan／native ID、唯一の永久binding、独占Core holderを必須にするので、返還済み／不一致fixtureが空候補だけで合格する構造ではない。
- CityLockはcleanPrepared由来の新INH_DelegatedUiCityLockを使う。旧maintenance fixtureと名前だけで同一扱いせず、Applied human debit＋Prepared第二証、同city lock、animalにpending debitなしを実際に確認する。旧fixtureの「護衛は未登録」という前提を新試験に持ち込んでいない。
- person／animalの需要はnative FoodFallPerTick、既存Quote、新Core候補Quoteとexactで照合する。食品数量・HP・def／stuff・元owner／native参照を照合し、受取別NutritionForEater／WillEatも確認する。供給元の実際の栄養食品を全件列挙し、元食品参照ごとに一行であること、少なくとも一つの対応食品があることをassertする。
- city-lock中はsummaryと全食品行を拒否し、humanのRecipientPendingRationsと独立animalを区別する。既存管理画面のCanRelease=false／CityLockedも別に確認し、previewはlock解除・復旧を呼ばない。
- 原物比較は同一object／native ID／owner、対象個人のneeds・年齢・技能・健康・gene・装備・inventory、食品comp、spent物品とcomp、隊列tile／構成、時計を含む。世界中の全map物品・全未登録物品の完全な個数監査とは異なるので、その範囲の実証を広げて記載しない。
- 描画fixtureは開始前のpaused frameで既存維持費Holdを落ち着かせてから計測する。通常Core updateの正当な初期Holdと、preview自身の変更を混同しない。Probe自身はTick／plan control／native transfer／recovery／SaveGameを呼ばない。
- 画像生成とRendered、描画後frameの不変比較はrepaintの確認になる。画像の存在とbyte数だけで長文・scroll・tooltipの視認性をPASSとしない。実画像の目視確認・実クリックは別証拠である。

### まだ含まれない実証

このfixtureに現物食品のcargo履歴一致が0件の場合、空historyの照合はできるが正のHistoricalCargo／HistoricalJourneySupply拒否は実証できない。Probeはその場合のEVIDENCE LIMITを明示する。全transport段階の正一致、未登録source、新規unsupported Gene／food、read-only collection変更拒否、world unavailable／load-save gate、長名・Unknown値などの専用fixtureをこの通常入力だけで網羅した扱いにしない。

actual languageのformatter一致は表示される理由を確認するが、記録したtranslation-error一覧が空であることや全53キーの実Verse読込を一度に証明するassertではない。静的XML検査、実ゲーム起動時の翻訳記録、目視画像結果を分けて扱う。

修正後の再読とRootのステージング再compileは完了している。独立fixtureの実ゲーム実行が完了するまでは、現在の結果を補給previewの実測済み証拠とせず、実物補給の未実装契約も維持する。

## 追加：0.4.13製品への統合差分確認

Rootは0.4.12を凍結後、既レビューのreadonly照会・Windowを0.4.13へ取り込み、full compile成功を報告した。新10試験は実行中であり、以下は統合差分だけの独立読取確認。以前の未統合レビュー／未実測記載は、その時点の記録として残す。本担当は製品・Tests・DLL・ゲームを変更していない。

**統合差分に追加の必須修正なし。** DelegatedCustodyWindow.OpenSupplyPreviewは指定city／entityと同じserviceを新Windowへ渡し、WindowStackへ追加して返すだけである。入庫・出庫・食料移管・補給receipt作成を呼ばない。

居住者右列のy+44に「Food preview／食品候補の確認」の入口を追加しており、返還ボタンのGUI.enabledを元に戻した後で描く。このため設定offやCityLockedで返還できないresidentも、readonlyの候補・理由を確認できる。元隊列名のlabel幅を右ボタン列の手前まで狭め、全文tooltipを付けている。右列ボタンはy+44から74、理由はy+84から始まるため、追加ボタンと理由のRectは重ならない。長い元隊列名の全文はtooltipで確認する。

日英のDelegatedCustody.xmlに入口キーを確認した。入口はpreviewの名称であり、移管実行と誤認する「補給する」ボタンにはなっていない。対象は表示中residentのentityとcityで、選択中の別隊列を補給先として渡さない。元隊列の再発見と適格性は既レビューのWindow／serviceに任せる。

Tests/SupplyPreviewProbeの描画も同じOpenSupplyPreviewを呼ぶ形へ変更されている。通常製品入口methodとreadonly window接続を通るが、管理画面のresident右列をポインタでクリックした証拠ではない。実マウス操作、GUI scale、長名tooltipの視認性は必要に応じ別に確認する。

Core7／Colony1の保存形式不変と既レビューstageソースの同内容統合はRoot報告による。今回の担当確認は入口method、右列Rect、label・tooltip、Probe接続に限定した。新試験・描画の全完了や0.4.13配布完了は、この追記では表明しない。実物補給Prepare／Apply／Cancel、生活、Core新版の未実装境界は維持する。

