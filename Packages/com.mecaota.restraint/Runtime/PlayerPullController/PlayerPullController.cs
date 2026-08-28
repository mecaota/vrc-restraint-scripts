using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Rendering;
using VRC.Udon.Common;

/// <summary>
/// 蜘蛛の糸玉に捕縛された（繭が張り付いた）ローカルプレイヤーを pullAnchor へ引き寄せる。
/// プレイヤーが移動操作するとその入力量に応じて引き寄せ速度が下がり、入力方向へ自分でも
/// 移動できる（値調整で「遅いが移動できる」と「移動不可・速度が下がるだけ」を切り替え可能）。
///
/// 設計の要点（設計レビュー準拠）:
/// ・SetVelocity はローカルプレイヤーにしか効かないので、各クライアントで自分1体だけ駆動する。
///   他人が引かれる様子は各自のクライアントが駆動し、標準の位置同期で見える（同期は不要 = SyncMode None）
/// ・捕縛判定は繭スロット(boneConstraints)を毎フレーム見て「自分の playerId が入っているか」で導出。
///   退出/リセット/Respawn で繭が Detach してスロットが空くと引き寄せも自動で止まる（解除配線ゼロ）
/// ・引き寄せは一定速度(pullSpeed)方式。誤差/dt 方式だと遠距離で巨大速度になり VR 酔い/ワープになる
/// ・Y 速度は GetVelocity().y で保存して重力/ジャンプを温存。水平だけ差し替える
/// ・木の吊り下げ拘束も SetVelocity を使うので、吊られている間は引き寄せを止める（相互排他）
///
/// 入力取得は InputFlyingSystem(fukuroudon) と同じ流儀。VR=GetRotation / Desktop=ScreenCamera.Rotation。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerPullController : UdonSharpBehaviour
{
    [Header("引き寄せ先")]
    [Tooltip("引き寄せる目標（蜘蛛の糸疣付近。ボーンの子にすればアニメに追従する）")]
    public Transform pullAnchor;

    [Header("捕縛判定")]
    [Tooltip("監視する繭。どれかに自分の playerId が入っていたら引き寄せを駆動する")]
    public PlayerBoneConstraint[] boneConstraints;

    [Tooltip("吊り下げ拘束（同時に SetVelocity すると競合するので、これに自分が入っている間は引き寄せを止める）")]
    public PlayerBoneConstraint[] exclusiveConstraints;

    [Tooltip("引き寄せ（引っ張り上げ）が始まっている間オフにするオブジェクト。糸玉パーティクルなど。捕縛が解けると自動で戻す（ローカルのみ）")]
    public GameObject hideOnPull;

    [Header("引き寄せ")]
    [Tooltip("基本の引き寄せ速度 (m/s)")]
    public float pullSpeed = 2.0f;

    [Tooltip("移動入力に対する引き寄せ減衰 (0..1)。1で全力入力時に minPullSpeed まで下がる")]
    public float resistanceStrength = 1.0f;

    [Tooltip("全力入力時に残る引き寄せ速度 (m/s)。0で「振り切れる」、>0で「遅くなるだけ・必ず引かれる」")]
    public float minPullSpeed = 0.0f;

    [Tooltip("入力方向への自己移動の許容 (0..1)。1で「遅いが自分で移動できる」、0で「移動不可」")]
    public float moveAllow = 1.0f;

    [Tooltip("自己移動の基準速度。0以下なら GetWalkSpeed() を使う")]
    public float moveSpeed = 0f;

    [Tooltip("この距離（水平m）まで近づいたら引き寄せを止める（拘束は残る）")]
    public float arriveDistance = 0.6f;

    [Header("酔い対策")]
    [Tooltip("捕縛直後に引き寄せ速度を立ち上げる時間 (秒)")]
    public float rampUpTime = 0.4f;

    [Tooltip("速度変化の上限 (m/s^2)。急激な引き寄せを抑える")]
    public float maxAccel = 12f;

    [Tooltip("入力の不感帯")]
    public float deadZone = 0.05f;

    private VRCPlayerApi _local;
    private VRCCameraSettings _camera;
    private float _h, _v;      // 移動入力（-1..1、変化時イベントでキャッシュ）
    private float _ramp;       // 立ち上がり係数
    private Vector3 _applied;  // 前フレームに与えた速度（加速度制限用）
    private bool _driving;

    void Start()
    {
        _local = Networking.LocalPlayer;
        _camera = VRCCameraSettings.ScreenCamera;
    }

    public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
    {
        _h = (Mathf.Abs(value) >= deadZone) ? value : 0f;
    }

    public override void InputMoveVertical(float value, UdonInputEventArgs args)
    {
        _v = (Mathf.Abs(value) >= deadZone) ? value : 0f;
    }

    void Update()
    {
        if (_local == null || !_local.IsValid()) { return; }
        int myId = _local.playerId;

        // 引き寄せ（引っ張り上げ）が始まったら糸玉パーティクルを消す。繭が外れたら戻す（ローカル）
        bool captured = IsHeldBy(boneConstraints, myId);
        if (hideOnPull != null && hideOnPull.activeSelf == captured) { hideOnPull.SetActive(!captured); }

        // 吊り下げ等で拘束中なら引き寄せを止める（SetVelocity競合の回避）
        if (IsHeldBy(exclusiveConstraints, myId)) { _driving = false; _ramp = 0f; return; }

        // 繭スロットに自分が入っているか（捕縛判定）
        if (!captured) { _driving = false; _ramp = 0f; return; }
        if (pullAnchor == null) { return; }

        // 引き寄せ方向（水平）
        Vector3 pos = _local.GetPosition();
        Vector3 to = pullAnchor.position - pos;
        to.y = 0f;
        float dist = to.magnitude;
        // 到達したら駆動停止（通常操作に復帰。繭＝拘束は残る）
        if (dist <= arriveDistance) { _driving = false; _ramp = 0f; return; }
        Vector3 pullDir = to / dist;

        // 立ち上がりランプ
        _ramp = Mathf.MoveTowards(_ramp, 1f, Time.deltaTime / Mathf.Max(rampUpTime, 0.01f));

        // 入力ベクトルをワールド水平方向へ
        float inputMag = Mathf.Clamp01(new Vector2(_h, _v).magnitude);
        Quaternion rot = _local.IsUserInVR() ? _local.GetRotation()
            : (_camera != null ? _camera.Rotation : _local.GetRotation());
        Vector3 inputWorld = rot * new Vector3(_h, 0f, _v);
        inputWorld.y = 0f;
        inputWorld = (inputWorld.sqrMagnitude > 1e-4f) ? inputWorld.normalized : Vector3.zero;

        // 引き寄せ成分（入力量で減衰、minPullSpeedで下限）
        float resist = Mathf.Clamp01(resistanceStrength * inputMag);
        float effPull = Mathf.Lerp(pullSpeed, minPullSpeed, resist) * _ramp;
        Vector3 pullVel = pullDir * effPull;

        // 自己移動成分
        float ms = (moveSpeed > 0f) ? moveSpeed : _local.GetWalkSpeed();
        Vector3 moveVel = inputWorld * (ms * moveAllow * inputMag);

        // 合成（水平）。Yは重力/ジャンプを保存
        Vector3 horiz = pullVel + moveVel;
        float keptY = _local.GetVelocity().y;

        // 加速度制限で急変を抑える
        if (!_driving) { _applied = new Vector3(horiz.x, keptY, horiz.z); _driving = true; }
        Vector3 desired = new Vector3(horiz.x, keptY, horiz.z);
        _applied = Vector3.MoveTowards(_applied, desired, maxAccel * Time.deltaTime);
        _applied.y = keptY;
        _local.SetVelocity(_applied);
    }

    // constraints のどれかが playerId を捕まえているか
    private bool IsHeldBy(PlayerBoneConstraint[] constraints, int playerId)
    {
        if (constraints == null) { return false; }
        for (int i = 0; i < constraints.Length; i++)
        {
            var c = constraints[i];
            if (c != null && c.gameObject.activeInHierarchy && c.targetPlayerId == playerId) { return true; }
        }
        return false;
    }
}
