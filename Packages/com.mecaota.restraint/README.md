# vrc_restraint_scripts
拘束できるVRChatのワールドギミック用スクリプト

## 構成

```
RestraintCore (Runtime/Core)      … 基幹。装着状態(プレイヤー/ボーン/オフセット)を同期し、装着/解除イベントを飛ばす。
                                    ボーンを読む static ヘルパ(無いボーンは親へさかのぼる)も持つ
├ PlayerBoneConstraint            … Core のイベントを受けてオブジェクトをボーンに追従させる追従器
├ PlayerRestraintConstraint       … Core の対象が自分のとき、自分をオブジェクト位置へ拘束する(SetVelocity)
├ StickyLine                      … 糸の描画(core を設定すると装着中だけ描く)
├ MovePositionByContact           … 接触したプレイヤーの最寄りボーンへ空き Core を装着する
└ PlayerPullController            … Core に捕まった自分を引き寄せる

PlayerEncumbranceController       … (Core と独立)複数ギミックからの減速・ジャンプ・重力の制限を重ねて掛け、元に戻す。
                                    PlayerObject に置いた複製がまとめ役で、プレイヤーごとの動きにくさ(0〜255)も同期し、
                                    値に応じて減速・移動不可にする(もがくと減る)。それ以外の場所に置くと、
                                    イベントで自分の移動速度を設定・増減・リセットする窓口になる
```

FukuroUdon は必須ではありません。装着のトリガーは `ActiveRelayToUdonBehaviour` などから `RestraintCore` の
`AttachLocalPlayer` / `DetachSelf` / `Detach` / `Toggle` を `SendCustomEvent` で呼ぶだけで連携できます。

## RestraintCore

拘束ギミックの基幹コンポーネント。「どのプレイヤーの・どのボーンに・どのオフセットで装着されているか」を Manual 同期で全員に共有し、その状態から自身のボーン追従(位置/回転/身長スケール)と装着/解除イベントの発火を行います。全クライアントが同じ同期状態から動くので遅入室者にも復元され、退室・リスポーン時の掃除と所有権も Core が担います。1 Core = 1 人分の装着セッションで、装飾は `PlayerBoneConstraint` 等の追従器を `receivers` に登録(または同じ GameObject に同居)させて動かします。イベントは Inspector で指定した `UdonBehaviour[]` へ任意のイベント名(既定 `OnBoneAttach` / `OnBoneDetach`)で、また同じ GameObject 上の他の UdonBehaviour へ固定名で送られます。Core の GameObject は常時アクティブにしてください(非アクティブだと同期を受け取れません)。ボーンを読む static ヘルパ(`TryGetBonePose` / `TryGetBonePoseOrParent` / `ParentBone` / `FindClosestBone`)も公開しています。`TryGetBonePoseOrParent` は、アバターに無いボーンを親へさかのぼって代わりに取ります。詳細は `Runtime/Core/readme.md`。

## PlayerBoneConstraint / PlayerRestraintConstraint

どちらも `RestraintCore` を参照する追従器です。`PlayerBoneConstraint` は Core に装着されたプレイヤーの(自分の `targetBone` または Core のボーンの)位置・回転・スケールにオブジェクトを追従させます。`PlayerRestraintConstraint` は逆に、Core の対象が自分のときだけ自分をオブジェクト位置へ拘束します(`SetVelocity` はローカル専用のため本人のクライアントでのみ動作)。いずれも Core の `OnBoneAttach` / `OnBoneDetach` で開始/停止し、取りこぼしても Core の状態を毎フレーム見て追いつきます。

## PlayerPullController

`RestraintCore` に装着された(捕まった)ローカルプレイヤーを`pullAnchor`へ一定速度で引き寄せます。移動操作すると引き寄せ速度が下がり、入力方向へ自分でも移動できます。`moveAllow`と`minPullSpeed`で「遅いが移動できる」（moveAllow=1,minPull=0）／「移動不可・速度が下がるだけ」（moveAllow=0,minPull>0）を切り替えます。`SetVelocity`はローカル専用なので各クライアントで自分1体だけ駆動し、他者は位置同期で見えます。捕縛判定は`cocoons`（監視する `RestraintCore`）を毎フレーム見て`targetPlayerId==LocalPlayer`で導出するため、Core の解除に自動連動します。`exclusiveCores`（`SetVelocity` を使うほかの拘束の Core など）に自分が入っている間は駆動を止めます。捕まったら`hideOnPull`に指定したオブジェクトをオフにします（ローカルのみ。オンには戻さないので、戻すのはほかの仕組みに任せます）。

## PlayerEncumbranceController

ローカルプレイヤーの移動速度・ジャンプ・重力を、複数のギミックから重ねて絞るための部品です。プレイヤーごとの動きにくさも持ちます。置いた場所で役割が 2 つに分かれます。

