# 六文化の HAR 種族・遺伝子・技術交流の統合計画

作成・外部資料参照日：2026-10-09（日本時間）  
状態：ユーザー指定により、六文化の種族に Humanoid Alien Races（HAR）を採用する方針が確定した。以下は未実装の計画である。この調査では HAR の導入、製品の種族 Def 生成、製品ビルド、ゲーム起動を行っていない。既存 0.4.13 の凍結作業や P04 の完了判定を変更しない。

仕様の根拠は [継承圏_設計書.md](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/継承圏_設計書.md) 第2・10・11・12・15節と技術交流の追記、[制作指示書](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/継承圏_制作指示書_モデルと推論強度.md) P09〜P11、[Core 共通契約](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/Inheritance/Docs/CoreArchitecture.md) のモジュール境界である。既存の六種族、巨体の実効、八開始、独立した文化建築・武器、遺伝子庫、技術交流の最終仕様を減らさず、HAR を加えた責務と検証順を具体化する。

## 1. 既存案から確定する差分

最新の先行確認：2026-10-09にCore 0.4.14＋四DLC＋ローカルHARの隔離初期化が成功した。[記録](Evidence/HARFrameworkH0/README.md)に実ロード型と元／Runtime125項目のhashを保存。Colony／World／専用種族はこの試験へ含めず、以下の種族実装は未着手。H0の公式ソースcommitとDLL対応も未確定である。

設計書は種族パックを Core＋Biotech としており、HAR の依存を明記していなかった。今後は六文化ごとの種族を HAR の種族 ThingDef として定義し、その上に Biotech の基礎遺伝子・ゼノタイプを載せる。ゼノタイプだけで六種族を表現する実装案があれば、この部分を置き換える。既に六種族の製品実装や互換確認が終わったという意味ではない。

