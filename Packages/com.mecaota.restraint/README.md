# vrc_restraint_scripts
拘束できるVRChatのワールドギミック用スクリプト

## 構成

```
RestraintCore (Runtime/Core)      … 基幹。装着状態(プレイヤー/ボーン/オフセット)を同期し、装着/解除イベントを飛ばす
├ PlayerBoneConstraint            … Core のイベントを受けてオブジェクトをボーンに追従させる追従器
├ PlayerRestraintConstraint       … Core の対象が自分のとき、自分をオブジェクト位置へ拘束する(SetVelocity)
├ StickyLine                      … 糸の描画(core を設定すると装着中だけ描く)
├ MovePositionByContact           … 接触したプレイヤーの最寄りボーンへ空き Core を装着する
├ PlayerPullController            … 繭(Core)に捕まった自分を引き寄せる
└ CocoonSpinStation               … 引き寄せの到達で VRCStation に座らせる

PlayerMovementLock                … (Core と独立)複数ギミックからの減速・ジャンプ・重力の制限を重ねて掛け、元に戻す共有部品
```

FukuroUdon は必須ではありません。装着のトリガーは `ActiveRelayToUdonBehaviour` などから `RestraintCore` の
`AttachLocalPlayer` / `DetachSelf` / `Detach` / `Toggle` を `SendCustomEvent` で呼ぶだけで連携できます。

## RestraintCore

拘束ギミックの基幹コンポーネント。「どのプレイヤーの・どのボーンに・どのオフセットで装着されているか」を Manual 同期で全員に共有し、その状態から自身のボーン追従(位置/回転/身長スケール)と装着/解除イベントの発火を行います。全クライアントが同じ同期状態から動くので遅入室者にも復元され、退室・リスポーン時の掃除と所有権も Core が担います。1 Core = 1 人分の装着セッションで、装飾は `PlayerBoneConstraint` 等の追従器を `receivers` に登録(または同じ GameObject に同居)させて動かします。イベントは Inspector で指定した `UdonBehaviour[]` へ任意のイベント名(既定 `OnBoneAttach` / `OnBoneDetach`)で、また同じ GameObject 上の他の UdonBehaviour へ固定名で送られます。Core の GameObject は常時アクティブにしてください(非アクティブだと同期を受け取れません)。詳細は `Runtime/Core/readme.md`。

## PlayerBoneConstraint / PlayerRestraintConstraint

どちらも `RestraintCore` を参照する追従器です。`PlayerBoneConstraint` は Core に装着されたプレイヤーの(自分の `targetBone` または Core のボーンの)位置・回転・スケールにオブジェクトを追従させます。`PlayerRestraintConstraint` は逆に、Core の対象が自分のときだけ自分をオブジェクト位置へ拘束します(`SetVelocity` はローカル専用のため本人のクライアントでのみ動作)。いずれも Core の `OnBoneAttach` / `OnBoneDetach` で開始/停止し、取りこぼしても Core の状態を毎フレーム見て追いつきます。

## PlayerPullController

蜘蛛の糸玉に捕縛されたローカルプレイヤーを`pullAnchor`へ一定速度で引き寄せます。移動操作すると引き寄せ速度が下がり、入力方向へ自分でも移動できます。`moveAllow`と`minPullSpeed`で「遅いが移動できる」（moveAllow=1,minPull=0）／「移動不可・速度が下がるだけ」（moveAllow=0,minPull>0）を切り替えます。`SetVelocity`はローカル専用なので各クライアントで自分1体だけ駆動し、他者は位置同期で見えます。捕縛判定は`cocoons`（繭の `RestraintCore`）を毎フレーム監視して`targetPlayerId==LocalPlayer`で導出するため、繭の解除に自動連動します。`exclusiveCores`（吊り下げ拘束の Core など）に自分が入っている間は駆動を止めます。引き寄せが始まっている間は`hideOnPull`に指定したオブジェクト（糸玉パーティクルなど）をオフにし、捕縛が解けると戻します（ローカルのみ）。

## CocoonSpinStation

糸疣まで引き寄せられて到達したローカルプレイヤーを`VRCStation`に着席させ、`PlayerMobility=Immobilize`で移動不能にします。横倒し(頭と足が地面と平行)＋頭と足を軸にしたループ回転はVRCStationの`animatorController`に割り当てたアニメが担い(着席で自動再生・着席中はアバターのアニメも全員へ同期)、本スクリプトはTransformを回さず着席・繭連動・退席だけを受け持ちます。捕縛判定は`cocoons`(繭の `RestraintCore`)を毎フレーム監視し、`pullAnchor`(糸疣)へ`grabDistance`まで近づいたら着席、繭が外れると自動で降ります。`UseStation`/`ExitStation`はローカル専用・着席事実はVRChatが自動同期するため`SyncMode None`。Station1台=同時1人対応。横倒し回転アニメはワールド側で用意します(本リポジトリには含みません)。

## PlayerMovementLock

ローカルプレイヤーの移動速度・ジャンプ・重力を、複数のギミックから重ねて絞るための共有部品(1 ワールドに 1 つ)。各ギミックは自分を holder として `Hold(holder, move, jump, gravity)` / `Release(holder)` を呼ぶだけで、元の値の保存と復元はこの部品が受け持ちます。項目ごとに一番強い制限(最小値)を使うので掛けた順に依存せず、最後の holder が外れると、この部品が入れた値のままの項目だけ元に戻します。リスポーンで全部外れます。ギミックの Prefab からは `PlayerMovementLock.Resolve(cached, prefab)` で名前検索・無ければ Prefab から生成して使えます。詳細は `Runtime/PlayerMovementLock/readme.md`。

## 0.3.0 の変更点

- `PlayerMovementLock` を新設。各ギミックが自前で速度を保存・復元していたため、減速が重なると解除の順序しだいで減速が残る問題を、共有部品への `Hold` / `Release` で解消
- `StickyLine` に `keepRendererWidth` を追加。ON なら LineRenderer に設定した太さをそのまま使い、`lineWidth` と中央を細くするカーブで上書きしない(捕縛の蜘蛛糸と同じ太さ一定の糸にする用。吊り下げギミックで使用)
- `PlayerRestraintConstraint` に `dynamicOffset` を追加。外部スクリプト(揺れなど)が毎フレーム書き込む拘束位置のオフセット(ワールド座標)で、解除時に 0 へ戻る

## 0.2.0 の変更点(旧バージョンからの移行)

- `RestraintCore` を新設。装着状態の同期・イベント・退室/リスポーンの掃除を Core に集約
- `PlayerBoneConstraint` / `PlayerRestraintConstraint` は Core を参照する追従器になり、`targetPlayerId` の書き込み API(`SetTargetPlayer` / `SetTargetBone` / `Detach`)を廃止。`resetPositionOnDisable` は `resetOnDetach` に改名
- `PlayerPullController` / `CocoonSpinStation` / `MovePositionByContact` の参照は `PlayerBoneConstraint[]` から `RestraintCore[]` に変更(`cocoons` / `exclusiveCores` / `cores`)
- `StickyLine` に `core`(装着中だけ描画)を追加
- Core に装着元ID(`channel`)の同期を追加。複数ギミックで Core プールを共有でき、追従器は自分の `channel` の装着だけに反応する
