# Player Restraint Scripts

拘束できるVRChatのワールドギミック用スクリプト集です。
プレイヤーのボーンにオブジェクトを追従させる、プレイヤーをオブジェクトの位置に拘束する、接触したプレイヤーを捕まえる、といった動作を組み合わせて拘束ギミックを作るためのUdonSharpスクリプトを収録しています。

## 導入方法

### VCC / ALCOMから導入する（推奨）

1. [配布ページ](https://github.pito.run/vrc-restraint-scripts/)を開き、上部の**Add to VCC**ボタンを押してリポジトリを追加します。
   ボタンが反応しない場合は、VCCの`Settings` → `Packages` → `Add Repository`に次のURLを貼り付けてください。

   ```text
   https://github.pito.run/vrc-restraint-scripts/index.json
   ```

2. 対象プロジェクトの`Manage Project`から**Player Restraint Scripts**を追加します。

### 手動で導入する

[Releases](https://github.com/mecaota/vrc-restraint-scripts/releases)から`.unitypackage`をダウンロードし、プロジェクトにインポートしてください。

## 動作環境

- Unity 2022.3
- VRChat SDK - Worlds（UdonSharp）

## 収録スクリプト

| スクリプト | 概要 |
| --- | --- |
| [PlayerBoneConstraint](#playerboneconstraint) | プレイヤーの指定したボーンにオブジェクトを追従させる |
| [PlayerRestraintConstraint](#playerrestraintconstraint) | プレイヤーをオブジェクトの位置に拘束する |
| [MovePositionByContact](#movepositionbycontact) | 接触したプレイヤーを最寄りのボーンごとConstraintに割り当てる |
| [ObjectPullPush](#objectpullpush) | オブジェクトを引き寄せる / 押し出す |
| [StickyLine](#stickyline) | 2点間に糸のような線を描画する |

### 基本的な組み合わせ

「触れたプレイヤーをその場に拘束する」ギミックは次の構成で作れます。

1. 拘束位置にしたいオブジェクトに`PlayerRestraintConstraint`を追加します（`Target Player Id`は`-1`のままにします）。
2. プレイヤーが触れるTrigger Colliderを持つオブジェクトに`MovePositionByContact`を追加し、`Bone Constraints`に手順1のオブジェクトを指定します。
3. プレイヤーが触れると、接触したオブジェクトの位置にもっとも近いボーンが選ばれ、空いているConstraintにそのプレイヤーが割り当てられて拘束が始まります。

### PlayerBoneConstraint

プレイヤーの指定したボーンにオブジェクトを追従させます。UnityのPosition Constraintのプレイヤー版です。
`Target Player Id`を他のスクリプトから設定することで、動的に追従対象を切り替えられます。

| 項目 | 説明 |
| --- | --- |
| Target Player Id | 追従するプレイヤーの`playerId`。`-1`で未設定 |
| Target Bone | 追従するボーン（`HumanBodyBones`） |
| Follow Strength | 追従の強さ（0〜1）。1で即座に追従します |
| Follow Position / Position Offset | 位置を追従するか、およびボーンの向きを基準にした位置オフセット |
| Follow Rotation / Rotation Offset | 回転を追従するか、および回転オフセット（オイラー角） |
| Follow Scale / Scale Offset | アバターの身長（目の高さ2mを基準）に応じてスケールを追従するか、およびスケール倍率。初期値は0なので利用時は設定してください |
| Reset Position On Disable | オブジェクトの無効化、プレイヤーの退室、リスポーン時に初期位置へ戻すか |

他のスクリプトから呼び出せるメソッド:

- `SetTargetPlayer(int playerId)` : 追従対象を設定します。プレイヤーが見つからない場合は解除されます
- `SetTargetBone(HumanBodyBones bone)` : 追従するボーンを設定します
- `Detach()` : 追従を解除して初期位置へ戻します
- `IsAttached()` : 追従中かどうかを返します

### PlayerRestraintConstraint

`PlayerBoneConstraint`を継承し、逆にプレイヤーの指定したボーンがオブジェクトの位置に来るようにプレイヤーを拘束します。
割り当て直後の1フレームでオブジェクトがプレイヤーのボーン位置へ移動し、その後はプレイヤーの速度を制御してその位置に留めます。

`PlayerBoneConstraint`の設定に加えて次の項目があります。

| 項目 | 説明 |
| --- | --- |
| Ignore X / Ignore Y / Ignore Z | 指定した軸方向の拘束を無視します（その軸方向には自由に動けます） |

- `Position Offset`は拘束位置（オブジェクト位置）へのオフセットとして使われます。
- `Follow Strength`は拘束の強さ（速度の倍率）として使われます。
- プレイヤーが接地している場合、拘束位置の高さはボーンの高さ以上に補正され、地面に埋まらないようになっています。

### MovePositionByContact

プレイヤーがこのオブジェクトに接触（`OnPlayerTriggerEnter` / `OnPlayerCollisionEnter` / `OnPlayerParticleCollision`）したとき、このオブジェクトの位置にもっとも近いプレイヤーのボーンを求め、空いている`PlayerBoneConstraint`にそのプレイヤーとボーンを割り当てます。
割り当てられたConstraintのオブジェクトはアクティブ化されます。

| 項目 | 説明 |
| --- | --- |
| Bone Constraints | 割り当て先の`PlayerBoneConstraint`（または`PlayerRestraintConstraint`）の配列。未指定の場合はアクティブな子オブジェクトから自動取得します |

TriggerのColliderや、CollisionモジュールでSend Collision Messagesを有効にしたParticle Systemなど、プレイヤーとの接触イベントが発生する設定と組み合わせて使います。

### ObjectPullPush

対象オブジェクトをこのオブジェクトへ引き寄せる（Pull）、または押し出す（Push）スクリプトです。
対象に`Rigidbody`があれば力を加え、なければ座標を直接移動します。
VRC Pickupと組み合わせると、`Interact`でモードを切り替え、`Use`ボタンを押している間だけ動作させられます。

| 項目 | 説明 |
| --- | --- |
| Min Distance | この距離より近い対象には力を加えません |
| Target Objects | 引き寄せ / 押し出しの対象（複数可） |
| Force | 引き寄せ / 押し出しの強さ |
| Enable Interact Toggle | `Interact`でPull / Pushを切り替えるか |
| Mode | `Pull`（引き寄せ）または`Push`（押し出し） |
| Auto Update | 毎フレーム自動で実行するか。Pickupの`Use`中は自動的に有効になります |

他のスクリプトから呼び出せるメソッド:

- `SetPullMode(PullPushMode mode)` : Pull / Pushを設定します
- `SetTargetObjects(GameObject[] objects)` : 対象オブジェクトを設定します
- `DoAction()` : その場で1回実行します

### StickyLine

`Target Object`からこのオブジェクトへ、たわみや揺らぎのある糸状の線を`LineRenderer`で描画します。
拘束したプレイヤーと拘束元をつなぐ糸などの表現に使えます。同じオブジェクトに`LineRenderer`が必要です。

| 項目 | 説明 |
| --- | --- |
| Target Object | 線の始点となるオブジェクト |
| Random Offset Range | 終点に加えるランダムな揺らぎの範囲 |
| Line Count | 線の本数 |
| Line Width | 線の太さ（両端が太く、中央が細くなります） |
| Sag Amount | 下方向へのたわみ量。0で直線になります |
| Segments Per Meter | 1mあたりの曲線の分割数 |

## ライセンス

[CC0 1.0 Universal](LICENSE)

## 作者

[mecaota](https://github.com/mecaota/)
