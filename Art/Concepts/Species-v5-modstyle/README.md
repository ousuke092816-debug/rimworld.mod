# J案 — 種族MODの小さなポーン表現を参考にした五種族

2026-10-10。ユーザーはH案をリアルすぎるとし、[種族追加MOD一覧](https://rimworld.2game.info/tag/%E7%A8%AE%E6%97%8F%E8%BF%BD%E5%8A%A0/)を参考にするよう指定した。

一覧から公式Workshopへ進み、[NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166)のポーン集合画像、[Wolfein Race](https://steamcommunity.com/sharedfiles/filedetails/?id=3473140562)の4ポーン画像をブラウザーで視覚確認した。[Kiiro Race](https://steamcommunity.com/sharedfiles/filedetails/?id=2988200143)はタイトルプレビューを補助的に確認した。全掲載MODの画像を精査したという主張ではない。

観察をもとに、丸く大きい頭、短い胴、小さな鼻口、髪を大きな色の塊にする描き方を独自の五種族へ反映。既存キャラクターの画像素材は使用せず、言語化した表現上の特徴からbuilt-in image_genで新規生成した。これは通常倍率での実ゲーム試験ではなく、画像上の選択用モックアップ。

| 番号 | 種族 | 特徴 |
| --- | --- | --- |
| J2 | 鉄樹の民 | 葉、小さな木肌の斑、茶髪 |
| J3 | 炉の氏族 | 銅色の肌、赤茶髪、首の印 |
| J4 | 白冠の獣侯 | 白髪、小さな角、耳 |
| J5 | 潮環の民 | 灰青の肌、鰓、耳の膜 |
| J6 | 空標の民 | 淡い紫の肌、白髪、耳 |

各列に男女一組。J2〜J6は未採用。E1（星墓）はユーザー採用済みで、元sheetのSHA不変を確認した。新画像には星墓を再描画していない。

![J案](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/Inheritance/Art/Concepts/Species-v5-modstyle/J-five-species-pawn-pairs.png)

使用した生成指示の全文はPrompt.md、画像SHAはSHA256.txt。元generated_images画像を保持し、workspaceのcopyとの一致を確認した。

完成HAR texture、男女の最終仕様、南東北の方向素材、addonや服との重なりはまだ制作・検証していない。手足のある比較案が、そのままバニラ/HARの描画方式で動くという主張ではない。全種族を採用した後に、通常倍率で読める顔の情報量と統一した線・塗りを確定する。旧画像・凍結ZIPはそのまま保持。

