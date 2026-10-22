# RimWorld 1.7への制作移行 — 2026年10月17日

## 2026年10月17日の更新

導入済み現物は **1.7.4979 rev109**。Assembly-CSharp SHA `A97A01C4319414414C0CA39F3565215E83300DC608DC94B69A27720095B4E751`、MVID `425a5939-54d7-432a-8f44-e3076b25fce9`。専用静的監査seal `87A1F4D821D454013E4A172BC51E07376E3989A2537A69351CE75870FF06AC5B`。下記rev1248は前日の履歴。

v3で新規World6国家/24都市・拠点1を実保存し全文snapshotと時計が一致。ただしRun004はengineによる通常ログ整理/回転で保全FAIL。Run005はAppContainerの私有KnownFolderで通常root完全保全と保存を確認したが、headless 0x0 UI scale ERROR1を保持。いずれも全面互換/cold成功へ昇格しない。

`-savedatafolder`はengineのpersistent/log rootを隔離せず、`-NoFileLog`でも旧Unityログ削除と終了時backlog書出しが残ることを実ILとRun004で確認。正式Windows KnownFolderを私有化するAppContainer tokenを起動条件にし、通常hostでの起動拒否・通常root全census・私有log/PID照合を追加した。従来のUnity起動引数だけに依存しない。

最新版 `work/rimworld17-save-roundtrip-probe-stage-v4` Build004はcold用LoadedGame hook、DevMode True/PauseOnLoad False、native autostart入力全hash、scope/plan/city/World/Colony全文hash・map0・3時計exact比較を実装/compile。Root sourceレビュー済み。v4はbase-only warm001/cold002とfour-dlc warm003/cold004、各1組計4プロセスが正常exit0・保全0・Root/独立raw確認済み。RootLimitedAll4.json SHA71DBFC9E96F2F0C39EA2ADFE791265FA09A295E2B961673C0E18FA962DD7D885。保存と別process再読のscope/plan/24city/map0・MOD World/Colony全文・3時計が一致。four coldにはIdeologyのhidden Funeral補完WARN12件があり、思想データや全ゲーム状態同一性を証明したものではない。Core10の無修正compile-onlyと25file私有packageの独立照合も完了したが、Core10 native/Scribe/移行は0。現在地はResumeCurrentを優先する。描画なし試験のDevModeは画面サイズ診断の分岐条件でもあり、描画成功の証拠ではない。


ユーザー指定により、今後の《継承圏》の主対象は **RimWorld 1.7**。六文化・HAR種族・建築・武器・Genes・定住・多拠点・外交・占領の仕様は維持する。Royalty・Ideology・Biotech・Odysseyを対象とし、Anomalyは対象外。

## 現物と公開状況

