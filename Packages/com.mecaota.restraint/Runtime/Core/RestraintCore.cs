using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;

/// <summary>
/// 拘束ギミックの基幹コンポーネント。
///
/// 「どのプレイヤーの、どのボーンに、どのオフセットで装着されているか」を [UdonSynced](Manual) で
/// 全クライアントに共有し、その同期状態から
/// ・自分自身のボーン追従(followPosition / followRotation / followScale)
/// ・装着/解除イベントの発火(Inspector の receivers と、同じ GameObject 上の他 UdonBehaviour)
/// を導出する。全クライアントが同じ同期状態から導出するため、遅入室者にも装着状態が復元される。
///
/// 1 Core = 1 プレイヤーの装着セッション。追従させたい装飾は PlayerBoneConstraint 等の追従器を
/// receivers に登録(または同じ GameObject に同居)させ、Core のイベントで動かす。
///
/// 装着トリガーは FukuroUdon の ActiveRelay 等から SendCustomEvent で
/// AttachLocalPlayer / DetachSelf / Detach / Toggle を呼ぶ。
///
/// 注意:
/// ・この GameObject は常時アクティブにしておく(非アクティブな UdonBehaviour は同期を受け取れない)。
///   見た目の切替は子や別オブジェクトで行う。
/// ・同じ GameObject に置く他の UdonBehaviour は SyncMethod を Manual に揃える(混在は SDK が警告する)。
/// ・追従は PostLateUpdate(アニメ/IK 適用後のボーン位置を読むため)。
/// ・複数の同期フィールドを FieldChangeCallback で個別に扱うと逆シリアライズの順序に依存するため、
///   OnDeserialization → ApplyState() の一本に集約している。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RestraintCore : UdonSharpBehaviour
{
    /// <summary>同じ GameObject 上の他 UdonBehaviour へ送る固定イベント名(装着)</summary>
    public const string EVENT_ATTACH = "OnBoneAttach";
    /// <summary>同じ GameObject 上の他 UdonBehaviour へ送る固定イベント名(解除)</summary>
    public const string EVENT_DETACH = "OnBoneDetach";
    public const int NO_PLAYER = -1;
    /// <summary>追従器の channel に指定すると、どの装着元(channel)にも反応する</summary>
    public const int CHANNEL_ANY = -1;

    [Header("ターゲット設定")]
    [Tooltip("ボーン未指定で装着したとき(AttachLocalPlayer / Attach / Toggle)に使うボーン")]
    public HumanBodyBones defaultBone = HumanBodyBones.Hips;

    [Tooltip("装着元ID(channel)の既定値。複数のギミックが同じ Core プールを共有するとき、どのギミックが装着したかを同期し、追従器が自分の channel の装着だけに反応できるようにする")]
    public int defaultChannel = 0;

    [Header("位置追従設定(この GameObject 自身)")]
    [Tooltip("自身の位置をボーンに追従させるか。ハブ専用なら OFF")]
    public bool followPosition = true;

    [Tooltip("装着時の自身の位置・回転をボーン基準のオフセットとして記録するか。OFF なら下の固定オフセットを使う")]
    public bool captureOffsetOnAttach = false;

    [Tooltip("固定の位置オフセット(ボーン座標系、m)。captureOffsetOnAttach=OFF のとき使用")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("回転追従設定")]
    [Tooltip("自身の回転をボーンに追従させるか")]
    public bool followRotation = false;

    [Tooltip("固定の回転オフセット(Euler角、ボーン座標系)。captureOffsetOnAttach=OFF のとき使用")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("スケール追従設定")]
    [Tooltip("アバターの身長に応じて自身をスケールするか")]
    public bool followScale = false;

    [Tooltip("この目線の高さ(m)のとき scaleMultiplier が等倍で適用される")]
    public float referenceEyeHeight = 2f;

    [Tooltip("身長スケールに掛ける倍率")]
    public Vector3 scaleMultiplier = Vector3.one;

    [Header("占有")]
    [Tooltip("他のプレイヤーを装着中でも別のプレイヤーで上書きを許すか。OFF なら占有中の Attach は無視される")]
    public bool allowReplace = false;

    [Header("解除")]
    [Tooltip("対象プレイヤーがリスポーンしたら解除するか")]
    public bool detachOnRespawn = true;

    [Tooltip("解除時に初期の位置・回転・スケール(ローカル)へ戻すか")]
    public bool resetOnDetach = true;

    [Header("イベント通知")]
    [Tooltip("receivers への通知方法。Self=各クライアントでローカル送信(推奨。ApplyState は全員で走るので全員に届く)。All/Owner/Others はオーナーのクライアントだけがネットワーク送信する")]
    public NetworkEventTarget networkEventTarget = NetworkEventTarget.Self;

    [Tooltip("装着/解除を通知する UdonBehaviour。追従器(PlayerBoneConstraint 等)や FukuroUdon のコンポーネントを入れる")]
    public UdonBehaviour[] receivers = new UdonBehaviour[0];

    [Tooltip("装着時に receivers へ送るイベント名。空なら送らない")]
    public string attachEventName = EVENT_ATTACH;

    [Tooltip("解除時に receivers へ送るイベント名。空なら送らない")]
    public string detachEventName = EVENT_DETACH;

    [Tooltip("同じ GameObject 上の他の UdonBehaviour へ OnBoneAttach / OnBoneDetach を自動送信するか(配線不要)。receivers と重複登録しないこと")]
    public bool notifySiblings = true;

    [Header("SendCustomEvent 用引数")]
    [Tooltip("AttachRequested が装着するプレイヤーID。SendCustomEvent は引数を持てないので先に設定してから呼ぶ")]
    public int requestedPlayerId = NO_PLAYER;

    [Tooltip("AttachRequested / SetBoneRequested が使うボーン")]
    public HumanBodyBones requestedBone = HumanBodyBones.Hips;

    [Tooltip("AttachRequested が使う装着元ID(channel)")]
    public int requestedChannel = 0;

    // ---- 読み取り専用の状態ミラー(非アクティブでも読めるようフィールドで公開。外部から書き込まないこと)
    [HideInInspector] public int targetPlayerId = NO_PLAYER;
    [HideInInspector] public HumanBodyBones targetBone = HumanBodyBones.Hips;
    /// <summary>現在の装着元ID(channel)のミラー。未装着時は直前の値が残るので targetPlayerId と併せて読む</summary>
    [HideInInspector] public int channel = 0;

    // ---- 同期状態(正)。書き込みはオーナー取得後、RequestSerialization とセットで行う
    [UdonSynced] private int _syncPlayerId = NO_PLAYER;
    [UdonSynced] private byte _syncBone = (byte)HumanBodyBones.Hips;
    [UdonSynced] private Vector3 _syncPosOffset = Vector3.zero;
    [UdonSynced] private Quaternion _syncRotOffset = Quaternion.identity;
    [UdonSynced] private int _syncChannel = 0;

    // ---- 実行時
    private int _appliedPlayerId = NO_PLAYER; // 最後にイベントを発火した playerId(遷移検出)
    private int _appliedChannel; // 最後にイベントを発火した channel(同じプレイヤーの付け替えの検出)
    private VRCPlayerApi _playerCache;
    private VRCPlayerApi _localPlayer;
    private Vector3 _initialLocalPosition;
    private Quaternion _initialLocalRotation;
    private Vector3 _initialLocalScale;
    private UdonBehaviour[] _siblings = new UdonBehaviour[0];
    private bool _initialized = false;

    // ------------------------------------------------------------------ 初期化・ライフサイクル

    private void Initialize()
    {
        if (_initialized) { return; }
        _initialized = true;
        _localPlayer = Networking.LocalPlayer;
        _initialLocalPosition = transform.localPosition;
        _initialLocalRotation = transform.localRotation;
        _initialLocalScale = transform.localScale;
        CollectSiblings();
    }

    // 同じ GameObject 上の他 UdonBehaviour(自分を除く)を集める(FukuroUdon ManualObjectSync と同じ作り)
    private void CollectSiblings()
    {
        UdonBehaviour[] all = GetComponents<UdonBehaviour>();
        if (all == null) { _siblings = new UdonBehaviour[0]; return; }
        int index = System.Array.IndexOf(all, this);
        if (index < 0) { _siblings = all; return; }
        UdonBehaviour[] others = new UdonBehaviour[all.Length - 1];
        if (index > 0) { System.Array.Copy(all, 0, others, 0, index); }
        if (index < others.Length) { System.Array.Copy(all, index + 1, others, index, others.Length - index); }
        _siblings = others;
    }

    private void Start()
    {
        Initialize();
        ApplyState();
    }

    private void OnEnable()
    {
        Initialize();
        ApplyState(); // 再有効化時に同期状態から復元(装着中なら OnBoneAttach を再送)
    }

    private void OnDisable()
    {
        // 同期状態は触らない(非アクティブ中は同期に参加できない)。ローカルの表示だけ未装着に落とす
        if (_appliedPlayerId >= 0)
        {
            _appliedPlayerId = NO_PLAYER;
            if (resetOnDetach) { ResetTransform(); }
            DispatchDetach();
        }
        _playerCache = null;
    }

    // ------------------------------------------------------------------ 装着 / 解除 API

    /// <summary>ローカルプレイヤーを defaultBone に装着する(SendCustomEvent 可)</summary>
    public void AttachLocalPlayer()
    {
        Initialize();
        if (_localPlayer == null) { return; }
        AttachToBone(_localPlayer.playerId, defaultBone);
    }

    /// <summary>指定プレイヤーを defaultBone に装着する</summary>
    public bool Attach(int playerId)
    {
        return AttachToBone(playerId, defaultBone);
    }

    /// <summary>requestedPlayerId / requestedBone / requestedChannel で装着する(SendCustomEvent 可)</summary>
    public void AttachRequested()
    {
        AttachToBoneOnChannel(requestedPlayerId, requestedBone, requestedChannel);
    }

    /// <summary>指定プレイヤーの指定ボーンに defaultChannel で装着する</summary>
    public bool AttachToBone(int playerId, HumanBodyBones bone)
    {
        return AttachToBoneOnChannel(playerId, bone, defaultChannel);
    }

    /// <summary>
    /// 指定プレイヤーの指定ボーンに、装着元ID(channel)付きで装着する。呼んだクライアントがオーナーになり同期する。
    /// 占有中(別プレイヤー)は allowReplace が OFF なら無視して false を返す。
    /// </summary>
    public bool AttachToBoneOnChannel(int playerId, HumanBodyBones bone, int attachChannel)
    {
        Initialize();
        if (_localPlayer == null) { return false; }
        VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
        if (player == null || !player.IsValid()) { return false; }
        if (_syncPlayerId >= 0 && _syncPlayerId != playerId && !allowReplace) { return false; }

        Networking.SetOwner(_localPlayer, gameObject);
        _syncPlayerId = playerId;
        _syncBone = (byte)(int)bone;
        _syncChannel = attachChannel;
        CaptureOffsets(player, bone);
        ApplyState();
        RequestSerialization();
        return true;
    }

    /// <summary>誰が呼んでも解除する(救助用。SendCustomEvent 可)</summary>
    public void Detach()
    {
        Initialize();
        if (_localPlayer == null) { return; }
        if (_syncPlayerId < 0) { return; }
        Networking.SetOwner(_localPlayer, gameObject);
        _syncPlayerId = NO_PLAYER;
        ApplyState();
        RequestSerialization();
    }

    /// <summary>対象が自分のときだけ解除する(自力脱出用。SendCustomEvent 可)</summary>
    public void DetachSelf()
    {
        Initialize();
        if (_localPlayer == null) { return; }
        if (_syncPlayerId != _localPlayer.playerId) { return; }
        Detach();
    }

    /// <summary>未装着ならローカルプレイヤーを装着、装着中なら(誰であっても)解除する(SendCustomEvent 可)</summary>
    public void Toggle()
    {
        if (_syncPlayerId < 0) { AttachLocalPlayer(); }
        else { Detach(); }
    }

    /// <summary>ボーンを変更する。装着中なら同期し直す(オフセットは captureOffsetOnAttach に従い再計算)。未装着なら defaultBone を更新</summary>
    public void SetTargetBone(HumanBodyBones bone)
    {
        Initialize();
        if (_syncPlayerId < 0 || _localPlayer == null)
        {
            defaultBone = bone;
            return;
        }
        VRCPlayerApi player = GetTargetPlayer();
        Networking.SetOwner(_localPlayer, gameObject);
        _syncBone = (byte)(int)bone;
        if (player != null) { CaptureOffsets(player, bone); }
        ApplyState();
        RequestSerialization();
    }

    /// <summary>requestedBone でボーンを変更する(SendCustomEvent 可)</summary>
    public void SetBoneRequested()
    {
        SetTargetBone(requestedBone);
    }

    /// <summary>固定オフセットを設定する。装着中なら同期し直す</summary>
    public void SetOffset(Vector3 position, Vector3 eulerRotation)
    {
        Initialize();
        positionOffset = position;
        rotationOffset = eulerRotation;
        if (_syncPlayerId < 0 || _localPlayer == null) { return; }
        Networking.SetOwner(_localPlayer, gameObject);
        _syncPosOffset = position;
        _syncRotOffset = Quaternion.Euler(eulerRotation);
        RequestSerialization();
    }

    /// <summary>ネットワーク経由で装着を依頼する入口(オーナー宛てに送ると先着順が保証される)</summary>
    [NetworkCallable]
    public void RequestAttach(int playerId, int bone, int attachChannel)
    {
        AttachToBoneOnChannel(playerId, (HumanBodyBones)bone, attachChannel);
    }

    /// <summary>ネットワーク経由で解除を依頼する入口</summary>
    [NetworkCallable]
    public void RequestDetach()
    {
        Detach();
    }

    // ------------------------------------------------------------------ 状態問い合わせ

    public bool IsAttached()
    {
        return targetPlayerId >= 0 && GetTargetPlayer() != null;
    }

    public bool IsAttachedTo(int playerId)
    {
        return playerId >= 0 && targetPlayerId == playerId;
    }

    /// <summary>装着中で、かつ装着元が指定 channel か(channel が CHANNEL_ANY なら装着中なら true)</summary>
    public bool IsAttachedOnChannel(int filterChannel)
    {
        return targetPlayerId >= 0 && (filterChannel == CHANNEL_ANY || channel == filterChannel);
    }

    public bool IsLocalPlayerAttached()
    {
        Initialize();
        return _localPlayer != null && targetPlayerId >= 0 && targetPlayerId == _localPlayer.playerId;
    }

    /// <summary>装着中のプレイヤー(未装着・退室済みなら null)</summary>
    public VRCPlayerApi GetTargetPlayer()
    {
        if (targetPlayerId < 0) { return null; }
        if (_playerCache != null && _playerCache.IsValid() && _playerCache.playerId == targetPlayerId)
        {
            return _playerCache;
        }
        _playerCache = VRCPlayerApi.GetPlayerById(targetPlayerId);
        if (_playerCache == null || !_playerCache.IsValid()) { _playerCache = null; }
        return _playerCache;
    }

    // ------------------------------------------------------------------ 同期

    public override void OnDeserialization()
    {
        Initialize();
        ApplyState();
    }

    /// <summary>
    /// 同期フィールドからミラー・追従・イベントを導出する。ローカル書き込み後と逆シリアライズ後の両方から呼ぶ。
    /// playerId か channel の遷移でイベントを発火する(ボーン/オフセットの変更はイベント無しで次フレームの追従に反映)。
    /// 同じプレイヤーのまま channel だけ変わったときも、解除 → 装着の順に送り、channel で絞る受け手が状態を合わせ直せるようにする。
    /// </summary>
    private void ApplyState()
    {
        targetPlayerId = _syncPlayerId;
        targetBone = (HumanBodyBones)(int)_syncBone;
        channel = _syncChannel;
        if (targetPlayerId == _appliedPlayerId && (targetPlayerId < 0 || channel == _appliedChannel)) { return; }

        int previous = _appliedPlayerId;
        _appliedPlayerId = targetPlayerId;
        _appliedChannel = channel;
        _playerCache = null;

        if (previous >= 0) { DispatchDetach(); }
        if (targetPlayerId >= 0)
        {
            VRCPlayerApi player = GetTargetPlayer();
            if (player != null) { Follow(player); } // このフレームで姿勢を合わせ、1 フレームの飛びを防ぐ
            DispatchAttach();
        }
        else if (resetOnDetach)
        {
            ResetTransform();
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null || player.playerId != _syncPlayerId) { return; }
        // 全クライアントでローカル表示を先に解除する。非オーナーの書き込みは次の受信で同じ -1 に上書きされるので無害
        _syncPlayerId = NO_PLAYER;
        ApplyState();
        if (Networking.IsOwner(gameObject)) { RequestSerialization(); }
        // オーナー自身が退室した場合は OnOwnershipTransferred で新オーナーが再送する
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) { return; }
        // 新オーナーになった: 対象が既にいなければ掃除し、いずれにせよ自分の見ている状態を再送して収束させる
        if (_syncPlayerId >= 0)
        {
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(_syncPlayerId);
            if (target == null || !target.IsValid())
            {
                _syncPlayerId = NO_PLAYER;
                ApplyState();
            }
        }
        RequestSerialization();
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!detachOnRespawn || player == null) { return; }
        // 本人のクライアントだけが書く(OnPlayerRespawn の発火範囲に依存しない)
        if (!player.isLocal || player.playerId != _syncPlayerId) { return; }
        Detach();
    }

    // ------------------------------------------------------------------ 追従

    public override void PostLateUpdate()
    {
        if (targetPlayerId < 0) { return; }
        VRCPlayerApi player = GetTargetPlayer();
        if (player == null)
        {
            // 対象が解決できない: オーナーなら掃除して同期。非オーナーは遅入室直後などで解決前の可能性があるので待つ
            if (Networking.IsOwner(gameObject) && _syncPlayerId >= 0)
            {
                _syncPlayerId = NO_PLAYER;
                ApplyState();
                RequestSerialization();
            }
            return;
        }
        Follow(player);
    }

    private void Follow(VRCPlayerApi player)
    {
        if (!followPosition && !followRotation && !followScale) { return; }
        HumanBodyBones bone = (HumanBodyBones)(int)_syncBone;
        Vector3 bonePosition;
        Quaternion boneRotation;
        if (!TryGetBonePose(player, bone, out bonePosition, out boneRotation)) { return; } // ボーン無し/未ロード: 前フレーム維持

        if (followPosition) { transform.position = bonePosition + boneRotation * _syncPosOffset; }
        if (followRotation) { transform.rotation = boneRotation * _syncRotOffset; }
        if (followScale)
        {
            float heightScale = GetAvatarHeightScale(player, referenceEyeHeight);
            transform.localScale = Vector3.Scale(Vector3.one * heightScale, scaleMultiplier);
        }
    }

    private void CaptureOffsets(VRCPlayerApi player, HumanBodyBones bone)
    {
        Vector3 bonePosition;
        Quaternion boneRotation;
        if (captureOffsetOnAttach && TryGetBonePose(player, bone, out bonePosition, out boneRotation))
        {
            _syncPosOffset = Quaternion.Inverse(boneRotation) * (transform.position - bonePosition);
            _syncRotOffset = Quaternion.Inverse(boneRotation) * transform.rotation;
        }
        else
        {
            _syncPosOffset = positionOffset;
            _syncRotOffset = Quaternion.Euler(rotationOffset);
        }
    }

    private void ResetTransform()
    {
        transform.localPosition = _initialLocalPosition;
        transform.localRotation = _initialLocalRotation;
        transform.localScale = _initialLocalScale;
    }

    // ------------------------------------------------------------------ イベント発火

    private void DispatchAttach()
    {
        SendToReceivers(attachEventName);
        if (notifySiblings) { SendToSiblings(EVENT_ATTACH); }
    }

    private void DispatchDetach()
    {
        SendToReceivers(detachEventName);
        if (notifySiblings) { SendToSiblings(EVENT_DETACH); }
    }

    private void SendToReceivers(string eventName)
    {
        if (receivers == null || eventName == null || eventName.Length == 0) { return; }
        bool networked = networkEventTarget != NetworkEventTarget.Self;
        // ApplyState は全クライアントで走る。ネットワーク送信はオーナーだけが行い二重配信を防ぐ
        if (networked && !Networking.IsOwner(gameObject)) { return; }
        for (int i = 0; i < receivers.Length; i++)
        {
            UdonBehaviour receiver = receivers[i];
            if (receiver == null) { continue; }
            if (networked) { receiver.SendCustomNetworkEvent(networkEventTarget, eventName); }
            else { receiver.SendCustomEvent(eventName); }
        }
    }

    private void SendToSiblings(string eventName)
    {
        for (int i = 0; i < _siblings.Length; i++)
        {
            if (_siblings[i] == null) { continue; }
            _siblings[i].SendCustomEvent(eventName);
        }
    }

    // ------------------------------------------------------------------ 共通ヘルパ(追従器からも使う)

    /// <summary>ボーンの位置・回転を取得する。アバターに無いボーン/未ロードなら false</summary>
    public static bool TryGetBonePose(VRCPlayerApi player, HumanBodyBones bone, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (player == null || !player.IsValid()) { return false; }
        position = player.GetBonePosition(bone);
        if (position == Vector3.zero) { return false; }
        rotation = player.GetBoneRotation(bone);
        return true;
    }

    /// <summary>
    /// Unity Humanoid の階層で bone の一つ上のボーンを返す(Hips の親は Hips。LastBone や範囲外も Hips)。
    /// アバターが省略できるボーン(UpperChest・Chest・Neck・Shoulder・指・つま先・目・顎)が無いときに、
    /// 親をたどって代わりのボーンを探すために使う。例: Neck → UpperChest → Chest → Spine → Hips。
    /// </summary>
    public static HumanBodyBones ParentBone(HumanBodyBones bone)
    {
        // enum 配列は Udon で作れないので switch で持つ。enum のまま switch せず int に直して比べる
        switch ((int)bone)
        {
            case (int)HumanBodyBones.Spine: return HumanBodyBones.Hips;
            case (int)HumanBodyBones.Chest: return HumanBodyBones.Spine;
            case (int)HumanBodyBones.UpperChest: return HumanBodyBones.Chest;
            case (int)HumanBodyBones.Neck: return HumanBodyBones.UpperChest;
            case (int)HumanBodyBones.Head: return HumanBodyBones.Neck;
            case (int)HumanBodyBones.LeftEye: return HumanBodyBones.Head;
            case (int)HumanBodyBones.RightEye: return HumanBodyBones.Head;
            case (int)HumanBodyBones.Jaw: return HumanBodyBones.Head;

            case (int)HumanBodyBones.LeftShoulder: return HumanBodyBones.UpperChest;
            case (int)HumanBodyBones.LeftUpperArm: return HumanBodyBones.LeftShoulder;
            case (int)HumanBodyBones.LeftLowerArm: return HumanBodyBones.LeftUpperArm;
            case (int)HumanBodyBones.LeftHand: return HumanBodyBones.LeftLowerArm;
            case (int)HumanBodyBones.RightShoulder: return HumanBodyBones.UpperChest;
            case (int)HumanBodyBones.RightUpperArm: return HumanBodyBones.RightShoulder;
            case (int)HumanBodyBones.RightLowerArm: return HumanBodyBones.RightUpperArm;
            case (int)HumanBodyBones.RightHand: return HumanBodyBones.RightLowerArm;

            case (int)HumanBodyBones.LeftUpperLeg: return HumanBodyBones.Hips;
            case (int)HumanBodyBones.LeftLowerLeg: return HumanBodyBones.LeftUpperLeg;
            case (int)HumanBodyBones.LeftFoot: return HumanBodyBones.LeftLowerLeg;
            case (int)HumanBodyBones.LeftToes: return HumanBodyBones.LeftFoot;
            case (int)HumanBodyBones.RightUpperLeg: return HumanBodyBones.Hips;
            case (int)HumanBodyBones.RightLowerLeg: return HumanBodyBones.RightUpperLeg;
            case (int)HumanBodyBones.RightFoot: return HumanBodyBones.RightLowerLeg;
            case (int)HumanBodyBones.RightToes: return HumanBodyBones.RightFoot;

            case (int)HumanBodyBones.LeftThumbProximal: return HumanBodyBones.LeftHand;
            case (int)HumanBodyBones.LeftThumbIntermediate: return HumanBodyBones.LeftThumbProximal;
            case (int)HumanBodyBones.LeftThumbDistal: return HumanBodyBones.LeftThumbIntermediate;
            case (int)HumanBodyBones.LeftIndexProximal: return HumanBodyBones.LeftHand;
            case (int)HumanBodyBones.LeftIndexIntermediate: return HumanBodyBones.LeftIndexProximal;
            case (int)HumanBodyBones.LeftIndexDistal: return HumanBodyBones.LeftIndexIntermediate;
            case (int)HumanBodyBones.LeftMiddleProximal: return HumanBodyBones.LeftHand;
            case (int)HumanBodyBones.LeftMiddleIntermediate: return HumanBodyBones.LeftMiddleProximal;
            case (int)HumanBodyBones.LeftMiddleDistal: return HumanBodyBones.LeftMiddleIntermediate;
            case (int)HumanBodyBones.LeftRingProximal: return HumanBodyBones.LeftHand;
            case (int)HumanBodyBones.LeftRingIntermediate: return HumanBodyBones.LeftRingProximal;
            case (int)HumanBodyBones.LeftRingDistal: return HumanBodyBones.LeftRingIntermediate;
            case (int)HumanBodyBones.LeftLittleProximal: return HumanBodyBones.LeftHand;
            case (int)HumanBodyBones.LeftLittleIntermediate: return HumanBodyBones.LeftLittleProximal;
            case (int)HumanBodyBones.LeftLittleDistal: return HumanBodyBones.LeftLittleIntermediate;

            case (int)HumanBodyBones.RightThumbProximal: return HumanBodyBones.RightHand;
            case (int)HumanBodyBones.RightThumbIntermediate: return HumanBodyBones.RightThumbProximal;
            case (int)HumanBodyBones.RightThumbDistal: return HumanBodyBones.RightThumbIntermediate;
            case (int)HumanBodyBones.RightIndexProximal: return HumanBodyBones.RightHand;
            case (int)HumanBodyBones.RightIndexIntermediate: return HumanBodyBones.RightIndexProximal;
            case (int)HumanBodyBones.RightIndexDistal: return HumanBodyBones.RightIndexIntermediate;
            case (int)HumanBodyBones.RightMiddleProximal: return HumanBodyBones.RightHand;
            case (int)HumanBodyBones.RightMiddleIntermediate: return HumanBodyBones.RightMiddleProximal;
            case (int)HumanBodyBones.RightMiddleDistal: return HumanBodyBones.RightMiddleIntermediate;
            case (int)HumanBodyBones.RightRingProximal: return HumanBodyBones.RightHand;
            case (int)HumanBodyBones.RightRingIntermediate: return HumanBodyBones.RightRingProximal;
            case (int)HumanBodyBones.RightRingDistal: return HumanBodyBones.RightRingIntermediate;
            case (int)HumanBodyBones.RightLittleProximal: return HumanBodyBones.RightHand;
            case (int)HumanBodyBones.RightLittleIntermediate: return HumanBodyBones.RightLittleProximal;
            case (int)HumanBodyBones.RightLittleDistal: return HumanBodyBones.RightLittleIntermediate;

            // Hips(終点)、LastBone、範囲外
            default: return HumanBodyBones.Hips;
        }
    }

    /// <summary>
    /// bone の位置・回転を取得する。アバターに無ければ ParentBone で親へさかのぼり、最初に取れたボーンを resolvedBone に返す。
    /// Hips でも取れない(プレイヤーが無効、アバターの読み込み中など)ときは false を返す。このとき resolvedBone は bone のままで、
    /// position / rotation は TryGetBonePose が失敗したときと同じ値(Vector3.zero / Quaternion.identity)になる。
    /// ・オフセットは resolvedBone の座標系を基準に掛かる。オフセットを記録する側も掛ける側も、bone ではなく resolvedBone を使うこと。
    /// ・どのボーンに解決されるかはクライアントごとに違うことがある(アバターの非表示や読み込みの差など)。
    ///   全員でそろえたいときは、装着する側で解決したボーンを同期すること。
    /// ・bone が LastBone や範囲外の値なら、GetBonePosition に渡さず Hips から取る(失敗時の resolvedBone は bone のまま)。
    /// </summary>
    public static bool TryGetBonePoseOrParent(VRCPlayerApi player, HumanBodyBones bone, out Vector3 position, out Quaternion rotation, out HumanBodyBones resolvedBone)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        resolvedBone = bone;
        if (player == null || !player.IsValid()) { return false; }
        // LastBone や範囲外の値は GetBonePosition で例外になりうるので、Hips から始める
        int index = (int)bone;
        HumanBodyBones current = index >= 0 && index < (int)HumanBodyBones.LastBone ? bone : HumanBodyBones.Hips;
        // いちばん深い指先でも Hips まで 11 個なので、12 回で必ず止まる
        for (int i = 0; i < 12; i++)
        {
            if (TryGetBonePose(player, current, out position, out rotation))
            {
                resolvedBone = current;
                return true;
            }
            if (current == HumanBodyBones.Hips) { break; }
            current = ParentBone(current);
        }
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    /// <summary>アバターの目線の高さを基準にした倍率(referenceEyeHeight のとき 1)</summary>
    public static float GetAvatarHeightScale(VRCPlayerApi player, float referenceEyeHeight)
    {
        if (player == null || !player.IsValid()) { return 1f; }
        float reference = Mathf.Max(referenceEyeHeight, 0.01f);
        return player.GetAvatarEyeHeightAsMeters() / reference;
    }

    /// <summary>主要 19 ボーンのうち point に最も近いボーンを返す(アバターに無いボーンは無視。見つからなければ Hips)</summary>
    public static HumanBodyBones FindClosestBone(VRCPlayerApi player, Vector3 point)
    {
        // enum 配列の生成(new HumanBodyBones[])は Udon に公開されていないため int で持つ
        int[] candidates = new int[]
        {
            (int)HumanBodyBones.Hips, (int)HumanBodyBones.Spine, (int)HumanBodyBones.Chest, (int)HumanBodyBones.Neck, (int)HumanBodyBones.Head,
            (int)HumanBodyBones.LeftShoulder, (int)HumanBodyBones.LeftUpperArm, (int)HumanBodyBones.LeftLowerArm, (int)HumanBodyBones.LeftHand,
            (int)HumanBodyBones.RightShoulder, (int)HumanBodyBones.RightUpperArm, (int)HumanBodyBones.RightLowerArm, (int)HumanBodyBones.RightHand,
            (int)HumanBodyBones.LeftUpperLeg, (int)HumanBodyBones.LeftLowerLeg, (int)HumanBodyBones.LeftFoot,
            (int)HumanBodyBones.RightUpperLeg, (int)HumanBodyBones.RightLowerLeg, (int)HumanBodyBones.RightFoot,
        };
        HumanBodyBones best = HumanBodyBones.Hips;
        float bestSqr = float.MaxValue;
        if (player == null || !player.IsValid()) { return best; }
        for (int i = 0; i < candidates.Length; i++)
        {
            HumanBodyBones bone = (HumanBodyBones)candidates[i];
            Vector3 bonePosition = player.GetBonePosition(bone);
            if (bonePosition == Vector3.zero) { continue; }
            float sqr = Vector3.SqrMagnitude(bonePosition - point);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = bone;
            }
        }
        return best;
    }
}
