# ゲームの検証対象ビルド

2026-10-09。既存・現在の実ゲームログで RimWorld 1.6.4871 rev591 を確認。Steamのローカル appmanifest_294100.acf では buildid 23969874、StateFlags 4、TargetBuildID 0、beta branchの指定なしを読取確認した。ユーザーのSteam設定やインストールは変更していない。

ゲーム Assembly-CSharp.dll の検証SHAは 5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A。HAR 1.6 AlienRace.dll は 9B5DC05932E9DFAA963C2FA5253127E050B074686468E03C145831DCF2D7F626。色保存の新試験契約では UnityEngine.CoreModule.dll C5C58EA254834291780A1D6C388C241443D07167B4A4B890A23C9494F626DDBA も入口で固定照合する。

[公式Steamニュース一覧](https://store.steampowered.com/oldnews/?appgroupname=RimWorld&appids=294100&feed=steam_community_announcements&headlines=1)の読取結果には2026-06-08の1.6.4850告知がある。この一覧だけでは、告知後の小規模な修正や現在のpublic branchのbuildidまで確定できない。ローカルの4871を古い4850へ戻さず、現在インストール済みの4871を固定して制作・試験を続ける。

「現在インストール済みのビルドで試験済み」と「Steam public branchの最新buildidをサーバーと照合済み」は別。後者は未完了。今後実バイナリが変わった場合、保存方式・HAR・APIの固定hash根拠を新バイナリで再確認してから新しい試験として数える。
