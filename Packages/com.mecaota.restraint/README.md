# vrc_restraint_scripts
拘束できるVRChatのワールドギミック用スクリプト

## PlayerPullController

蜘蛛の糸玉に捕縛されたローカルプレイヤーを`pullAnchor`へ一定速度で引き寄せます。移動操作すると引き寄せ速度が下がり、入力方向へ自分でも移動できます。`moveAllow`と`minPullSpeed`で「遅いが移動できる」（moveAllow=1,minPull=0）／「移動不可・速度が下がるだけ」（moveAllow=0,minPull>0）を切り替えます。`SetVelocity`はローカル専用なので各クライアントで自分1体だけ駆動し、他者は位置同期で見えます。捕縛判定は`boneConstraints`（繭）を毎フレーム監視して`targetPlayerId==LocalPlayer`で導出するため、繭の解除に自動連動します。`exclusiveConstraints`（吊り下げ拘束など）に自分が入っている間は駆動を止めます。

## CocoonSpinStation

糸疣まで引き寄せられて到達したローカルプレイヤーを`VRCStation`に着席させ、`PlayerMobility=Immobilize`で移動不能にしたうえで、着席pivot(`spinPivot`)を回して体を横倒しのまま長軸周りにくるくる回します。横倒しの姿勢そのものはVRCStationの`animatorController`(横倒しポーズ)が担い、本スクリプトは着席・回転・繭連動・退席だけを受け持ちます。回転角度は`GetServerTimeInSeconds()`から導出するので同期変数なしで全員の画面が一致します。捕縛判定は`boneConstraints`(繭)を毎フレーム監視し、`pullAnchor`(糸疣)へ`grabDistance`まで近づいたら着席、繭が外れると自動で降ります。`UseStation`/`ExitStation`はローカル専用・着席事実はVRChatが自動同期するため`SyncMode None`。Station1台=同時1人対応。横倒しポーズのanimはワールド側で用意します(本リポジトリには含みません)。