HAR の実体種族と Biotech のゼノタイプは異なる層であり、併用できる。種族の最小実装には専用 ThingDef と PawnKindDef が必要となる。[HAR Feature Comparison](https://github.com/erdelf/AlienRaces/wiki/Feature-Comparison)、[Getting Started](https://github.com/erdelf/AlienRaces/wiki/Getting-Started)

| 層 | 所有する内容 | 他の層への境界 |
|---|---|---|
| HAR 種族 | 生体の基礎形態、身体構造、基礎体格、描画、成長時の外観、種族固有の最小 Comp | 遺伝子を入れ替えても Pawn の種族 Def を自動交換しない |
| 基礎・改良遺伝子 | 抽出・移植・発現・遺伝できる性質と代償 | 巨体や荷重の効果を種族の基礎値と二重計上しない |
| 文化 | 技術、建築、武器、背景、思想や慣習 | 種族だけで全員の思想、敵意、所属を固定しない |
| 所属・統治 | 国家、都市、外交、政策、占領後の扱い | 同じ種族でも別の国家・思想へ所属できる |

既存 World の種族なし構成は人間主体で成立させる。後から Species を導入しても、保存済みの人間を HAR Pawn に作り直さない。一方、特定種族を保証する開始ルートでは人間への代替を認めず、必要な Species がないルートを表示しない。

## 2. 依存境界と読込順

HAR の公式 packageId は `erdelf.HumanoidAlienRaces` で、公式 About は RimWorld 1.6 を対象に含めている。これを各 Species の必須依存として指定する。単なる読込順の指定だけで必須依存を代用しない。[公式 About.xml](https://github.com/erdelf/AlienRaces/blob/master/About/About.xml)

| モジュール | 必須依存の計画 | HAR 型・Def の扱い |
|---|---|---|
| Core | RimWorld 1.6 | HAR のアセンブリや型に直接依存しない。中立な識別・保存・台帳を維持 |
| Colony／World | Core | 単独構成へ HAR 必須を追加しない。種族の存在は導入済みモジュールから照会 |
| Species：星墓／鉄樹／炉／白冠／潮環／空標 | Core、Biotech、HAR | 種族 Def、専用 PawnKind、最低限の基礎遺伝子、背景を所有 |
| 各文化 Architecture／Arsenal | Core | Species、HAR、World を必須にしない。普通の人間でも利用できる |
| Genes：継承遺伝子庫 | Core、Biotech | HAR を必須にしない。普通の人間・自作ゼノタイプにも使える |
| Exchange | Core | 合作の個々の定義だけ、必要な文化パック・DLC が揃ったときに追加 |
| Starts | Core、Biotech、Settlement | 各文化単独・四種族混成・六種族混成を必要 Species が揃ったときだけ追加 |
| 任意の HAR 適合定義 | 対象 Species／Genes／装備等 | 本当に必要な場合だけ小さな条件付き層へ分離。Core 側に吸収しない |

基本順は「ゲーム・必要 DLC → HAR と Core → Species／独立コンテンツ → 対象の揃った Starts・Exchange・適合定義」とする。実際の About の依存・順序を使って循環がないことを確認する。Genes と Species は互いを必須にせず、両者の GeneDef の所有者を一つに決める。基礎遺伝子は Species、改良版は Genes に置き、条件付き適合定義から参照する。同一 Def を複数パックに複製しない。

Royalty・Ideology・Odyssey は必要な部分だけ条件付きで読む。Anomaly への依存を加えない。HAR 自身にも 1.6 の Ideology／Odyssey 条件付きフォルダがあるが、これだけで継承圏の全 DLC 構成が互換確認済みにはならない。[公式 loadFolders.xml](https://github.com/erdelf/AlienRaces/blob/master/loadFolders.xml)

## 3. 六種族の責務

数値・寿命・交配規則は未確定とし、最初の見本の測定後に決める。外観倍率だけで大型種族を成立させず、基本設備で生活できる条件を六種族共通の受入条件にする。

| 文化・主要派閥 | HAR 側の形態・描画 | 生体・遺伝子として検証する内容 |
|---|---|---|
| 星墓の継承者／碑原評議会 | 鉱物質の皮膚、明確な巨体、積層した輪郭 | 実際の健康、食費、荷重、移動、装備運用の得失。記録・要塞・修復は文化技術 |
| 鉄樹の民／根海共同体 | 人間程度の体格、植物・共生菌の表現 | 農業・環境への適応と負担。治療や生体建築を種族限定にしない |
| 炉の氏族／炉路氏族連盟 | 大柄で筋肉質、熱と重作業に適した輪郭 | 熱・重労働・採掘・荷重の実効と食費等の代償。製造技術は他文化も学べる |
| 白冠の獣侯／白冠諸侯盟約 | 毛皮・角、系統による体格差 | 感覚・移動・護衛等の得失。系統差を最初から別 Species の乱立で表さない |
| 潮環の民／潮衡水務連邦 | 人間程度の体格、青灰色の皮膚、薄い膜 | 塩分・脱水・浸透圧への適応。陸上で通常生活・戦闘が成立し、水中マップを要求しない |
| 空標の民／遠標観測同盟 | 細身、感覚・平衡を表す輪郭 | 動体視力・聴覚・高地適応と感覚過負荷・近接等の弱点。観測や通信は確実な予知にしない |

巨体は、生体の基礎体格・健康、可搬重量、食料の日需要、移動や戦闘への影響を個別に測る。遺伝子による可変の補正と、種族の固定の基礎値を表にして一つの効果の責任箇所を決める。例えば基礎体格で増える需要へ、更に同じ理由の食費補正を無条件に足すような設計を避ける。改良遺伝子で弱点を全て消す万能構成にせず、意味のある代償を残す。

描画の大きさと生体の大きさは別に設定・試験する。HAR の描画サイズは衣服や頭装備にも影響し、頭位置等の調整が必要となる。[General Settings](https://github.com/erdelf/AlienRaces/wiki/General-Settings) 通常のベッド・作業台・医療設備を使えることを先に確認し、専用大型設備は後から改善として追加する。身体構造を増やす場合は、傷・義肢・治療レシピ・防具の被覆部位も合わせて検証する。

遺伝子庫単独で大型の自作ゼノタイプを作る仕様も保持する。骨格・荷重・代謝・装備運用の遺伝子は普通の人間にも利用できる設計とし、独立構成の可変描画にどの実 API が必要かは別途調査する。HAR を遺伝子庫の新たな必須依存にして解決した扱いにしない。

## 4. geneRestrictions と raceRestricted の具体的な設定方針

この二つは検討項目名として扱う。今回参照した公式 master の XML メンバーには、そのままの単一設定名としては見当たらず、実際の制限は `alienRace/raceRestriction` 内で設定する。実装時は使用する HAR バイナリと対応するソースを固定してから、存在するメンバーだけを使う。[公式 ThingDef_AlienRace.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/ThingDef_AlienRace.cs)

| 制限 | 採用方針 |
|---|---|
| `onlyHaveRaceRestrictedGenes` | 初期は false。自文化の遺伝子だけを許す構成にしない |
| `geneList` | 全種族に対する専用扱いを生むため、抽出して他種族でも利用する基礎遺伝子を安易に登録しない |
| `whiteGeneList` | 制限を設けた生理上の例外が必要な場合の明示的な許可。文化所属の判定に使わない |
| `blackGeneList` | 実際に成立しない組合せを実機で確認した場合だけ、具体的な Def 単位で拒否 |
| `whiteGeneTags`／`blackGeneTags` | 文化を分ける独自タグだと思って使わない。広い接頭辞で他の遺伝子まで巻き込む拒否を避ける |
| Endo／Xeno 別の制限 | 原則どちらも開く。必要な差を確認できた場合にだけ、該当する別設定へ絞る |
| ゼノタイプ制限 | 自文化の基礎ゼノタイプを生成の既定に使っても、改良や混成の全ゼノタイプを閉じない |

公式 Wiki は排他リスト・許可リスト・拒否リストを区別している。公式ソースでは共通制限に加えて Endo／Xeno の制限も評価し、遺伝子タグの照合には接頭辞の一致を使う。このため「文化タグを付ければ安全な許可範囲になる」という前提は置かない。[Race Restriction](https://github.com/erdelf/AlienRaces/wiki/Race-Restriction)、[公式 ThingDef_AlienRace.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/ThingDef_AlienRace.cs)

HAR の種族基礎遺伝子は生成時に追加できるが、それだけで保存読込時の自己修復や、不変の遺伝子構成を保証した扱いにしない。また HAR の遺伝子追加処理は制限により追加を拒否し得る。移植・改良の成功は呼出しの終了だけで判断せず、目的の遺伝子が実際に残り、抑制状態や代謝が期待どおりであることを確認する。[公式 HarmonyPatches.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/HarmonyPatches.cs)

生殖は、交流可能な社会関係と分けて決める。混成集団・交際・婚姻を種族固定の敵意で閉じない一方、異種間出生で子の種族・遺伝子・身体・成長段階をどう選ぶかは未確定である。HAR の既定だけを最終生物仕様とみなさず、一組の出生と保存復帰から検証する。全組合せの繁殖可能や固定した不妊を、未検証のまま仕様化しない。

## 5. 他文化の装備・建築・研究へ進む経路

HAR の専用装備リストは「その種族の好み」を示すだけのものではなく、他種族の使用も制限する。文化武器・防具を `weaponList`／`apparelList` に一括登録せず、専用物だけを使うフラグも初期は false とする。文化建築、製作レシピ、研究にも種族排他リストを使わない。[Race Restriction](https://github.com/erdelf/AlienRaces/wiki/Race-Restriction)

通常の進行を次の二経路に分ける。

1. **完成品を交易で得る。** 普通の商人から他文化の武器・防具・遺伝子関連物を購入できる。購入した装備は、対応部位・身体条件を満たす人間と六種族が使える。出身文化の研究を持たないという理由で使用を禁止しない。
2. **研究して自作する。** 基本研究、設計図の入手、必要素材、製造設備を段階として示す。他文化の技術を研究する担当者の種族を条件にしない。文化パック単独でも自力研究や普通の交易で進み、World、別文化 Species、占領を必須にしない。

巨体の防具は対応する描画や製作上の調整を持たせる。身体が違うため一部の部位が合わない場合は、その理由と利用できる代替を装備説明へ示し、文化そのものを閉じる抜け道にしない。まず人間・星墓・もう一種族で、一つの他文化装備を買う／研究・製作する／装着する経路を通す。以後、導入済み六文化の装備を各種族と人間が利用する組合せへ広げる。

合作は Exchange の任意追加とする。星墓＋鉄樹の補修防壁、炉＋空標の射撃設備などを少数から試し、必要なコンテンツが欠ける場合は該当する合作だけ読まない。合作を通常生活・戦闘の必須条件にせず、単独文化の装備を一律に上回る完成品にもせず、用途と負担を付ける。

## 6. 小刻みな実装順と停止条件

P09 の一文化見本 → 六文化展開、P10 の開始・交流、P11 の遺伝子庫という順を維持する。P04 の保管・会計と HAR の種族導入は別の進捗であり、この先行見本を P04 全体の完成に数えない。

| 段階 | 最小変更 | 次へ進むための確認 |
|---|---|---|
| H0：対象を固定 | 隔離した検証構成で HAR、ゲーム DLL、DLC、導入順、製品の版と hash を記録。HAR なしの既存構成を基準に保存 | 公式ソースと使う DLL の対応、重複した HAR の不在、依存エラーの扱いを確認 |
| H1：星墓一体 | Species 一パック、一つの種族 Def、一つの PawnKind、最低限の基礎遺伝子・仮の外観のみ | 生成、選択、食事、作業、寝床、治療、装備、保存読込。同じ種族・Pawn が残り、体格と食費・荷重が実際に変わる |
| H2：一つの遺伝子交換 | 星墓の基礎遺伝子一つと、人間用の改良遺伝子一つで抽出・移植・抑制を確認 | Endo／Xeno、代謝、排他、HAR による拒否が期待どおり。投入物や結果の誤判定なし |
| H3：他文化技術一つ | もう一文化の武器か防具を、Species とは独立した Arsenal へ追加 | 人間と星墓が完成品交易・研究・製作・使用できる。World なしでも進行 |
| H4：元個体の連携 | H1 の Pawn を既存のローカル保管・元キャラバン保管・返還・輸送の試験へ投入 | 同じ実物と HAR Comp・遺伝子・外観・装備が維持され、需要見積もりがその食べ手に一致 |
| H5：成長と第二種族 | 子供から成人、頭・角・装備・寝床の描画を先に確認してから第二種族へ展開 | 年齢別の姿、身体、負傷、装備が破綻しない。種族を増やす前に共通設定を固定 |
| H6：六文化と八開始 | 同じ受入条件で残る Species を追加。各単独開始、四種族混成、六種族混成を接続 | 「四つの火、ひとつの家」は元四種族各2人・計8人。「六つの手で築く家」は六種族各1人・計6人。実体種族と遺伝子が正しく、不足パック時は該当ルートがない |
| H7：遺伝子庫と DLC | 48 遺伝子の最初の分類・取得・代償・排他、必要な DLC 適合だけを追加 | Genes 単独の人間改良、Species との組合せ、遺伝・成長を確認。100→180→約300への展開はその後 |

**最初に実装する範囲は H0・H1 まで。** 新しい六文化 Def をまとめて生成する前に、星墓の成人一体で通常の生活と保存復帰を通す。実装初回には完成イラスト、六国家の生成、合作、48遺伝子の全展開を含めない。H1 の検証後に外観・体格・遺伝子のどこを調整するかを判断できるため、短い検証周期を維持できる。

各段階の変更は個別の staging と証拠で区切る。読込不能、想定外の遺伝子拒否、装備不可、実物を再生成する保存挙動が一つでもあれば、その原因を解決するまで六文化へ展開しない。

## 7. 保存・元個体・需要との境界

[Core 元個体保管](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/Inheritance/Docs/P04CorePawnCustody.md)、[元キャラバン保管](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/Inheritance/Docs/P04DelegatedCaravanCustody.md)、[委任生活計画](C:/Users/Ousuk/Documents/Codex/2026-10-06/new-chat/outputs/Inheritance/Docs/P04DelegatedLifePlan.md) にある責務を維持する。Core は元 Pawn と履歴を管理し、Colony が native の操作を担う。HAR 用 Pawn を名前と遺伝子だけから再生成して返還する実装は作らない。

保存する元 Pawn には種族 Def、HAR の Comp・外観情報、Endo／Xeno 遺伝子、装備、健康、関係を含めて保持し、保管前後・別プロセス読込後に照合する。公開後の packageId と Def 名を不用意に変更しない。保存に必要な Species が欠けたときは、その個体を人間へ置換して復旧成功とは扱わない。具体的な不足検出と案内は、通常の MOD 不足表示とモジュール検証を接続して検討する。

HAR 対応のために、現行 Core 7／Colony 1 の保存形式をこの計画だけで変更する必要はない。将来、委任生活に種族適応の証明や別の需要を追加する必要が判明した場合は、Core 中立データ・Species の所有データ・native Pawn のどこへ置くかを先に決めて保存移行をレビューする。

現行 RationDebits は食べ手に対する可食性・栄養・日需要を照会し、特殊な遺伝子資源を普通の栄養食品と区別している。HAR にも人型種族の食品適格性へ介入する処理があるため、実 DLL で通る API の経路を確認する。種族名だけで需要を定数化したり、HAR の制限を Colony 側へ複製したりしない。[公式 HarmonyPatches.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/HarmonyPatches.cs)

HAR の遺伝子・特殊資源を追加しても、現行の凍結保管・維持費会計だけで通常の生活、飢餓、治療、生産を実装した扱いにしない。既存の保管回帰は HAR 個体での回帰の代わりにならず、以下の実機確認が必要である。

## 8. 試験計画

以下は全て今後の試験であり、本調査での成功結果ではない。最初は H1 の最小構成で確認し、機能が増えるたびに必要な構成を広げる。

| 構成 | 主な確認 |
|---|---|
| Core／Colony／World、HAR・Species なし | 既存製品の単独読込・保存回帰。HAR 型を探して起動失敗しない。World の人間主体構成 |
| Core＋Biotech＋Genes、HAR・Species なし | 普通の人間・自作ゼノタイプの改良と保存。大型化遺伝子の実効。HAR 必須化していない |
| Core＋文化 Architecture／Arsenal のみ | 一般商人、研究、製作、装備使用。未導入 Species や World の Def 参照がない |
| Core＋Biotech＋HAR＋星墓 Species | 成人の H1 基本行動・実効体格・保存、最小の抽出と移植 |
| 上記＋もう一 Species＋他文化 Arsenal | 複数種族の共存、文化を越えた装備、遺伝子の移植、意図しない排他なし |
| 一部の Species＋Starts／Exchange | 導入済みの開始のみ表示。欠ける合作だけ非表示。誤った人間代替・欠落 Def エラーなし |
| 六 Species＋Biotech の基本構成 | 六文化の adult／child、八開始、混成、成長、出生方針、保存読込 |
| 六 Species＋Royalty＋Ideology＋Biotech＋Odyssey | DLC 追加分の生成・役職／思想・探索等の適合。Anomaly 不在で成立 |
| Species を指定して HAR が不足 | 必須依存が明瞭。エラーを回避するため Core や他パックへ HAR を押し付けない |

個体単位では次を確認する。

- **形態と装備：** 成人・子供の各段階、寝ている／倒れている姿、正面・側面・背面、髪・頭・角・膜、武器の持ち位置、防具の重なり、ポートレート。通常倍率とゲーム内表示の両方を確認する。
- **実効体格：** 通常ベッドと医療ベッド、作業台、負傷と治療、移動、可搬重量、食べ手別の栄養と日需要、戦闘結果。見た目だけ大きい、または同じ効果を二重に加える状態を検出する。
- **遺伝子：** Endo／Xeno、抽出、移植、発現・抑制、ゼノタイプ設定、代謝、拒否対象と許可対象、出生と子供の成長。追加拒否でも成功表示や意図しない投入物消費が起きないことを確認する。
- **技術交流：** 最初は人間・星墓・第二種族で一装備、最終的には人間と六種族から導入済み六文化の装備へ進める組合せ。完成品の購入と、設計図・研究・設備を経た製作を分けて試す。
- **保存と保管：** 同じ Pawn、種族・Comp・外観・遺伝子・健康・関係・装備を照合。ローカル保管、元キャラバンへの入出庫、途中保存再開、設定無効後の返還・再試行、輸送を既存の安全条件で検証する。
- **需要と供給候補：** HAR の可食性、普通の栄養食品、拒否食品、特殊遺伝子資源、未知の需要、不足を比較。供給候補の表示だけで食品を移したり、生体が生活した扱いにしない。

各試験はゲーム版・DLC・MOD 順・HAR DLL hash・Species の版・保存前後の観測・ログ・画面を記録する。出生や移植で HAR の既定挙動と設計が違う場合は、未確認の独自パッチを一括追加せず、再現例一つと変更一つで検証する。

## 9. 公式資料とローカル確認の限界

外部資料は公式 `erdelf/AlienRaces` リポジトリとその Wiki のみを参照した。参照日は全て 2026-10-09。master の読み取り結果であり、使用予定 DLL と同じコミットであることまでは確認していない。

| 公式資料 | この計画での用途・注意 |
|---|---|
| [About.xml](https://github.com/erdelf/AlienRaces/blob/master/About/About.xml) | packageId、1.6 対象。版番号や最新配布との一致の証明にはしない |
| [Getting Started](https://github.com/erdelf/AlienRaces/wiki/Getting-Started) | 最小の種族定義と段階的な作成 |
| [Feature Comparison](https://github.com/erdelf/AlienRaces/wiki/Feature-Comparison) | HAR の種族とゼノタイプの役割の違い |
| [General Settings](https://github.com/erdelf/AlienRaces/wiki/General-Settings) | 外観・基礎遺伝子・成長に関する設定の入口 |
| [Race Restriction](https://github.com/erdelf/AlienRaces/wiki/Race-Restriction) | 装備・遺伝子等の制限。正確な現行メンバーはソースも照合 |
| [ThingDef_AlienRace.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/ThingDef_AlienRace.cs) | 遺伝子制限・全種族への排他リスト反映の現行実装 |
| [HarmonyPatches.cs](https://github.com/erdelf/AlienRaces/blob/master/Source/AlienRace/AlienRace/HarmonyPatches.cs) | 基礎遺伝子追加、追加拒否、食品適格性への介入 |
| [loadFolders.xml](https://github.com/erdelf/AlienRaces/blob/master/loadFolders.xml) | 1.6 と DLC の条件付き読込 |
| [1.6 Migration](https://github.com/erdelf/AlienRaces/wiki/RimWorld-1.6-Migration) | 1.6 の変更点。WIP の文書であり、全互換の完成保証ではない |
| [Biotech Compatibility](https://github.com/erdelf/AlienRaces/wiki/Biotech-Compatibility) | 更新途中の短い案内。これだけを遺伝子適合の根拠にしない |

ローカルには Steam Workshop の `C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/839005762` に公式 packageId と 1.6 対象を持つ HAR が存在した。通常の `RimWorld/Mods` フォルダ内には HAR 本体を見つけていない。他 MOD の About にある HAR の依存参照を、HAR 本体の重複と数えないよう本体の packageId で判別した。

読み取った `1.6/Assemblies/AlienRace.dll` の SHA-256 は `9B5DC05932E9DFAA963C2FA5253127E050B074686468E03C145831DCF2D7F626`。About の modVersion は空で、アセンブリの 1.0.0.0 やファイル日時は最新配布版の証明にならない。この調査ではアセンブリを実行・ゲームへ追加しておらず、Workshop の最新配布との一致も未確認である。実装開始時に採用する配布物と対応ソースを固定する。

HAR 本体を継承圏へ同梱する前提にはせず、Species の明示的な依存として扱う。公式資料の設定を採用しても、六種族の製品実装・描画・Biotech・既存保管の適合は上記の実機試験が完了するまで未確認と報告する。

## H1 最小候補の実ゲーム確認（2026-10-09追記）
専用HAR ThingDefとPawnKindをworkに作成。凍結Core15＋HAR＋Biotechのみ／四DLCでDefs読込2試験PASS、Evidence/HARH1MinimalDefsに2抜粋とREADME/RuntimeSHA256、全262Runtime比較一致。Human fallbackの専用試験種族で、六文化製品へ未統合。個体生成初回はHAR型と成人30歳男性には合格したが、Baselinerで実endo/xenoが空という基準にFAILしたため保存試験を止めた。InitialGeneBaselineFailureに元ログ/Source/DLLを保全、gene Def実名を調査中。専用個体生成・保存再読・描画・通常生活・巨体成功とは扱わない。Core15構成はP04製品16の件数へ合算しない。

### H1の追加確認：自然色と独立した診断系列

上記は初回時点の記録。その後、固定native XML/DLLを調べ、Baselinerが自然に持つ髪・肌の色遺伝子だけを明示allowlistで許す基準へ改訂した。各個体は成人30歳・健康・裸で、Endo2（髪1/肌1）・Xeno0。能力遺伝子を許さず、Geneの除去や書換えもしない。原seedは保持し、コピー内の旧Job-key2項目だけをnative unset値へ置き換えた制御派生を使用した。

[Biotech限定の3工程](Evidence/HARH1ColourV2Biotech/Result.md)は、固定v2 DLLで生成保存→別プロセス再読と二度目保存→別プロセス最終再読が成功。24の色項目だけ、保存前の期待値へnativeのF3→Single契約を適用し、実際の再読値を丸めず完全一致で照合する。他の項目・時計・年齢はraw完全一致、保存直前直後と二度目保存後の最終再読もraw完全一致で確認した。

元v2の四DLC生成保存は所持品判定でFAILした。[別DLLの所持品読取診断3工程](Evidence/HARH1InventoryDiagnostic3/Result.md)では、元requestと裸判定を保ったまま4個体の服・武器・所持品が各0となり、保存・再読・最終再読に成功した。ただし生成結果も異なり、元失敗は未再現・未解決。異なるDLLの2系列を同系列6件成功へ合算しない。診断初回保存の外側出力はRoot実行ツール応答の原文転記（exit0）、再読/最終再読は実コンソール採取と区別して証拠化した。

どちらもCore15とHuman fallbackの技術Minimalに限定する。六文化の完成、描画、巨体の実効体格、通常生活、装備・負傷・Robust、Core16互換は未確認。新しく配布する証拠には抜粋・結果・SHAだけを含め、rawログ・save・DLLはworkに保持する。
