using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// ローカルプレイヤーの移動速度(歩く/走る/横移動)を、イベントで設定・増減・リセットする部品。
/// 速度は「元の速度の何倍か」(倍率)で扱う。1 = 元の速度、0.5 = 半分、0 = 移動できない。
/// ・SetSpeed: setRatio にする / AddSpeed・SubtractSpeed: stepRatio だけ増やす・減らす / ResetSpeed: defaultRatio に戻す。
///   どれも引数なしなので SendCustomEvent で呼べる(値はインスペクタで、またはスクリプトから先に入れておく)。
/// ・増減は範囲(minRatio〜maxRatio)の外に出た分も覚えておき、掛けるときだけ範囲に収める。
///   SubtractSpeed と AddSpeed を対で呼べば、何回重なっても必ず元に戻る(蜘蛛の巣を何枚つないでも 1 つで済む)。
/// ・元の値の保存と復元は PlayerMovementLock に任せ、この部品は自分を holder として Hold / Release するだけ。
///   倍率が 1 のときは Release する。ほかのギミックの制限と重なったときは、一番遅いものが使われる。
/// ・core を設定して Core の receivers に入れると、Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する(他人の装着では動かない)。
/// ・リスポーンでは倍率を変えない(PlayerMovementLock がリスポーンで外した分は、次のフレームで掛け直す)。
/// 速度はローカル専用なので同期しない。送り手は自分のクライアントで、自分に関係するときだけ送ること。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerSpeedLimiter : UdonSharpBehaviour
{
    [Header("倍率(1 = 元の速度、0.5 = 半分、0 = 移動できない)")]
    [Tooltip("元の速度。ResetSpeed でこの倍率に戻す。最初もこの倍率から始まる")]
    public float defaultRatio = 1f;

    [Tooltip("SetSpeed でこの倍率にする")]
    public float setRatio = .5f;

    [Tooltip("AddSpeed / SubtractSpeed で増やす・減らす量")]
    public float stepRatio = .25f;

    [Tooltip("掛ける倍率の下限。増減で範囲の外に出た分は覚えておき、掛けるときだけこの範囲に収める")]
    public float minRatio = 0f;

    [Tooltip("掛ける倍率の上限")]
    public float maxRatio = 1f;

    [Header("PlayerMovementLock")]
    [Tooltip("シーンに PlayerMovementLock が無いときに作る Prefab(PlayerMovementLock.prefab)。シーンに置いてあれば空でよい")]
    public GameObject movementLockPrefab;

    [Header("RestraintCore(任意)")]
    [Tooltip("設定すると、この Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する。Core の receivers にこの部品を入れる")]
    public RestraintCore core;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する")]
    public int channel = RestraintCore.CHANNEL_ANY;

    private PlayerMovementLock movementLock;
    private float ratio;
    private bool started;
    private bool coreHeld;

    // 最初の呼び出しでロックを作るので、Prefab を配線していない別のギミックも名前で見つけられる
    private void OnEnable()
    {
        if (!started) { started = true; ratio = defaultRatio; }
        Apply();
        ResyncCore();
    }

    // 無効の間はイベントが届かないので、掛けている分を外しておく(倍率は覚えておき、有効に戻ったら掛け直す)
    private void OnDisable()
    {
        if (movementLock != null) movementLock.Release(this);
    }

    // setRatio にする(SendCustomEvent 可)
    public void SetSpeed() { ratio = setRatio; Apply(); }

    // stepRatio だけ増やす(SendCustomEvent 可)
    public void AddSpeed() { ratio += stepRatio; Apply(); }

    // stepRatio だけ減らす(SendCustomEvent 可)
    public void SubtractSpeed() { ratio -= stepRatio; Apply(); }

    // defaultRatio に戻す(SendCustomEvent 可)。増減で覚えていた分も捨てる
    public void ResetSpeed() { ratio = defaultRatio; Apply(); }

    // いま掛けている倍率(範囲に収めた後の値)
    public float GetRatio() { return Mathf.Clamp(ratio, minRatio, maxRatio); }

    public void OnBoneAttach() { ResyncCore(); }

    public void OnBoneDetach() { ResyncCore(); }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        // PlayerMovementLock はリスポーンで全部外す。どちらが先に走るか決まらないので、次のフレームで今の倍率を掛け直す
        SendCustomEventDelayedFrames(nameof(_Reapply), 1);
    }

    public void _Reapply() { Apply(); }

    // Core に自分が装着されたら SetSpeed、外れたら ResetSpeed(変わったときだけ)
    private void ResyncCore()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        bool held = core != null && Utilities.IsValid(local) && core.IsAttachedOnChannel(channel) && core.targetPlayerId == local.playerId;
        if (held == coreHeld) return;
        coreHeld = held;
        if (held) SetSpeed();
        else ResetSpeed();
    }

    private void Apply()
    {
        if (Lock() == null) return;
        float applied = GetRatio();
        if (Mathf.Abs(applied - 1f) < .0001f) movementLock.Release(this);
        else movementLock.Hold(this, Mathf.Max(applied, 0f), PlayerMovementLock.Free, PlayerMovementLock.Free);
    }

    private PlayerMovementLock Lock()
    {
        movementLock = PlayerMovementLock.Resolve(movementLock, movementLockPrefab);
        return movementLock;
    }
}
