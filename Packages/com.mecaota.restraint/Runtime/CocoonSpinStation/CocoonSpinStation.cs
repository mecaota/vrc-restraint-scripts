using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 蜘蛛の糸疣まで引き寄せられて到達したローカルプレイヤーを VRCStation に着席させ、
/// 移動不能(Immobilize)にする。
///
/// 横倒し(頭と足が地面と平行)＋頭と足を軸にした回転は、VRCStation の animatorController に
/// 割り当てたループアニメが担う(着席すると自動再生される)。本スクリプトはスクリプト側で
/// Transform を回さず、「着席させる/繭が外れたら降ろす」だけを受け持つ。
///
/// 設計の要点:
/// ・着席/退席は VRCStation.UseStation/ExitStation でローカルプレイヤーのみ実行できる。
///   「誰が座ったか」は VRChat が自動同期し、着席中はアバターのアニメも全員へ同期される。
///   よって同期変数(UdonSynced)は不要(SyncMode None)。
/// ・捕縛判定は繭スロット(boneConstraints)を毎フレーム監視し、糸疣(pullAnchor)へ
///   grabDistance まで近づいたら着席。繭が外れる(退出/リセット/Respawn)と自動で降りる。
/// ・PlayerMobility=ImmobilizeForVehicle で着席中は自力移動できず、着席アニメが適用される(拘束)。
/// ・「1人だけ」対応: Station は1台。誰か着席中(_occupied)は他の人は着席させない。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CocoonSpinStation : UdonSharpBehaviour
{
    [Header("参照")]
    [Tooltip("着席させる VRCStation(このGameObjectに付ける)。横倒し回転アニメは animatorController 側で設定する")]
    public VRCStation station;

    [Tooltip("監視する繭。どれかに自分のplayerIdが入っていて糸疣へ到達したら着席する")]
    public PlayerBoneConstraint[] boneConstraints;

    [Tooltip("到達判定に使う糸疣アンカー(PlayerPullControllerのpullAnchorと同じで良い)")]
    public Transform pullAnchor;

    [Header("着席条件")]
    [Tooltip("糸疣へこの水平距離(m)まで近づいたら着席。PlayerPullControllerのarriveDistance以下にする")]
    public float grabDistance = 0.7f;

    private VRCPlayerApi _local;
    private bool _occupied;     // 誰か着席中か(OnStationEntered/Exitedで全員一致)
    private bool _seatedLocal;  // 自分が着席したか

    void Start()
    {
        _local = Networking.LocalPlayer;
        if (station != null)
        {
            station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle; // 移動不能＋着席アニメ適用(Immobilizeだと棒立ちになる)
            station.disableStationExit = true;                      // 勝手に降りられない(降ろすのは本スクリプト)
        }
    }

    void Update()
    {
        if (_local == null || !_local.IsValid()) { return; }
        int myId = _local.playerId;

        if (!_seatedLocal)
        {
            // まだ座っていない: 自分が繭に入っていて糸疣へ到達し、Stationが空いていれば座る
            if (!_occupied && station != null && pullAnchor != null && IsHeldBy(myId))
            {
                Vector3 to = pullAnchor.position - _local.GetPosition();
                to.y = 0f;
                if (to.magnitude <= grabDistance)
                {
                    station.UseStation(_local); // 着席→animatorControllerの横倒し回転アニメが再生される
                }
            }
        }
        else
        {
            // 座っている: 繭が外れたら降りる
            if (!IsHeldBy(myId) && station != null)
            {
                station.ExitStation(_local);
            }
        }
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        _occupied = true;
        if (player != null && player.isLocal) { _seatedLocal = true; }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player != null && player.isLocal) { _seatedLocal = false; }
        _occupied = false;
    }

    // boneConstraints のどれかが playerId を捕まえているか
    private bool IsHeldBy(int playerId)
    {
        if (boneConstraints == null) { return false; }
        for (int i = 0; i < boneConstraints.Length; i++)
        {
            var c = boneConstraints[i];
            if (c != null && c.gameObject.activeInHierarchy && c.targetPlayerId == playerId) { return true; }
        }
        return false;
    }
}
