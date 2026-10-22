# 通常の保存画面への連携 — 2026-10-11

保存入口を止めるだけでは、ゲーム側の「保存できた」という後続処理を止められない。実物移送を有効にする前の残課題として、一次DLLの静的監査を行った。通常UIの実操作やauto-saveの動作試験はまだ行っていない。

監査資料は `work/p04-native-save-refusal-caller-audit`、封印SHA `8C675D042B83BB2672E33CC97E3F3717E1029FB1BFB874CC437449B7CEE0962B`。RimWorldの実DLL SHA `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`をmetadataとして読む方法で全89,845 IL bodyを調べた。ゲームDLLのconstructorやstatic初期化、ゲームAPI、Harmony hookは実行していない。

| 呼出し元 | 確認した問題 |
| --- | --- |
| 通常の保存画面 | 保存callbackをqueueした直後にSavedAs・成功表示・knowledge保存・画面閉鎖を行い、保存結果を待たない。 |
| due auto-save | callbackの実行前に周期をresetする。保存入口の拒否で実保存がなくても、周期が消費され得る。 |
| permadeathの保存してメニュー／終了 | void SaveGameの正常戻り後にworld解放やshutdown予約へ進む。保存を拒否しただけでは止まらない。 |
| permadeathの名前変更 | 新名でのDoAutosaveが正常戻りした後に旧保存を削除する経路がある。 |

SaveGameは内部の書込み例外をcatchしてvoidで正常に戻る。したがって「admitted」「例外が来なかった」「voidが戻った」「lastSaveTickだけ変わった」はWrittenの証明にしない。auto-saveには保存先の回転indexがなく、未存在名または最古mtimeの名を選ぶ。mtime不変は今回の静的監査や既存native6では未測定。

対応は、既存SavingIsTemporarilyDisabledとCoreの保留を合成する事前判定に加え、既存の保存画面・queue済みcallback・SaveAndQuit・名前変更の各要求と結果を結び付ける必要がある。要求ごとのworld・thread・名前・識別子を持ち、拒否時は成功表示・周期消費・world解放・shutdown・旧保存削除をしない。queue後に状態が変わった場合も再判定する。一般I/O成否やflushの原子性を、拒否検査と混同しない。

独立モデルとAPI adapterをwork内で作成中。保存保護native-v4の6実試験は、同名／新規保存のファイル不変、通常解除後の保存再読、例外後の拒否維持を確認した限定結果。その6件を通常保存画面・実食品移送・永続台帳の完成証にはしない。配布0.4.17へ保存保護試作は含めていない。

独立モデルの最終準備を完了した。`work/p04-native-save-request-outcome-stage`の封印SHAは`9F43ED49F9491CFE6F8D95863AE17670320315A8C61838C0714AD23A0C404A3F`。純粋モデル96件、合成XML11件、模擬リンク属性4件と固定ゲームAPIでのcompileが成功した。Rootは封印と既存結果を読み取り検証し、最終モデルとadapterをレビューした（`work/SaveRequestOutcomeRootReview-20261011.md`、SHA `CA6A8F71ABBEDB0C559E270AC712CB2383D70843CB9E9B241D4E3074E77E7634`）。ゲーム・実caller・実adapterは動かしていない。

新しい`work/p04-native-save-completion-bridge-stage`では、要求とSaveGame入口、SafeSaverの完了を結び付ける部分を作成中。SafeSaverが例外を出してSaveGame内部でcatchされた場合も、元の書込み失敗を残す設計にする。最終封印・実ゲーム・通常画面接続はまだ行っていない。実junction試験はOSに拒否されており、模擬リンク属性試験を実リンク成功とは扱わない。

2026-10-12 保存completionの実ゲーム検証を開始。新v3は既存の全Core比較を維持し、完了validatorが使うXmlReaderと同じ方法で期待ハッシュを別に算出する。元v2の実保存は同一bytesだったが、Core内一つのtextnodeの311 CRLFが読み方によりLFとなり、判定が一致しなかった。モデル/validator/bridge/保存XMLの変更はしない。

v3のDLCなし初回はRoot session56866 exit0、actualPID41376/exit0、source/input/native/config/全保存current+kept/ログ/固定4ZIPを確認して成功。新規保存・同名上書きの双方で要求に対応する完了証と全Core一致、lease中の4拒否、食料・住民・時計・元の所有者が不変。成功後の動作は模擬manual effectsだけであり、通常画面・自動保存・保存終了・改名にはまだ接続していない。cold/failureと対象4DLCは未完了、製品57に加算しない。原証拠はwork/p04-native-save-completion-validation-v3/Runs/base-only/Attempts/warmおよびwork/NativeSaveCompletionV3-base-only-warm-RootVerified.json。期待内容の汎用取得、権限付き要求、ファイル同一性の保護、永続T2も残る。
2026-10-13 保存completion v3 正式限定証拠採取完了: Root Collector005 session76223 exit0、Docs/Evidence/P04NativeSaveCompletionV3/all-6/ActualProof.json とCollection.json/Result.mdをCreateNew作成。元6actualResults/全RootStartVerifiedConsole/native/pid/config/fullRaw/current-kept/source/fixture/seal/四ZIP、fullhistorical/currentphasecensus、両Runtime actualcleanup+absenceを再照合。全raw・実rwsはwork原位置保持、過去FAIL/003004/source/sealも保持。新base3/four3計6だけでrelease57追加0、trustednativebaseline/fake manual effectsのみ、実UI/食品移送/T1T2/fullWorld/fsync/opaqueauthority/alias未対応。今後この6を再実行・製品57へ足さない。次優先はCore10freshv6 Prepared作成/Cold再読、T0puremetadata19のFullWorld統合は後続。
