# PlayerRestraintConstraint

## 概要

`RestraintCore` に装着されたプレイヤーが「自分」のとき、自分をこの GameObject の位置へ拘束します
(`PlayerBoneConstraint` の逆で、オブジェクトではなくプレイヤーを動かします)。

`SetVelocity` はローカルプレイヤーにしか効かないため、Core の装着イベントを受けても、対象が自分のクライアントでだけ動作します。
他人が拘束される様子は VRChat 標準の位置同期で見えます。解除(Respawn / 退室 / Detach)は Core が担います。

Core の `Receivers` に登録するか、Core と同じ GameObject に置いてください。

## 設定項目

### コア

- **Core**: 装着状態を持つ `RestraintCore`。未設定なら同じ GameObject → 親 の順で探します

### ターゲット設定

- **Target Bone**: 拘束の基準にするボーン(このボーンがオブジェクト位置に来ます)

### 拘束設定

- **Follow Strength**: 拘束の強さ(0-1)。差分 / dt に掛けます
- **Position Offset**: 拘束位置のオフセット(ワールド座標)
- **Snap To Player On Attach**: ON なら装着直後の 1 フレームでオブジェクトをプレイヤーのボーン位置へ合わせ、現在地で拘束を始めます

### 軸無視設定

- **Ignore X / Y / Z**: その軸方向は拘束しません

### 解除

- **Reset On Detach**: 解除時に初期位置へ戻します

## 読み取り

- `targetPlayerId`: Core が装着しているプレイヤー ID(自分でなくても入ります)
- `IsAttached()`: 自分がこの拘束で駆動されているか
