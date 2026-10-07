using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// RestraintCore に装着されたプレイヤーが「自分」のとき、自分をこの GameObject の位置へ拘束する
/// (PlayerBoneConstraint の逆: オブジェクトではなくプレイヤーを動かす)。
///
/// SetVelocity はローカルプレイヤーにしか効かないため、Core の装着イベントを受けても
/// 対象が自分のクライアントでだけ動作する。他人が拘束される様子は VRChat 標準の位置同期で見える。
/// 装着直後の 1 フレームは(snapToPlayerOnAttach=ON なら)オブジェクト側を対象ボーンへ合わせ、
/// 以降は毎フレーム「オブジェクト位置(+オフセット) − ボーン位置」の差を速度に変換して与える。
/// 解除は Core が担う(Respawn/退室/Detach)。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerRestraintConstraint : UdonSharpBehaviour
{
    [Header("コア")]
    [Tooltip("装着状態を持つ RestraintCore。未設定なら同じ GameObject → 親 の順で探す")]
    public RestraintCore core;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する")]
    public int channel = RestraintCore.CHANNEL_ANY;

    [Header("ターゲット設定")]
    [Tooltip("拘束の基準にするプレイヤーのボーン(このボーンがオブジェクト位置に来る)")]
    public HumanBodyBones targetBone = HumanBodyBones.Hips;

    [Header("拘束設定")]
    [Tooltip("拘束の強さ(0-1)。差分/dt に掛ける")]
    public float followStrength = 1f;

    [Tooltip("拘束位置のオフセット(ワールド座標)")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("装着直後の最初の更新でオブジェクト(アンカー)をプレイヤーのボーン位置へ動かすか。OFF ならアンカーは固定でプレイヤーをそこへ引き寄せる(吊り下げ等)。ON は「現在地で拘束を始める」用途")]
    public bool snapToPlayerOnAttach = false;

    [Header("軸無視設定")]
    [Tooltip("X軸を無視するか")]
    public bool ignoreX = false;

    [Tooltip("Y軸を無視するか")]
    public bool ignoreY = false;

    [Tooltip("Z軸を無視するか")]
    public bool ignoreZ = false;

    [Header("解除")]
    [Tooltip("解除時に初期の位置(ローカル)へ戻すか")]
    public bool resetOnDetach = true;

    /// <summary>Core が装着しているプレイヤーID(ミラー。-1=未装着)。自分でなくても入る。読み取り専用</summary>
    [HideInInspector] public int targetPlayerId = -1;

    /// <summary>外部スクリプト(揺れ等)が毎フレーム書き込む動的な拘束位置オフセット(ワールド座標)。解除時に 0 へ戻る</summary>
    [HideInInspector] public Vector3 dynamicOffset = Vector3.zero;

    private VRCPlayerApi _localPlayer;
    private Vector3 _initialLocalPosition;
    private bool _initialized = false;
    private bool _active = false;      // 対象が自分で拘束を駆動中
    private bool _firstUpdate = false;

    // ------------------------------------------------------------------ ライフサイクル

    private void Initialize()
    {
        if (_initialized) { return; }
        _initialized = true;
        _localPlayer = Networking.LocalPlayer;
        _initialLocalPosition = transform.localPosition;
        if (core == null) { core = GetComponent<RestraintCore>(); }
        if (core == null) { core = GetComponentInParent<RestraintCore>(); }
    }

    private void Start()
    {
        Initialize();
        Resync();
    }

    private void OnEnable()
    {
        Initialize();
        Resync();
    }

    private void OnDisable()
    {
        if (_active && resetOnDetach) { transform.localPosition = _initialLocalPosition; }
        _active = false;
        targetPlayerId = -1;
        dynamicOffset = Vector3.zero;
    }

    // ------------------------------------------------------------------ Core からのイベント

    public void OnBoneAttach()
    {
        Initialize();
        Resync();
    }

    public void OnBoneDetach()
    {
        Initialize();
        Resync();
    }

    /// <summary>Core の状態を読み直し、対象が自分なら拘束を開始、そうでなければ停止する</summary>
    public void Resync()
    {
        int id = (core != null && core.IsAttachedOnChannel(channel)) ? core.targetPlayerId : -1;
        targetPlayerId = id;
        bool shouldDrive = id >= 0 && _localPlayer != null && id == _localPlayer.playerId;
        if (shouldDrive && !_active)
        {
            _active = true;
            _firstUpdate = snapToPlayerOnAttach;
        }
        else if (!shouldDrive && _active)
        {
            _active = false;
            _firstUpdate = false;
            dynamicOffset = Vector3.zero;
            if (resetOnDetach) { transform.localPosition = _initialLocalPosition; }
        }
    }

    // ------------------------------------------------------------------ 拘束

    private void Update()
    {
        if (core == null) { return; }
        int coreId = core.IsAttachedOnChannel(channel) ? core.targetPlayerId : -1;
        if (coreId != targetPlayerId) { Resync(); } // 取りこぼし・channel 違いのフォールバック
        if (!_active) { return; }
        if (_localPlayer == null || !_localPlayer.IsValid()) { return; }

        Vector3 bonePosition;
        Quaternion boneRotation;
        if (!RestraintCore.TryGetBonePose(_localPlayer, targetBone, out bonePosition, out boneRotation)) { return; }

        if (_firstUpdate)
        {
            // 初回は現在地で拘束を始める(オブジェクトをプレイヤーへ合わせる)
            transform.position = bonePosition;
            _firstUpdate = false;
            return;
        }
        RestrainPlayer(_localPlayer, bonePosition);
    }

    private void RestrainPlayer(VRCPlayerApi player, Vector3 bonePosition)
    {
        Vector3 restraintPosition = transform.position + positionOffset + dynamicOffset;
        // 地面にいる場合は拘束位置のYをプレイヤーのY以上にする(地面に埋まらないように)
        if (player.IsPlayerGrounded()) { restraintPosition.y = Mathf.Max(restraintPosition.y, bonePosition.y); }

        Vector3 diff = restraintPosition - bonePosition;
        if (ignoreX) { diff.x = 0f; }
        if (ignoreY) { diff.y = 0f; }
        if (ignoreZ) { diff.z = 0f; }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) { return; }
        player.SetVelocity(diff / deltaTime * followStrength);
    }

    // ------------------------------------------------------------------ 状態問い合わせ

    /// <summary>自分がこの拘束で駆動されているか</summary>
    public bool IsAttached()
    {
        return _active;
    }
}
