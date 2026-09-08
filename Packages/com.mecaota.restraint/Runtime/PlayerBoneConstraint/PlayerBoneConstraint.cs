using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// RestraintCore に装着されたプレイヤーのボーンへ、この GameObject を追従させる追従器
/// (VRC Position Constraint のプレイヤー版)。
///
/// 「誰に装着されているか」は Core が同期・管理し、本コンポーネントは Core の OnBoneAttach / OnBoneDetach を受けて
/// 追従を開始/停止する。Core の receivers に登録するか、Core と同じ GameObject に置く(兄弟イベント)。
/// 非アクティブ中にイベントを取りこぼしても OnEnable と毎フレームのエッジ検出で Core の状態に追いつく。
///
/// 追従先ボーンは自分の targetBone(useCoreBone=OFF)か、Core が装着したボーン(useCoreBone=ON)。
/// 1 つの Core に部位ごとの追従器を複数ぶら下げる(吊り下げギミックの各部位繭など)用途と、
/// Core と同居して Core のボーンに装飾を追従させる用途の両方に使える。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerBoneConstraint : UdonSharpBehaviour
{
    [Header("コア")]
    [Tooltip("装着状態を持つ RestraintCore。未設定なら同じ GameObject → 親 の順で探す")]
    public RestraintCore core;

    [Tooltip("ON なら Core が装着したボーンに追従する。OFF なら下の targetBone に追従する")]
    public bool useCoreBone = false;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する。複数ギミックで Core を共有するとき、自分のギミックの装着だけに反応させる")]
    public int channel = RestraintCore.CHANNEL_ANY;

    [Header("ターゲット設定")]
    [Tooltip("追従するボーン(useCoreBone=OFF のとき)")]
    public HumanBodyBones targetBone = HumanBodyBones.Hips;

    [Header("追従設定")]
    [Tooltip("追従の強さ(0-1)。1 で即座に一致。装着直後の 1 フレームは必ず 1 でスナップする")]
    public float followStrength = 1f;

    [Header("位置追従設定")]
    [Tooltip("位置を追従するか")]
    public bool followPosition = true;

    [Tooltip("位置オフセット(ボーン座標系)")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("回転追従設定")]
    [Tooltip("回転を追従するか")]
    public bool followRotation = false;

    [Tooltip("回転オフセット(Euler角)")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("スケール追従設定")]
    [Tooltip("スケールを追従するか")]
    public bool followScale = false;

    [Tooltip("スケールオフセット(身長倍率に掛ける)")]
    public Vector3 scaleOffset = Vector3.one;

    [Tooltip("この目線の高さ(m)のとき scaleOffset が等倍で適用される")]
    public float referenceEyeHeight = 2f;

    [Header("解除")]
    [Tooltip("解除時に初期の位置・回転・スケール(ローカル)へ戻すか")]
    public bool resetOnDetach = true;

    /// <summary>追従中のプレイヤーID(Core のミラー。-1=未追従)。読み取り専用</summary>
    [HideInInspector] public int targetPlayerId = -1;

    protected Vector3 initialLocalPosition;
    protected Quaternion initialLocalRotation;
    protected Vector3 initialLocalScale;
    protected VRCPlayerApi playerCache;
    /// <summary>装着直後の最初の更新か(派生クラスはこのフレームだけ強さ 1 で合わせる)</summary>
    protected bool snapPending;

    private bool _initialized = false;
    private bool _following = false;

    // ------------------------------------------------------------------ ライフサイクル

    protected void Initialize()
    {
        if (_initialized) { return; }
        _initialized = true;
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;
        if (core == null) { core = GetComponent<RestraintCore>(); }
        if (core == null) { core = GetComponentInParent<RestraintCore>(); }
    }

    protected void Start()
    {
        Initialize();
        Resync();
    }

    protected void OnEnable()
    {
        Initialize();
        Resync(); // 非アクティブ中に落ちたイベントの補完
    }

    protected void OnDisable()
    {
        // 非表示になったら初期位置へ(表示復帰時は OnEnable の Resync で追従再開)
        if (_following && resetOnDetach) { ResetTransform(); }
        _following = false;
        targetPlayerId = -1;
        playerCache = null;
    }

    // ------------------------------------------------------------------ Core からのイベント

    /// <summary>RestraintCore の装着イベント(receivers 登録または兄弟)</summary>
    public void OnBoneAttach()
    {
        Initialize();
        Resync();
    }

    /// <summary>RestraintCore の解除イベント</summary>
    public void OnBoneDetach()
    {
        Initialize();
        Resync();
    }

    /// <summary>Core の状態を読み直して追従の開始/停止を合わせる</summary>
    public void Resync()
    {
        int id = (core != null && core.IsAttachedOnChannel(channel)) ? core.targetPlayerId : -1;
        if (id >= 0)
        {
            if (!_following || targetPlayerId != id)
            {
                _following = true;
                targetPlayerId = id;
                playerCache = null;
                snapPending = true;
                OnFollowStarted();
            }
        }
        else if (_following)
        {
            _following = false;
            targetPlayerId = -1;
            playerCache = null;
            if (resetOnDetach) { ResetTransform(); }
            OnFollowStopped();
        }
    }

    /// <summary>追従開始時のフック(派生クラス用)</summary>
    protected virtual void OnFollowStarted() { }

    /// <summary>追従停止時のフック(派生クラス用)</summary>
    protected virtual void OnFollowStopped() { }

    // ------------------------------------------------------------------ 追従

    public override void PostLateUpdate()
    {
        if (core == null) { return; }
        int coreId = core.IsAttachedOnChannel(channel) ? core.targetPlayerId : -1;
        if (coreId != targetPlayerId) { Resync(); } // 配線漏れ・取りこぼし・channel 違いのフォールバック
        if (!_following) { return; }
        VRCPlayerApi player = ResolvePlayer();
        if (player == null) { return; }
        UpdateConstraint(player);
        snapPending = false;
    }

    /// <summary>毎フレームの追従処理。派生クラスで置き換え/拡張する</summary>
    protected virtual void UpdateConstraint(VRCPlayerApi targetPlayer)
    {
        SetConstraint(targetPlayer);
    }

    /// <summary>位置・回転・スケールの標準追従</summary>
    protected void SetConstraint(VRCPlayerApi targetPlayer)
    {
        Vector3 bonePosition;
        Quaternion boneRotation;
        if (!RestraintCore.TryGetBonePose(targetPlayer, ResolveBone(), out bonePosition, out boneRotation)) { return; }
        float strength = snapPending ? 1f : followStrength;

        if (followPosition)
        {
            Vector3 targetPosition = bonePosition + boneRotation * positionOffset;
            transform.position = Vector3.Lerp(transform.position, targetPosition, strength);
        }
        if (followRotation)
        {
            Quaternion targetRotation = boneRotation * Quaternion.Euler(rotationOffset);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, strength);
        }
        if (followScale)
        {
            float heightScale = RestraintCore.GetAvatarHeightScale(targetPlayer, referenceEyeHeight);
            Vector3 targetScale = Vector3.Scale(Vector3.one * heightScale, scaleOffset);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, strength);
        }
    }

    /// <summary>実際に追従するボーン</summary>
    protected HumanBodyBones ResolveBone()
    {
        return (useCoreBone && core != null) ? core.targetBone : targetBone;
    }

    protected VRCPlayerApi ResolvePlayer()
    {
        if (targetPlayerId < 0) { return null; }
        if (playerCache != null && playerCache.IsValid() && playerCache.playerId == targetPlayerId) { return playerCache; }
        playerCache = VRCPlayerApi.GetPlayerById(targetPlayerId);
        if (playerCache == null || !playerCache.IsValid()) { playerCache = null; }
        return playerCache;
    }

    protected void ResetTransform()
    {
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        transform.localScale = initialLocalScale;
    }

    // ------------------------------------------------------------------ 状態問い合わせ

    /// <summary>追従中か(対象プレイヤーが在室していること)</summary>
    public bool IsAttached()
    {
        return _following && ResolvePlayer() != null;
    }
}
