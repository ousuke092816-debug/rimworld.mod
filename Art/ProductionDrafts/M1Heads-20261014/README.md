# M1 星墓 — 方向別頭部の試作

2026年10月14日。採用済みSpecies-v10-strongerのM1男女を参照し、built-in image_genで頭部だけの透過PNGを6枚制作した。元の採用画像と生成原物は保持。生成後の画像加工は行わず、SHA一致を確認してworkspaceへコピーした。

| 性別 | 正面・south | 右向き・east | 後ろ・north |
| --- | --- | --- | --- |
| 男性 | [PNG](M1Male_Head_south.png) | [PNG](M1Male_Head_east.png) | [PNG](M1Male_Head_north.png) |
| 女性 | [PNG](M1Female_Head_south.png) | [PNG](M1Female_Head_east.png) | [PNG](M1Female_Head_north.png) |

鉱物皮膚、角張った頭、淡い鉱脈、控えめな目と輪郭を維持し、正面/側面/背面を作成した。男女の違いは例示であり、全個体の体格や生理の固定仕様を決めない。

[表示サイズと背景を変えて比較するプレビュー](Preview.html)も保存した。ブラウザで素材を表示するだけで、実ゲームの描画試験ではない。

各PNGは1254×1254のARGB。四隅のalpha=0、中央のalpha>0をデコードして確認した。これは全画素のalpha検査ではない。背景の黒は表示側の透過表示であり、黒い背景を描いたファイルではない。[元ファイル・寸法・SHA・参照順](ImageManifest.json)と[生成指示全文](Prompts.md)を保存。

**ゲーム投入前の素材試作**。HARへの接続、1.7での表示、実倍率での識別、描画サイズ、首の接続位置、方向間の鉱脈の繋がり、衣服/帽子/髪との重なり、圧縮とmipmapは未確認。1.7の描画仕様や必要なテクスチャ寸法を推測して完成品へ組み込まない。身体素材・M2〜M6の方向素材は別途制作する。
