# RestraintCore

## 概要

拘束ギミックの基幹コンポーネントです。「どのプレイヤーの・どのボーンに・どのオフセットで装着されているか」を
Manual 同期で全クライアントに共有し、その状態から

- 自分自身のボーン追従(位置 / 回転 / 身長スケール)
- 装着 / 解除イベントの発火

を行います。全クライアントが同じ同期状態から動くので、後から入室したプレイヤーにも装着状態が復元されます。
退室・リスポーン時の掃除と所有権の扱いも Core が担います。

1 つの Core = 1 人分の装着セッションです。同じプレイヤーに複数の装飾を付けたいときは、
`PlayerBoneConstraint` などの追従器を Core の `receivers` に登録する(または Core と同じ GameObject に置く)ことで、
Core のイベントに合わせて追従を開始 / 停止させます。

## 使い方(FukuroUdon との組み合わせ)

```
Gimmick/
  Core            RestraintCore(常時アクティブ)  ← 追従器や装飾はここ、または receivers に
  Trigger         ActiveRelayByPlayerTrigger(acceptPlayer=Self, ToggleActive, gameObjects=[Relay])
  Relay (inactive) ActiveRelayToUdonBehaviour(networkEventTarget=Self,
                    active:   [Core] "AttachLocalPlayer"
                    inactive: [Core] "DetachSelf")
```

- 装着のトリガーは FukuroUdon の ActiveRelay で中継オブジェクトを有効化し、`ActiveRelayToUdonBehaviour` から
  `AttachLocalPlayer` / `DetachSelf` / `Detach` / `Toggle` を呼びます。
- FukuroUdon 側のコンポーネントを `receivers` に入れ、`attachEventName` / `detachEventName` に任意のイベント名を指定して
  通知することもできます。

## 呼び出せるメソッド(SendCustomEvent 可 = 引数なし)

| メソッド | 内容 |
|---|---|
| `AttachLocalPlayer()` | ローカルプレイヤーを `Default Bone` に装着 |
| `Detach()` | 誰が呼んでも解除(救助用) |
| `DetachSelf()` | 装着されているのが自分のときだけ解除 |
| `Toggle()` | 未装着なら AttachLocalPlayer、装着中なら Detach |
| `AttachRequested()` | `Requested Player Id` / `Requested Bone` で装着(引数の代わりに先にフィールドを設定する) |
| `SetBoneRequested()` | `Requested Bone` へボーンを変更 |

スクリプトからは `Attach(int playerId)`、`AttachToBone(int playerId, HumanBodyBones bone)`、`SetTargetBone(...)`、
`SetOffset(...)`、`IsAttached()`、`IsAttachedTo(int)`、`IsLocalPlayerAttached()`、`GetTargetPlayer()` が使えます。
ネットワーク越しに依頼したいときは `[NetworkCallable]` の `RequestAttach(int playerId, int bone, int attachChannel)` / `RequestDetach()` を
`SendCustomNetworkEvent(NetworkEventTarget.Owner, ...)` で送ると先着順が保証されます。

読み取り用に `targetPlayerId`(-1 = 未装着)と `targetBone` を公開しています。非アクティブな UdonBehaviour への
メソッド呼び出しは捨てられるため、状態の監視はこのフィールドの直読みで行ってください(書き込みはしないこと)。

## イベント

装着 / 解除の遷移ごとに、各クライアントで 1 回ずつ発火します。

1. `Receivers` に登録した UdonBehaviour へ `Attach Event Name` / `Detach Event Name`(既定 `OnBoneAttach` / `OnBoneDetach`)を送信
   - `Network Event Target` が `Self` のときは各クライアントでローカル送信します(推奨)。
   - `All` / `Owner` / `Others` のときはオーナーのクライアントだけがネットワーク送信します(二重配信防止)。
     同期なし(`None`)の UdonBehaviour はネットワークイベントを受け取れず、あとから入った人にも送り直されません。
     同梱の部品(`PlayerMovementLock` など)を受け手にするときは `Self` のままにしてください。
2. `Notify Siblings` が ON なら、同じ GameObject 上の他の UdonBehaviour へ固定名 `OnBoneAttach` / `OnBoneDetach` を送信(配線不要)

受信側は `public void OnBoneAttach()` / `public void OnBoneDetach()` を実装し、必要なら `core.targetPlayerId` / `core.targetBone` を読みます。
FukuroUdon の ManualObjectSync が送る `OnAttach` / `OnDetach` とは名前を分けています。

## 設定項目

### ターゲット設定

- **Default Bone**: ボーン未指定で装着したときのボーン

### 位置 / 回転 / スケール追従(この GameObject 自身)

- **Follow Position / Follow Rotation / Follow Scale**: 自身をボーンに追従させるか。ハブ専用なら全部 OFF
- **Capture Offset On Attach**: ON なら装着時の自身の位置・回転をボーン基準のオフセットとして記録(その場に張り付く)。OFF なら固定オフセットを使う
- **Position Offset / Rotation Offset**: 固定オフセット(ボーン座標系)
- **Reference Eye Height / Scale Multiplier**: 目線の高さがこの値のとき倍率が等倍

### 占有 / 解除

- **Allow Replace**: 他のプレイヤーを装着中でも別のプレイヤーで上書きを許すか
- **Detach On Respawn**: 対象がリスポーンしたら解除
- **Reset On Detach**: 解除時に初期の位置・回転・スケールへ戻す

