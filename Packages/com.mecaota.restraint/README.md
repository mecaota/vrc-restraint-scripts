# vrc_restraint_scripts
拘束できるVRChatのワールドギミック用スクリプト

## PlayerPullController

蜘蛛の糸玉に捕縛されたローカルプレイヤーを`pullAnchor`へ一定速度で引き寄せます。移動操作すると引き寄せ速度が下がり、入力方向へ自分でも移動できます。`moveAllow`と`minPullSpeed`で「遅いが移動できる」（moveAllow=1,minPull=0）／「移動不可・速度が下がるだけ」（moveAllow=0,minPull>0）を切り替えます。`SetVelocity`はローカル専用なので各クライアントで自分1体だけ駆動し、他者は位置同期で見えます。捕縛判定は`boneConstraints`（繭）を毎フレーム監視して`targetPlayerId==LocalPlayer`で導出するため、繭の解除に自動連動します。`exclusiveConstraints`（吊り下げ拘束など）に自分が入っている間は駆動を止めます。

## CocoonSpinStation

糸疣まで引き寄せられて到達したローカルプレイヤーを`VRCStation`に着席させ、`PlayerMobility=Immobilize`で移動不能にします。横倒し(頭と足が地面と平行)＋頭と足を軸にしたループ回転はVRCStationの`animatorController`に割り当てたアニメが担い(着席で自動再生・着席中はアバターのアニメも全員へ同期)、本スクリプトはTransformを回さず着席・繭連動・退席だけを受け持ちます。捕縛判定は`boneConstraints`(繭)を毎フレーム監視し、`pullAnchor`(糸疣)へ`grabDistance`まで近づいたら着席、繭が外れると自動で降ります。`UseStation`/`ExitStation`はローカル専用・着席事実はVRChatが自動同期するため`SyncMode None`。Station1台=同時1人対応。横倒し回転アニメはワールド側で用意します(本リポジトリには含みません)。
