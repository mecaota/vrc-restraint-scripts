# vrc_restraint_scripts
拘束できるVRChatのワールドギミック用スクリプト

## PlayerPullController

蜘蛛の糸玉に捕縛されたローカルプレイヤーを`pullAnchor`へ一定速度で引き寄せます。移動操作すると引き寄せ速度が下がり、入力方向へ自分でも移動できます。`moveAllow`と`minPullSpeed`で「遅いが移動できる」（moveAllow=1,minPull=0）／「移動不可・速度が下がるだけ」（moveAllow=0,minPull>0）を切り替えます。`SetVelocity`はローカル専用なので各クライアントで自分1体だけ駆動し、他者は位置同期で見えます。捕縛判定は`boneConstraints`（繭）を毎フレーム監視して`targetPlayerId==LocalPlayer`で導出するため、繭の解除に自動連動します。`exclusiveConstraints`（吊り下げ拘束など）に自分が入っている間は駆動を止めます。