- **まとめ役**(PlayerObject のテンプレートの根に置き、ローカルプレイヤーの複製がまとめ役になる): 各ギミックは自分を holder として `Hold(holder, move, jump, gravity)` / `Release(holder)` を呼ぶだけで、元の値の保存と復元はまとめ役が受け持ちます。項目ごとに一番強い制限(最小値)を使うので掛けた順に依存せず、最後の holder が外れると、まとめ役が入れた値のままの項目だけ元に戻します。リスポーンで全部外れます。ギミックからは `PlayerEncumbranceController.Resolve(cached)` でローカルプレイヤーの PlayerObject から取ります。
  - 動きにくさ(0〜255)を Manual 同期で持ちます。本人の端末だけが `_Add(int)` で増減し、`_Reset()` とリスポーンで 0 に戻ります。値が `immobileAt`(既定 32)より小さい間は「1 − 値 ÷ `immobileAt`」倍以下に減速し、以上で移動とジャンプを止めます。もがき 1 回ごとに `recoverAmount`(既定 2)だけ減ります。`animator` を設定すると、全端末で値 ÷ 255 を float の `Amount` に入れます。
- **窓口**(それ以外の場所に置いたもの): スクリプト無しで使うための入口です。`SetSpeed`(決めた倍率にする)/ `AddSpeed`・`SubtractSpeed`(決めた量だけ増減)/ `ResetSpeed`(元の速度に戻す)を `SendCustomEvent` で受け、ローカルプレイヤーの移動速度を「元の速度の何倍か」で変えます(0 で移動できない、0.5 で半分)。増減は範囲の外に出た分も覚えるので、`SubtractSpeed` と `AddSpeed` を対で呼べば何回重なっても元に戻ります(対で送る送り手がいくつあっても、窓口は 1 つで済む)。`Core` を設定して Core の `receivers` に入れると、自分が装着されたら `SetSpeed`、外れたら `ResetSpeed` します。

いま頼まれている移動・ジャンプの倍率は `HeldMove()` / `HeldJump()` で読めます(誰も掛けていなければ 1)。頼まれた値なので、別のシステムが上書きした実際の値とは違うことがあります。

詳細は `Runtime/PlayerEncumbranceController/readme.md`。

## 0.7.1 の変更点

