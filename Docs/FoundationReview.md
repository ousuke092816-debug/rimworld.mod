# 継承圏 P01土台・P02設計 独立レビュー

日付：2026-10-07（日本時間）。担当：GPT-6.1 Sol / high の代替独立レビュー。指定のAstraレビューは使用量上限で途中停止し、本結果をAstraによる確認済みとは扱わない。

対象：Source/Core、Build、Tests/SmokeMod、Docs/CoreArchitecture.md、仕様書の依存・保存要件。実装・ゲーム本体の変更なし。ゲーム本体DLLは読み取りのみ。ログはレビュー時点のもの。

判定：最小MODの読込とヘッダー単体の保存往復は確認できる。P02設計は所有・保管・更新責任を分離し、予約・移管・地区の二重処理を避ける方針で元仕様と整合する。以下の保存上の問題を解決してから資産台帳へ進む必要がある。P02実装完了ではない。

## 確定した指摘

1. **P1：保存版がXMLから省略される。** `CoreWorldComponent.cs:24` は現行版をScribeの既定値にも指定する。実際の `work/inheritance-smoke/base-only/SaveData/InheritanceHeaderTest.xml` に `schemaVersion` が存在しない。将来この既定値を版2へ変更すると、既存P01ヘッダーを版2と解釈して版1→2の移行を迂回する。版番号は常に保存し、既存の版なしP01ヘッダーは固定で版1と解釈する。版なし旧ヘッダー、明記した版1、版2、未知の将来版をそれぞれ試験する。

2. **P1：未知版の例外だけでは読込拒否を保証できない。** `CoreWorldComponent.cs:18–19,26,38` は例外を投げるが、実ゲームDLLの `Verse.PostLoadIniter.DoAllPostLoadInits` と `RimWorld.Planet.WorldComponentUtility.FinalizeInit` は当該例外をログへ記録して処理を継続する。将来版のフィールドを理解しないまま保存すると未知データを失う可能性がある。互換判定を明示的な状態として保持し、対応不能時に更新・台帳書込を開始させず、原本を損なう保存を防ぐ実際の経路を検証する。設計第8節の「対応不能として明示」だけでは続行・再保存の挙動が未確定。

3. **P2：記録した対象revと実ログのrevが違う。** `CoreArchitecture.md:6,269` と `RunSmoke.ps1:34` はrev590、実行済 `base-only/Player.log:21` は `1.6.4871 rev591`。API調査・試験の基準を実DLLとログの版へ統一する。保存設定に書いた版を実行版の証拠にしない。

## 未確認と試験の限界

- 現行SmokeModが検証するのはCore XML、Mod初期化、SaveHeader単体のScribe往復と再初期化。`SmokeMod.cs:29–41` はWorldを作らず、実WorldComponent登録・世界保存・読込・FinalizeInitを通さない。現在のALL PASSを実ゲームのセーブ再開合格へ拡張しない。
- レビュー時点でbase-onlyログに上記4項目のPASSとALL PASSがある。四DLC構成、実世界の保存再開、設定画面操作、未知版・破損・ヘッダー欠落はこのレビューでは実行していない。親の後続試験結果で別途判定する。
- 将来の台帳・個体参照・実物移動・地区保存・占領・移行は設計のみ。実物アダプターの証拠と中間保存からの復旧試験がそろうまで、二重加算防止の実装合格としない。

## 設計の整合確認

依存表は元仕様第12節と一致。Species/GenesだけがBiotechを必須とし、World/Colony/文化建築/武器は相互に必須依存を作らない。Startsの八ルートは各Speciesの存在条件を持ち、WorldにEnemyがない場合の実体生成禁止も明記されている。Coreは任意モジュールへのコンパイル参照を含めない。現ModuleDefは識別子・protocolVersionだけの試作で、登録検証・モジュール保存版・欠落検知は未実装として扱う。

IDの安定性、単一保管先、抽象/実物の正本切替、保存できる操作状態、原因ID付き再処理、資産保持の失陥都市について、設計内に確定した矛盾は見つからなかった。実装時には参照解決→照合→復旧→更新再開の順序を実ゲームの呼出順へ対応させる必要がある。

## 修正追跡・追加レビュー

上の指摘と未確認欄は初回レビュー時点の記録。以下を現在の判定とする。担当は同じSol / highであり、Astra確認には置き換えない。

- **保存版省略：修正確認。** 現コードは `Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1, true)`。固定の旧版既定値と強制保存が入った。`INH_WorldSmoke.rws`の実WorldComponent内に版1とSaveScopeIdの両方があり、四DLC `Header-Player.log` に `Schema version written explicitly` がある。版なしP01ヘッダーの試験コードも追加されたが、その追加項目のPASSログはこの追跡時点では未確認。版2移行は未実装。
- **実WorldComponent保存・再開：追加確認。** `base-only/WorldSave-Player.log` は `PASS new world component and full game save`、別プロセスの `WorldLoad-Player.log` は `PASS full game reload preserved world identifier` とALL PASS。通常版の世界保存・再開の未確認は解消した。四DLCについて確認したのはヘッダー往復であり、同構成の実世界保存再開へ拡張しない。
- **実行版の表記：修正確認。** CoreArchitectureとRunSmokeは実ログのrev591に統一され、Version.txtのrev590との差も説明されている。
- **未来版99拒否：未解決。** IsCompatibleと互換性ガードは追加されたが、`FutureLoad-Player.log` は `play=True, entry=False, longEvent=True, schema=99, guard=True` の状態を反復し、メニュー復帰成功の記録がない。ヘッダー単体の互換判定成功を、ゲーム読込拒否の成功として扱わない。