### イベント通知

- **Network Event Target / Receivers / Attach Event Name / Detach Event Name / Notify Siblings**: 上記「イベント」参照

## 装着元ID(channel)

複数のギミックが同じ Core プール(例: ヒエラルキー直下の `ConstraintCores` に「対象ボーン × 対応人数」分)を共有するとき、
「どのギミックが装着したか」を `channel`(int)として Core が同期します。

- `AttachToBoneOnChannel(playerId, bone, channel)` / `AttachRequested()`(`Requested Channel`)で装着元を指定します。`AttachToBone` / `AttachLocalPlayer` は `Default Channel` を使います
- 追従器(`PlayerBoneConstraint` / `PlayerRestraintConstraint` / `StickyLine`)の `Channel` に自分のギミックの値を入れると、その channel の装着だけに反応します(-1 = すべて)
- 読み取り用に `channel`(ミラー)と `IsAttachedOnChannel(int)` を公開しています

例えば、引き寄せるギミック = 1、その場に留めるギミック = 2 のように、ギミックごとに番号を決めて使います。どの Core を使うかは、ギミックが空いているものを実行時に選べます。

## 同期と所有権

- 装着 / 解除を呼んだクライアントがオーナーになり、`RequestSerialization` で全員に配信します。
- 対象プレイヤーが退室したら全クライアントでローカルに解除し、オーナーが同期状態を掃除します。オーナー自身が退室した場合は新オーナーが再送します。
- 同時に 2 人が装着した場合は後着が勝ち、負けた側は `OnBoneDetach` → `OnBoneAttach` の順で収束します。

## ボーンを読む static ヘルパ

追従器やほかのギミックからも使えるように、ボーンを読む関数を `public static` で公開しています。

| 関数 | 内容 |
|---|---|
| `TryGetBonePose(player, bone, out position, out rotation)` | ボーンの位置・回転を取得します。アバターに無いボーンや、読み込み中なら false |
| `TryGetBonePoseOrParent(player, bone, out position, out rotation, out resolvedBone)` | `bone` が取れなければ親へさかのぼり、最初に取れたボーンの位置・回転を返します。どのボーンで取れたかは `resolvedBone` に入ります。Hips でも取れないときは false で、`resolvedBone` は `bone` のままです |
| `ParentBone(bone)` | Humanoid の階層で一つ上のボーンを返します(下の表)。Hips の親は Hips です |
| `FindClosestBone(player, point)` | 主要 19 ボーンのうち、`point` に最も近いボーンを返します |
| `GetAvatarHeightScale(player, referenceEyeHeight)` | 目線の高さが `referenceEyeHeight` のとき 1 になる倍率 |

### 省略できるボーンとさかのぼる順

Humanoid のアバターは、Neck・Chest・UpperChest・Shoulder・指・つま先(Toes)・目(Eye)・顎(Jaw)を持たないことがあります。
これらのボーンを `TryGetBonePose` で読むと false になるので、`TryGetBonePoseOrParent` で次の表の親へさかのぼって代わりのボーンを使います。

| ボーン | 親 |
|---|---|
| Spine | Hips |
| Chest | Spine |
| UpperChest | Chest |
| Neck | UpperChest |
| Head | Neck |
| LeftEye / RightEye / Jaw | Head |
| LeftShoulder / RightShoulder | UpperChest |
| UpperArm | 同じ側の Shoulder |
| LowerArm | 同じ側の UpperArm |
| Hand | 同じ側の LowerArm |
| 各指の Proximal | 同じ側の Hand |
| 各指の Intermediate | 同じ指の Proximal |
| 各指の Distal | 同じ指の Intermediate |
| LeftUpperLeg / RightUpperLeg | Hips |
| LowerLeg | 同じ側の UpperLeg |
| Foot | 同じ側の LowerLeg |
| Toes | 同じ側の Foot |
| Hips(と LastBone などの範囲外の値) | Hips |

たとえば Neck は UpperChest → Chest → Spine → Hips の順にさかのぼり、Shoulder も UpperChest から同じ順にたどります。
指先からでも Hips まで 10 回でたどり着くので、`TryGetBonePoseOrParent` は最大 12 回で必ず止まります。

### さかのぼって取ったボーンを使うときの注意

- オフセットは、取れたボーン(`resolvedBone`)の座標系を基準に掛かります。オフセットを記録する側も掛ける側も、頼んだ `bone` ではなく `resolvedBone` を使ってください。頼んだボーンの座標系のつもりで掛けると、親ボーンの向きでずれます
- どのボーンに解決されるかは、クライアントごとに違うことがあります(アバターを非表示にしている、読み込みが終わっていないなど)。全員でそろえたいときは、装着する側で解決したボーンを同期してください

## 注意

- **Core の GameObject は常時アクティブにしてください。** 非アクティブな UdonBehaviour は同期データを受け取れず、追従もしません。
  見た目の ON/OFF は子オブジェクトや `receivers` 側で行います。
- 同じ GameObject に置く他の UdonBehaviour は SyncMethod を **Manual** に揃えてください(混在は SDK が警告します)。
- 追従は `PostLateUpdate` で行います(アニメ / IK 適用後のボーン位置を読むため)。
- `receivers` と同じ GameObject の UdonBehaviour を二重に登録するとイベントが 2 回届きます。