2026年10月12日の[公式発表](https://ludeon.com/blog/2026/10/announcing-the-waylith-engine-and-update-1-7/)では、1.7はWaylithエンジンのunstable版として公開され、通常配信は1.6のまま。公式は多数の既存MODと保存の互換性を案内し、MOD作者には実際に試してから1.7対応表示を付けるよう求めている。互換性の一般的な案内を、このMODの試験結果に置き換えない。

ユーザーが1.7を導入し、10月15日の実体は **1.7.4977 rev1248**。flat `RimWorld.exe`、.NET10/Waylithの本体を別workへコピーして試験した。CSharp SHA `3D4F1DDFE71BD8760CB488255E3BDEAC9366F537DC1EF813CC6F8D09824E2DA9`、MVID `de18e239-08d3-45a3-b8ee-b67b13536b54`。rev926と旧Unity1.6の実体pinは再利用しない。最新静的監査は `work/rimworld17-rev1248-audit-stage`、Seal `170A05A94B9DB144953D98FC4BDBEF7B458CC3D97ECED82EEB2476E28B74C7B5`。

旧Core17/Colony/Worldの三DLLは、DLCなしと対象4DLCの各1プロセスで起動・型ロード・3 ModuleDefs/schema9登録を確認した。捕捉stdout/stderrのERROR0、WARN8を独立レビュー済み。これは起動専用で、実World/マップ・保存・別プロセス再読・HAR・GeneDefs効果・ゲームプレイの互換成功ではない。最初のbase試行にあったOverwolf由来の早期ERRORは別試行として保持し、子プロセス限定のmanifest指定環境変数を使った新試行と区別する。`-logFile`指定でもPlayer.logは生成されず、stdout/stderrとprobe証拠が原記録。

HARの[作者リポジトリAbout.xml](https://github.com/erdelf/AlienRaces/blob/master/About/About.xml)は前回確認時点で1.6までを記載していた。HARの1.7実互換性は未確認で、六文化をxenotypeだけに置き換えない。

## 実装の扱い

- 0.4.16/0.4.17の製品、配布ZIP、元ソースと1.6試験証拠を保持する。1.6の成功を1.7の成功数へ加算しない。
- 保存形式schema9/10/11はMOD内部の版であり、RimWorldの1.7とは別。ゲームの版変更だけで保存schemaを変更しない。
- 次のゲーム統合は新しい1.7用コピーで行う。既存のsealed stageと元のhard pinを変更しない。
- 純粋な台帳モデル、定義設計、六文化の画像・バランス・翻訳など、ゲームAPIに依存しない部分は制作を続けられる。
- 実確認前に配布About.xmlへ1.7を追加しない。開発対象と動作保証を区別する。

## 次の工程

1. **W17-01 環境同定**：正規の1.7本体を用意した後、実Version、起動ログ、実行ファイル、参照DLL、HAR/Harmony、DLC、コンパイラを記録し、SHAとAPIを固定する。ユーザーの通常設定・セーブは保全し、試験はwork内の隔離環境。
2. **W17-02 参照とhookの再監査**：Core/World/Colonyをその実参照でビルド。旧Unity参照の要否、SaveGame/SafeSaver/DoSave/delegate、署名・token・IL・例外経路、終了処理を現物で確認。1.6のtokenやDLL SHAを1.7へ流用しない。
3. **W17-03 最小実起動・保存**：DLCなしと対象4DLCでの起動専用試験は成功。次は新規保存・別プロセス再読込。`work/rimworld17-save-roundtrip-probe-stage-v2` のBuild001は実1.7参照でcompile-only成功、quicktest正規生成経路を静的確認し、World/Colony全snapshot比較を追加した。元v1/Build003とP2レビューは保持。実World/保存/coldは0、cold mode未実装。v2独立source review、部分保存の拒否、専用context/runner、clock/pause・正常終了と全ログ判定を固めてから起動する。
4. **W17-04 旧保存と未完成補給**：コピーした旧1.6保存の移行、Journal11 strict format2、責任保持、二重操作拒否を検証し、その後に食品実移送・保存完了連携へ進む。通常画面・自動保存・保存終了の接続は別途確認する。
5. **W17-05 HARと六文化**：男女・大型種族・方向別画像・装備・生成・保存再読・通常生活を1.7で試験。成功範囲を記録してから配布側へ1.7対応を追加する。

作業配分は既存のSol中心方針を維持する。移行設計・API/保存監査と実装はSol high、限定した難所はSol xhigh、固定済みXML・翻訳は必要に応じてLuna。旧版の未完了試験は比較資料として保持するが、今後の主対象を1.6へ戻さない。

## 開発用の対象確認

`work/rimworld17-preparation/TargetPolicy.json` に主対象1.7を記録した。`InspectTarget.ps1` はVersion.txtと候補参照のSHA/assembly名を読むだけで、ゲームやDLLコードを実行せず、Steamの分岐や設定も変更しない。

```powershell
& 'work/rimworld17-preparation/InspectTarget.ps1' `
  -GameDirectory 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld' `
  -RequireTarget
```

旧1.6での `TARGET_VERSION_MISMATCH` 拒否結果とrev926の検出結果は保持。Inspector単体の `BuildReady=false` / `CompatibilityVerified=false` は版検出だけで互換を保証しない境界であり、上記の限定的な実起動結果とは別に扱う。


## 2026年10月17日 — Core10初回nativeは初期化失敗、修正へ

【Core10専用probeの進捗】Build-001 compile-only/new pure38は成功。base warm001（native10100/runner20388/exec64553）は初期化のfixed-thread ERROR1で保存前に失敗。Rootが所有PID/パス/開始時刻照合で早期終了、native exit -1、post preservation0/evidenceErrors0。新rws/request/result0、stdout全読WARN9/ERROR1、stderr空。強制終了のため私有engine logは未生成、raw読了を主張しない。RootWarm001FailureReview SHA BE6DDB99D88624F460B8D572BC93DFED89030A3302724D80D0511CDEC7F8DA2B。進行中Native0、Core10保存成功0、旧v4/製品57成功には加算しない。失敗owner/Runtimeを保持し、cold/four源へ使わない。次は独立stageの初期化context修正をレビューして新構成で再試験。9→10移行/Prepared行/実食品は未確認。

初期化の新規FinalizeInit→ValidateHistoricalが固定threadを要求するModelへ到達する。actual1.7のworld生成はworkerで行われ、Find.Worldへ割当前のFinalizeでもあるため、threadとactive worldの両方が揃うまで既存Updateの初期化経路へ遅延させる修正を検討。Modelのthread/world guardやcodecを緩めない。旧Core10 compile/package/probe/失敗証拠は変更せず、修正source・compileを別stageへ置く。RTK.mdを新たに読了、関連コマンドまとめと圧縮出力の扱いに従う。


## 2026年10月17日 — 現物1.7.4980 rev60を別audit

【最新版への追従】再開時の現物は1.7.4980 rev60、CSharp EFF7D97949F919FB2B151EBFB906E9BE749B63410B7DBDA78EEEBB7972B245C3、MVID8e962ecc-7ac7-42d2-8f3b-1b16cd24d2cd。work/rimworld17-rev4980-60-audit-stageで新30refsとmetadata/保存・時計IL/ログroot3DLL/初期化thread ILを新規採取しstatic4run成功、180selectedAPI差0、seal8A43489E78BEC074648613950A31E50317F91BFD5FEC71183B113918ACEAD17F。numeric field/type tokenを含む比較のため全binary意味同一は主張しない。旧4979のnative成功/失敗を4980へ計上しない。init-context-stageは54source、53完全同一・FinalizeInit条件1箇所変更、source準備sealC625ED65…。新auditを使うcompile-only待ち、最新native保存成功0。