### 未来版試験の停滞に関する実DLLの根拠

`LongEventHandler.UpdateCurrentSynchronousEvent` は `currentEvent.ShouldWaitUntilDisplayed` がtrueなら処理を実行せず戻る。この条件は `!alreadyDisplayed && UseStandardWindow && eventText非空`。`UseStandardWindow` は `canEverUseStandardWindow && !doAsynchronously && eventActionEnumerator == null` で、`DrawLongEventWindowContents`のGUI Repaintで初めて `alreadyDisplayed=true` となる。

未来版ログに出ているイベントは同期、通常ウィンドウ許可、表示未完了、Loadingの文字列ありで、上の待機条件に一致する。現Coreの拒否処理は同条件の同期イベントを登録している。長い読込が終わらないことと、拒否イベント自身が描画を待って実行されないことを区別する必要がある。ログと実DLLからは後者が強い原因候補である。

引数について初回の改善案を訂正する。対象rev591実DLLの `MethodInfo.GetParameters` で、Action版オーバーロードの第5引数は `showExtraUIInfo`、第6引数は `forceHideUI`、第7引数は `callback` と確認した。`canEverUseStandardWindow` を直接指定する引数ではない。第5引数falseだけで通常ウィンドウを禁止するという説明は誤りであり、親の実ログでも待機継続となった。

親は第6引数 `forceHideUI=true` を明示した版を再試験中。ただし実DLLではこの欄は `LongEventsOnGUI` を早期returnさせる一方、`ShouldWaitUntilDisplayed` と `UseStandardWindow` の条件には入らない。この変更だけで描画待ちが解消するとは断定できない。`canEverUseStandardWindow` はイベント登録時の `!AnyEventWhichDoesntUseStandardWindowNowOrWaiting` から決まる。

描画待ちを直接外す別案は、同期拒否イベントの `textKey` をnullまたは空にすること。`LongEventsUpdate` はこの場合eventTextを空にし、`ShouldWaitUntilDisplayed` の非空条件が成立しなくなる。`GenScene.GoToMainMenu` 内部のEntryイベントは非同期登録であり、そのfalse引数を「通常ウィンドウの直接禁止」と説明した部分も訂正する。いずれの変更も、batchmodeで `Unsupported save schema 99` → Entryシーン・Current.Gameなし → ALL PASSと原本セーブ不変までを改めて確認する。改善案はこの担当では実装していない。

なお、`Root_Play.Update` はLongEvent待機中にGame.UpdatePlayへ進まず、GameComponentUpdateも呼ばれない。拒否検査の追加だけで待機中のイベントが進むとは限らない。FinalizeInit内の直接メニュー復帰については、親が観察したMapDrawer未初期化の失敗を踏まえ、マップ復元完了後の経路を採用する。

## 最終修正確認

今回の最終読取時点では、初回の具体的な保存上の阻害3件は、P01最小ヘッダーの範囲で解消した。コード変更は親担当のみ。独立レビューはSol / highによる代替のままである。

- 拒否イベントは `textKey=null` の同期登録へ変更された。最新 `base-only/FutureLoad-Player.log` で `Unsupported save schema 99` → `[INH-FUTURE] MAIN MENU` → ALL PASSを確認した。対象ログに例外・読込失敗は見つからない。親のRunSmoke実行結果で終了コード0と未来版原本のSHA256不変を確認した報告があり、現RunSmokeにも両者を必須とする照合処理がある。これにより上の「未来版99拒否：未解決」は解消した。
- 最新 `four-dlc/Header-Player.log` に `Unversioned prototype stays schema one` とALL PASSがあり、版番号省略の旧ヘッダーを固定で版1と扱う試験も確認済み。
- 最新の通常版 `base-only/WorldLoad-Player.log` に `PASS full game reload preserved world identifier` とALL PASSがあり、未来版拒否修正後も通常セーブを別プロセスで再開できている。

現在の確認で残る、今回の最小保存土台を阻害する具体的な不具合は見つからなかった。次の点は引き続き完成根拠の限界である。

1. 通常の設定画面を目視・操作した確認は未了。batchmodeの成功だけで表示・翻訳・入力操作を合格としない。
2. PC内で確認した実行版は1.6.4871 rev591。Steam公開の最新安定版のrevとの一致は、このレビューでは確認していない。
3. P02の完全台帳、移管ジャーナル、版2移行、モジュール欠落検知、実物との照合は未実装。未来版の資産を業務更新より前に停止する保証は、ヘッダーのみの今回試験からは導けない。台帳実装時に共通サービスの互換・復旧ゲートを設けてから更新を始める。
4. 四DLC構成はヘッダー試験のみ。四DLCの実世界保存・再開、既存バニラセーブへの追加、破損ヘッダーを含む広い回帰確認へは結果を拡張しない。
