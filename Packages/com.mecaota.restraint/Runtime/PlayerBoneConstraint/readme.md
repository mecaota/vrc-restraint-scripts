# PlayerBoneConstraint

## 概要

`RestraintCore` に装着されたプレイヤーのボーンへ、この GameObject を追従させる追従器です(VRC Position Constraint のプレイヤー版)。

「誰に装着されているか」は Core が同期・管理します。このコンポーネントは Core の `OnBoneAttach` / `OnBoneDetach` を受けて
追従を開始 / 停止します。Core の `Receivers` に登録するか、Core と同じ GameObject に置いてください。
非アクティブ中にイベントを取りこぼしても、再有効化時と毎フレームのチェックで Core の状態に追いつきます。

1 つの Core に部位ごとの追従器を複数ぶら下げる(吊り下げギミックの各部位の繭など)用途と、
Core と同居して Core が装着したボーンに装飾を追従させる用途の両方に使えます。

## 設定項目

### コア

- **Core**: 装着状態を持つ `RestraintCore`。未設定なら同じ GameObject → 親 の順で探します
- **Use Core Bone**: ON なら Core が装着したボーンに追従します。OFF なら下の Target Bone に追従します

### ターゲット設定

- **Target Bone**: 追従するボーン(Use Core Bone が OFF のとき)

### 追従設定

- **Follow Strength**: 追従の強さ(0-1)。装着直後の 1 フレームは必ず 1 でスナップします
- **Follow Position / Position Offset**: 位置の追従とオフセット(ボーン座標系)
- **Follow Rotation / Rotation Offset**: 回転の追従とオフセット(Euler 角)。OFF ならオブジェクトの回転はそのまま
- **Follow Scale / Scale Offset / Reference Eye Height**: 目線の高さに応じたスケール追従

### 解除

- **Reset On Detach**: 解除時(および非アクティブ化時)に初期の位置・回転・スケールへ戻します

## 読み取り

- `targetPlayerId`: 追従中のプレイヤー ID(-1 = 未追従)。Core のミラーです
- `IsAttached()`: 追従中で対象が在室しているか

## 派生

`protected virtual void UpdateConstraint(VRCPlayerApi)` を override して追従処理を差し替えられます。
`ResolveBone()` で実際の追従ボーン、`snapPending` で装着直後のフレームかを参照できます。
