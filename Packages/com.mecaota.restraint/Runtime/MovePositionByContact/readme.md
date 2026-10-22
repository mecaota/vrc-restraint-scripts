# MovePositionByContact

## 概要

接触(トリガー / コリジョン / パーティクル)したプレイヤーの、このオブジェクトに最も近いボーンへ、
空いている `RestraintCore` を装着します。

接触イベントは各クライアントで発火しますが、装着の書き込みは接触した本人のクライアントだけが行い、
Core の同期で全員に反映されます。

`RestraintCore`(と、見た目を追従させる `PlayerBoneConstraint`)とセットで利用します。

## 設定項目

### Cores

装着先の `RestraintCore` を指定します。空なら子オブジェクトから自動収集します。
