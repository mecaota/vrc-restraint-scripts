using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 掛けている間、ローカルプレイヤーの移動速度を moveRatio 倍にする部品(0 で移動できない、0.5 で半分の速さ)。
/// 元の値の保存と復元は PlayerMovementLock に任せ、この部品は自分を holder として Hold / Release するだけ。
/// ・Engage / Disengage: SendCustomEvent で呼べる入口(引数なし)。何か所から呼ばれても回数を数え、
///   Engage が Disengage より多い間だけ掛ける(蜘蛛の巣を何枚つないでも 1 つの部品で済む)。
/// ・OnBoneAttach / OnBoneDetach: core を設定して Core の receivers に入れると、Core に装着されているのが自分の間も掛ける
///   (他人の装着では掛けない)。
/// ・ローカルプレイヤーがリスポーンしたら Engage の回数を捨てる(PlayerMovementLock もリスポーンで全部外すため)。
/// 速度はローカル専用なので同期しない。送り手は「自分が」捕まった/放されたときだけ、自分のクライアントで送ること。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerSpeedLimiter : UdonSharpBehaviour
{
    [Header("倍率")]
    [Tooltip("掛けている間、歩く/走る/横移動を元の何倍にするか。0 で移動できない、0.5 で半分の速さ")]
    [Range(0f, 1f)] public float moveRatio = .5f;

    [Tooltip("掛けている間、ジャンプを元の何倍にするか。負の値(-1)ならジャンプには触らない")]
    public float jumpRatio = PlayerMovementLock.Free;

    [Header("PlayerMovementLock")]
    [Tooltip("シーンに PlayerMovementLock が無いときに作る Prefab(PlayerMovementLock.prefab)。シーンに置いてあれば空でよい")]
    public GameObject movementLockPrefab;

    [Header("RestraintCore(任意)")]
    [Tooltip("設定すると、この Core に装着されているのが自分の間も掛ける。Core の receivers にこの部品を入れる")]
    public RestraintCore core;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する")]
    public int channel = RestraintCore.CHANNEL_ANY;

    private PlayerMovementLock movementLock;
    private int engageCount;
    private bool coreHeld;

    // 有効になったら Core の状態を読み直す。最初の呼び出しでロックを作るので、Prefab を配線していない別のギミックも名前で見つけられる
    private void OnEnable() { ResyncCore(); }

    // 掛ける(SendCustomEvent 可)。外すときは Disengage を同じ回数だけ呼ぶ
    public void Engage()
    {
        engageCount++;
        Apply();
    }

    // Engage を 1 回分外す(SendCustomEvent 可)。全部外れて、Core にも装着されていなければ元の速度に戻す
    public void Disengage()
    {
        if (engageCount > 0) engageCount--;
        Apply();
    }

    public void OnBoneAttach() { ResyncCore(); }

    public void OnBoneDetach() { ResyncCore(); }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        engageCount = 0;
        // PlayerMovementLock のリスポーン処理とどちらが先に走るか決まらないので、Core の装着が残っていれば次のフレームで掛け直す
        SendCustomEventDelayedFrames(nameof(_ResyncCore), 1);
    }

    public void _ResyncCore() { ResyncCore(); }

    // 無効の間に届いたイベントは捨てられ、回数が合わなくなるので、無効にされたら外して数え直す
    private void OnDisable()
    {
        engageCount = 0;
        coreHeld = false;
        if (movementLock != null) movementLock.Release(this);
    }

    private void ResyncCore()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        coreHeld = core != null && Utilities.IsValid(local) && core.IsAttachedOnChannel(channel) && core.targetPlayerId == local.playerId;
        Apply();
    }

    private void Apply()
    {
        if (Lock() == null) return;
        if (engageCount > 0 || coreHeld) movementLock.Hold(this, Mathf.Clamp01(moveRatio), jumpRatio, PlayerMovementLock.Free);
        else movementLock.Release(this);
    }

    private PlayerMovementLock Lock()
    {
        movementLock = PlayerMovementLock.Resolve(movementLock, movementLockPrefab);
        return movementLock;
    }
}
