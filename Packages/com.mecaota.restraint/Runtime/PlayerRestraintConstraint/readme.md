# PlayerBoneConstraint

## 概要

このコンポーネントをセットして、PlayerIDとBoneを指定することで指定した部位に追従するPlayer用のBone Constraintです。

PlayerIDを指定せずに別のコンポーネントから指定すると、動的にPlayerに追従できます。

## 設定項目

### ターゲット設定

#### Target Player ID

`PlayerId` を設定します。設定するとBoneへの追従を開始します。

#### Target Bone

追従するボーンを設定します。Target Player IDのプレイヤーの指定したボーンに追従します。

### 追従設定

#### FollowMode

追従する

#### Follow Strength

追従の強さを設定します。

#### Follow Rotation

Rotationも追従します。設定しない場合はオブジェクトのRotationのまま位置だけが追従します。

#### Position Offset

追従するPositionに位置オフセットを設定します。

#### Rotation Offset

追従するRotationに回転オフセットを設定します。

#### Reset Position On Disable

オブジェクトがDisableになった際に初期位置に戻します。
