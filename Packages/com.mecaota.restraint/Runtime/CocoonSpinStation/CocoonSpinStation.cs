using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 蜘蛛の糸疣まで引き寄せられて到達したローカルプレイヤーを VRCStation に着席させ、
/// 移動不能(Immobilize)にしたうえで、着席pivotを回して体を横倒しのまま長軸周りにくるくる回す。
///
/// 役割分担:
/// ・横倒しの姿勢そのものは VRCStation の animatorController(AnywhereAnimationSystem の
///   「右横向き」等のポーズ anim)が担う。着席するとそのポーズがアバターに適用される。
/// ・本スクリプトは「着席させる/回す/繭が外れたら降ろす」だけを受け持つ。回転は
///   stationEnterPlayerLocation に割り当てた spinPivot を回すことで、横倒し姿勢ごと回す。
///
/// 設計の要点:
/// ・着席/退席は VRCStation.UseStation/ExitStation でローカルプレイヤーのみ実行できる。
///   「誰が座ったか」は VRChat が自動同期し、OnStationEntered/Exited は全員で発火するので
///   player.isLocal で自分と他人を分ける。→ 同期変数(UdonSynced)は一切不要。
/// ・回転角度は Networking.GetServerTimeInSeconds()(全クライアント共通のサーバー時刻)から
///   算出するので、同期しなくても全員の画面で回転がほぼ一致する。
/// ・捕縛判定は繭スロット(boneConstraints)を毎フレーム監視し、糸疣(pullAnchor)へ
///   grabDistance まで近づいたら着席。繭が外れる(退出/リセット/Respawn)と自動で降りる。
/// ・PlayerMobility=Immobilize で着席中は自力移動できない(＝拘束)。
/// ・「1人だけ」対応: Station は1台。誰か着席中(_occupied)は他の人は着席させない。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CocoonSpinStation : UdonSharpBehaviour
{
    [Header("参照")]
    [Tooltip("着席させる VRCStation(このGameObjectに付ける)")]
    public VRCStation station;

    [Tooltip("回す着席pivot(= station.stationEnterPlayerLocation)。これを長軸周りに回すとアバターが回る")]
    public Transform spinPivot;

    [Tooltip("監視する繭。どれかに自分のplayerIdが入っていて糸疣へ到達したら着席する")]
    public PlayerBoneConstraint[] boneConstraints;

    [Tooltip("到達判定に使う糸疣アンカー(PlayerPullControllerのpullAnchorと同じで良い)")]
    public Transform pullAnchor;

    [Header("着席条件")]
    [Tooltip("糸疣へこの水平距離(m)まで近づいたら着席。PlayerPullControllerのarriveDistance以下にする")]
    public float grabDistance = 0.7f;

    [Header("回転演出")]
    [Tooltip("回転速度(度/秒)。90で約0.25回転/秒")]
    public float rotationSpeed = 90f;

    [Tooltip("回す軸(spinPivotのローカル軸)。横倒しポーズの体の長軸に合わせて実機で微調整する")]
    public Vector3 spinAxis = Vector3.forward;

    [Tooltip("着席直後に回転速度を立ち上げる時間(秒)。急な回転で酔わないための猶予")]
    public float rampUpTime = 1.5f;

    private VRCPlayerApi _local;
    private bool _occupied;        // 誰か着席中か(OnStationEntered/Exitedで全員一致)
    private bool _seatedLocal;     // 自分が着席したか
    private double _sitServerTime; // 着席したサーバー時刻(回転の起点)

    void Start()
    {
        _local = Networking.LocalPlayer;
        if (station != null)
        {
            station.PlayerMobility = VRCStation.Mobility.Immobilize; // 着席中は移動不能
            station.disableStationExit = true;                      // 勝手に降りられない(降ろすのは本スクリプト)
        }
        if (spinPivot != null) { spinPivot.localRotation = Quaternion.identity; }
    }

    void Update()
    {
        // --- 回転(全クライアントで実行): 着席中はサーバー時刻から角度を出して回す
        if (_occupied && spinPivot != null)
        {
            float t = (float)(Networking.GetServerTimeInSeconds() - _sitServerTime);
            if (t < 0f) { t = 0f; }
            float ramp = (rampUpTime > 0.01f) ? Mathf.Clamp01(t / rampUpTime) : 1f;
            float ang = t * rotationSpeed * ramp; // 立ち上がり込み。tと共に単調増加
            Vector3 ax = (spinAxis.sqrMagnitude > 1e-6f) ? spinAxis.normalized : Vector3.forward;
            spinPivot.localRotation = Quaternion.AngleAxis(ang, ax);
        }

        // --- 着席/退席判定(ローカルプレイヤーのみ)
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
                    station.UseStation(_local);
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
        _sitServerTime = Networking.GetServerTimeInSeconds();
        if (player != null && player.isLocal) { _seatedLocal = true; }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player != null && player.isLocal) { _seatedLocal = false; }
        _occupied = false;
        if (spinPivot != null) { spinPivot.localRotation = Quaternion.identity; }
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