- `ObjectPullPush` で Rigidbody を押す・引く `AddForce` を、`ForceMode.VelocityChange` から「力 × 質量を `ForceMode.Impulse`」に替えた。VRChat の [Unity 6 移行手順](https://vrc-beta-docs.netlify.app/sdk/upgrade/unity-6000/)の「Force and Torque Changes」は `ForceMode` を `Force` にして質量を掛けるよう示すが、これは `Acceleration` の置き換え。`VelocityChange` と同じ速度の変化を保つには「`Impulse` × 質量」が要る(「`Force` × 質量」では時間刻み倍に弱まる)ので `Impulse` にした。手順は `Impulse` に触れておらず、`Impulse` が Unity 6 の変更の外にあるというのは推測で、実機では確かめていない
- 動きは式の上で同じ。Unity 6 の [`Rigidbody.AddForce`](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/rigidbody/addforce) の説明では、1 回の呼び出しあたりの速度の変化は `VelocityChange` が「力」、`Impulse` が「力 ÷ 質量」で、どちらも 1 回の呼び出しあたりでは時間刻みに依存しない。`Impulse` に「力 × 質量」を渡すと「力」になり、前と同じになる
- この枝(`AddForce`)を通るのは、`SetTargetObjects()` で Rigidbody つきの対象を渡したときだけ。Inspector で設定した対象は Rigidbody の有無にかかわらず座標を直接動かす枝を通るので、今回の変更の影響を受けない

## 0.7.0 の変更点

**互換を切った。** 古い版から上げると、利用側の型名と prefab が壊れる。

- `PlayerMovementLock` と `MovementDebuff` を 1 つの `PlayerEncumbranceController`(`Runtime/PlayerEncumbranceController`)にまとめた。スクリプトの GUID は `PlayerMovementLock` のものを引き継いだので、シーンに置いた `PlayerMovementLock` のコンポーネントはそのまま新しい型になる。`MovementDebuff` のコンポーネントは参照が切れるので、付け直すこと
- `PlayerMovementLock.prefab` を削除した。シーンに名前 `PlayerMovementLock` のまとめ役を置く決まりをやめ、ローカルプレイヤーの PlayerObject の複製をまとめ役にした。`ObjectName` 定数と `movementLockPrefab` 欄を消し、`Resolve(cached, prefab)` を `Resolve(cached)` にした
- 利用側のスクリプトは、型名 `PlayerMovementLock` / `MovementDebuff` を `PlayerEncumbranceController` に、`Resolve(cached, prefab)` を `Resolve(cached)` に書き換えること。シーンの `PlayerMovementLock.prefab` のインスタンスは Missing になるので消すこと
- 同期方式を None から Manual にした(動きにくさを同期するため)。窓口も Manual 同期になったので、Continuous 同期の部品(VRCObjectSync など)と同じ GameObject に置かないこと
- 移行の手順: `MovementDebuff` を付け直したら、`Immobile At` / `Recover Amount` / `Animator` を入れ直すこと。`MovementDebuff` 型の欄で参照していた利用側は、つなぎ直すこと。まとめ役は PlayerObject のテンプレートに置かないと何も効かない(以前はシーンに無ければ prefab から自動で作られた)
- そのほかの入口(`Hold` / `Release` / `ReleaseAndRestore` / `Holds` / `Base*` / `Held*` / 速度のイベント / `_Add` / `_Reset` / `StrongestHold` / `HeldRatio` / `Free` / `MaxHolders`)と欄の名前は変えていない

## 0.6.0 の変更点

追加だけで、既存の動きは変わらない。

- `MovementDebuff` を新設(`Runtime/MovementDebuff`)。PlayerObject のテンプレートに置き、プレイヤーごとの値(0〜255)を Manual 同期で持つ。値は本人の端末だけが `_Add(int)` で増減し、`_Reset()` とリスポーンで 0 に戻る
- 値が `immobileAt`(既定 32)より小さい間は「1 − 値 ÷ `immobileAt`」倍に減速し、以上で移動とジャンプを止める。速度はシーンの `PlayerMovementLock` のまとめ役に頼む
- もがき 1 回ごとに `recoverAmount`(既定 2)だけ減る。VR は手を速く振り返した時、デスクトップは移動キーを新しく倒した時に数える
- `animator` を設定すると、全端末で値 ÷ 255 を float の `Amount` に入れる
- シーンに `PlayerMovementLock.prefab`(名前 `PlayerMovementLock`)を 1 つ置くこと。無いと減速しない

## 0.5.0 の変更点

どちらも追加だけで、既存の動きは変わらない。

- `RestraintCore` に `ParentBone(bone)` と `TryGetBonePoseOrParent(player, bone, out position, out rotation, out resolvedBone)` を追加。アバターが Neck・Chest・UpperChest・Shoulder・指・つま先・目・顎を持たないとき、親のボーンへさかのぼって位置・回転を取れる。オフセットは取れたボーン(`resolvedBone`)の座標系を基準に掛かるので、記録する側も掛ける側も `resolvedBone` を使う。さかのぼる順は `Runtime/Core/readme.md`
- `PlayerMovementLock` に `HeldMove()` / `HeldJump()` を追加。いま頼まれている移動・ジャンプの倍率(`Free` を除いた最小値、誰も掛けていなければ 1)を返す。返すのは頼まれた値で、別のシステムが上書きした実際の値ではない。重力は扱わない
- 最小値の計算を `PlayerMovementLock.StrongestHold` / `HeldRatio` に切り出し、制限を掛け直す処理(`Apply`)と `HeldMove` / `HeldJump` が同じ計算を使うようにした。`Apply` の結果は変わらない
- EditMode テスト(`Tests/Editor`)を追加。すべての `HumanBodyBones` が循環せずに Hips へ着くことと、最小値の計算を確かめる

## 0.4.1 の変更点

- `PlayerMovementLock.prefab` の UdonBehaviour の同期方式を、スクリプトの `BehaviourSyncMode.None` に合わせて None にした(これまでは Unknown で、シーンに置くと何かが設定し直すまで未設定のままだった)。使い方は変わらない

## 0.4.0 の変更点

- `PlayerMovementLock` に速度のイベント(`SetSpeed` / `AddSpeed` / `SubtractSpeed` / `ResetSpeed`)を追加。まとめ役以外の場所に置くと、イベントを受けてまとめ役に制限を頼む窓口になる。既存の `Hold` / `Release` などの使い方は変わらない

## 0.3.0 の変更点

- `PlayerMovementLock` を新設。各ギミックが自前で速度を保存・復元していたため、減速が重なると解除の順序しだいで減速が残る問題を、共有部品への `Hold` / `Release` で解消
- `StickyLine` に `keepRendererWidth` を追加。ON なら LineRenderer に設定した太さをそのまま使い、`lineWidth` と中央を細くするカーブで上書きしない(太さが一定の糸にする用)
- `PlayerRestraintConstraint` に `dynamicOffset` を追加。外部スクリプト(揺れなど)が毎フレーム書き込む拘束位置のオフセット(ワールド座標)で、解除時に 0 へ戻る

## 0.2.0 の変更点(旧バージョンからの移行)

- `RestraintCore` を新設。装着状態の同期・イベント・退室/リスポーンの掃除を Core に集約
- `PlayerBoneConstraint` / `PlayerRestraintConstraint` は Core を参照する追従器になり、`targetPlayerId` の書き込み API(`SetTargetPlayer` / `SetTargetBone` / `Detach`)を廃止。`resetPositionOnDisable` は `resetOnDetach` に改名
- `PlayerPullController` / `MovePositionByContact` の参照は `PlayerBoneConstraint[]` から `RestraintCore[]` に変更(`cocoons` / `exclusiveCores` / `cores`)
- `StickyLine` に `core`(装着中だけ描画)を追加
- Core に装着元ID(`channel`)の同期を追加。複数ギミックで Core プールを共有でき、追従器は自分の `channel` の装着だけに反応する
