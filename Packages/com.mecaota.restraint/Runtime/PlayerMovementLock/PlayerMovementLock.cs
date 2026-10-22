using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// ローカルプレイヤーの移動速度・ジャンプ・重力を、複数のギミックから重ねて絞るための部品。置いた場所で役割が 2 つに分かれる。
/// ・まとめ役(1ワールドに1つ): 名前が PlayerMovementLock のもの。Resolve で名前から探し、無ければ Prefab から作る。
///   各ギミックは自分を holder として Hold / Release するだけで、元の値の保存と復元はまとめ役が受け持つ。
///   ・最初の Hold で、その時点の歩く/走る/横移動/ジャンプ/重力を「元の値」として保存する。
///   ・複数のギミックが同時に掛けたときは、項目ごとに一番強い制限(最小値)を使う。掛けた順に依存しない。
///   ・最後の Release で元の値に戻す。ただし、まとめ役が入れた値のままの項目だけ戻す(別のシステムが途中で変えた値は残す)。
///     ReleaseAndRestore は無条件で戻す(移動の制御をほかの仕組みに引き渡すときなど)。
///   ・ローカルプレイヤーがリスポーンしたら、全部の制限を外して元に戻す。
/// ・窓口(それ以外の場所に置いたもの): 速度のイベントを受け、自分を holder としてまとめ役に Hold する。
///   窓口の Hold / Release なども、そのまままとめ役に渡す。
/// 速度のイベント(どれも引数なしなので SendCustomEvent で呼べる。値はインスペクタで、またはスクリプトから先に入れておく):
/// ・SetSpeed: setRatio にする / AddSpeed・SubtractSpeed: stepRatio だけ増やす・減らす / ResetSpeed: defaultRatio に戻す。
///   倍率は「元の速度の何倍か」。1 = 元の速度、0.5 = 半分、0 = 移動できない。1 のときは Release する。
/// ・増減は範囲(minRatio〜maxRatio)の外に出た分も覚えておき、掛けるときだけ範囲に収める。
///   SubtractSpeed と AddSpeed を対で呼べば、何回重なっても必ず元に戻る(対で送る送り手がいくつあっても、窓口は 1 つで済む)。
/// ・core を設定して Core の receivers に入れると、Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する(他人の装着では動かない)。
/// ・リスポーンでは倍率を変えない(まとめ役がリスポーンで外した分は、次のフレームで掛け直す)。
/// 速度はローカル専用なので同期しない。送り手は自分のクライアントで、自分に関係するときだけ送ること。
/// 以前は各ギミックが自前で保存・復元していたため、別のギミックが既に絞った値を「元の値」として保存して、
/// 解除後も減速が残ることがあった(2026-10-07 に統合)。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerMovementLock : UdonSharpBehaviour
{
    public const string ObjectName = "PlayerMovementLock";
    // Hold の項目をそのままにする値。
    public const float Free = -1f;
    public const int MaxHolders = 16;

    [Header("まとめ役の設定(窓口では使わない)")]
    [Tooltip("元の重力がほぼ0(飛行中など)だったときに、解除で戻す重力")]
    public float fallbackGravity = 1f;

    [Header("速度の倍率(1 = 元の速度、0.5 = 半分、0 = 移動できない)")]
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

    [Header("窓口として置くとき")]
    [Tooltip("シーンにまとめ役が無いときに作る Prefab(PlayerMovementLock.prefab)。まとめ役がシーンにあれば空でよい")]
    public GameObject movementLockPrefab;

    [Header("RestraintCore(任意)")]
    [Tooltip("設定すると、この Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する。Core の receivers にこの部品を入れる")]
    public RestraintCore core;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する")]
    public int channel = RestraintCore.CHANNEL_ANY;

    private UdonSharpBehaviour[] holders = new UdonSharpBehaviour[MaxHolders];
    private float[] moves = new float[MaxHolders], jumps = new float[MaxHolders], gravities = new float[MaxHolders];
    private int count;
    private VRCPlayerApi local;
    private float baseWalk, baseRun, baseStrafe, baseJump, baseGravity;
    private float appliedWalk, appliedRun, appliedStrafe, appliedJump, appliedGravity;
    private bool moveSet, jumpSet, gravitySet;
    private PlayerMovementLock hub;
    private float ratio;
    private bool started;
    private bool coreHeld;
    private bool retryPending;

    // シーン内のまとめ役を名前で探し、無ければ prefab から作る(ギミックの Prefab からシーンを参照しなくて済む)。
    public static PlayerMovementLock Resolve(PlayerMovementLock cached, GameObject prefab)
    {
        if (cached != null) return cached;
        GameObject found = GameObject.Find(ObjectName);
        if (found == null && prefab != null)
        {
            found = Instantiate(prefab);
            found.name = ObjectName;
        }
        return found != null ? found.GetComponent<PlayerMovementLock>() : null;
    }

    // 窓口は最初の呼び出しでまとめ役を作るので、Prefab を配線していない別のギミックも名前で見つけられる
    private void OnEnable()
    {
        if (!started) { started = true; ratio = defaultRatio; }
        ApplySpeed();
        ResyncCore();
    }

    // 無効の間はイベントが届かないので、掛けている分を外しておく(倍率は覚えておき、有効に戻ったら掛け直す)
    private void OnDisable()
    {
        retryPending = false;
        if (hub != null) hub.Release(this);
    }

    // holder がローカルプレイヤーを絞る。move: 歩く/走る/横移動を元の何倍にするか、jump: ジャンプを元の何倍にするか、
    // gravity: 重力の値そのもの。Free はその項目に触らない。同じ値で毎フレーム呼んでもよい(別のシステムに上書きされていたら掛け直す)。
    public void Hold(UdonSharpBehaviour holder, float move, float jump, float gravity)
    {
        if (!IsHub()) { if (hub != null) hub.Hold(holder, move, jump, gravity); return; }
        if (holder == null || !Ready()) return;
        int i = IndexOf(holder);
        if (i < 0)
        {
            if (count >= MaxHolders) return;
            if (count == 0) SaveBase();
            i = count++;
            holders[i] = holder;
        }
        else if (moves[i] == move && jumps[i] == jump && gravities[i] == gravity && !Overwritten()) return;
        moves[i] = move; jumps[i] = jump; gravities[i] = gravity;
        Apply(false);
    }

    // holder の制限を外す。最後の一つなら、まとめ役が入れた値のままの項目だけ元に戻す。
    public void Release(UdonSharpBehaviour holder)
    {
        if (IsHub()) Remove(holder, false);
        else if (hub != null) hub.Release(holder);
    }

    // holder の制限を外し、最後の一つなら別のシステムが変えた値も含めて元に戻す(元の重力がほぼ0なら fallbackGravity)。
    public void ReleaseAndRestore(UdonSharpBehaviour holder)
    {
        if (IsHub()) Remove(holder, true);
        else if (hub != null) hub.ReleaseAndRestore(holder);
    }

    public bool Holds(UdonSharpBehaviour holder)
    {
        if (!IsHub()) return hub != null && hub.Holds(holder);
        return IndexOf(holder) >= 0;
    }

    // 制限を掛ける前の、プレイヤー本来の値(誰も掛けていなければ今の値)。
    public float BaseRun()
    {
        if (!IsHub() && hub != null) return hub.BaseRun();
        return count > 0 ? baseRun : (Ready() ? local.GetRunSpeed() : 0f);
    }

    public float BaseWalk()
    {
        if (!IsHub() && hub != null) return hub.BaseWalk();
        return count > 0 ? baseWalk : (Ready() ? local.GetWalkSpeed() : 0f);
    }

    // 元の重力(ほぼ0、つまり飛行中に掛けられていたなら fallbackGravity)。
    public float BaseGravity()
    {
        if (!IsHub() && hub != null) return hub.BaseGravity();
        float g = count > 0 ? baseGravity : (Ready() ? local.GetGravityStrength() : 1f);
        return g < .01f ? fallbackGravity : g;
    }

    // setRatio にする(SendCustomEvent 可)
    public void SetSpeed() { ratio = setRatio; ApplySpeed(); }

    // stepRatio だけ増やす(SendCustomEvent 可)
    public void AddSpeed() { ratio += stepRatio; ApplySpeed(); }

    // stepRatio だけ減らす(SendCustomEvent 可)
    public void SubtractSpeed() { ratio -= stepRatio; ApplySpeed(); }

    // defaultRatio に戻す(SendCustomEvent 可)。増減で覚えていた分も捨てる
    public void ResetSpeed() { ratio = defaultRatio; ApplySpeed(); }

    // いま掛けている倍率(範囲に収めた後の値)
    public float GetRatio() { return Mathf.Clamp(ratio, minRatio, maxRatio); }

    public void OnBoneAttach() { ResyncCore(); }

    public void OnBoneDetach() { ResyncCore(); }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        // まとめ役は全部外す(holder がいるのはまとめ役だけ)
        if (count > 0)
        {
            for (int i = 0; i < count; i++) holders[i] = null;
            count = 0;
            Apply(false);
        }
        // どの OnPlayerRespawn が先に走るか決まらないので、自分の倍率は次のフレームで掛け直す
        SendCustomEventDelayedFrames(nameof(_ReapplySpeed), 1);
    }

    public void _ReapplySpeed() { ApplySpeed(); }

    // まとめ役なら true。窓口なら hub にまとめ役が入る(見つからず Prefab も無ければ null)
    private bool IsHub()
    {
        hub = Resolve(hub, movementLockPrefab);
        return hub == this;
    }

    private void ApplySpeed()
    {
        float applied = GetRatio();
        if (Mathf.Abs(applied - 1f) < .0001f) { Release(this); return; }
        Hold(this, Mathf.Max(applied, 0f), Free, Free);
        // まとめ役がまだ無い(ほかのギミックがあとで作る)ときは、見つかるまで 1 秒ごとに掛け直す
        if (hub == null && !retryPending)
        {
            retryPending = true;
            SendCustomEventDelayedSeconds(nameof(_RetrySpeed), 1f);
        }
    }

    public void _RetrySpeed()
    {
        retryPending = false;
        if (gameObject.activeInHierarchy) ApplySpeed();
    }

    // Core に自分が装着されたら SetSpeed、外れたら ResetSpeed(変わったときだけ)
    private void ResyncCore()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        bool held = core != null && Utilities.IsValid(player) && core.IsAttachedOnChannel(channel) && core.targetPlayerId == player.playerId;
        if (held == coreHeld) return;
        coreHeld = held;
        if (held) SetSpeed();
        else ResetSpeed();
    }

    private bool Ready()
    {
        if (!Utilities.IsValid(local)) local = Networking.LocalPlayer;
        return Utilities.IsValid(local);
    }

    private int IndexOf(UdonSharpBehaviour holder)
    {
        for (int i = 0; i < count; i++) if (holders[i] == holder) return i;
        return -1;
    }

    private void Remove(UdonSharpBehaviour holder, bool force)
    {
        int i = IndexOf(holder);
        if (i < 0 || !Ready()) return;
        count--;
        holders[i] = holders[count]; moves[i] = moves[count]; jumps[i] = jumps[count]; gravities[i] = gravities[count];
        holders[count] = null;
        Apply(force);
    }

    private void SaveBase()
    {
        baseWalk = local.GetWalkSpeed(); baseRun = local.GetRunSpeed(); baseStrafe = local.GetStrafeSpeed();
        baseJump = local.GetJumpImpulse(); baseGravity = local.GetGravityStrength();
        moveSet = false; jumpSet = false; gravitySet = false;
    }

    private static bool Same(float current, float applied) { return Mathf.Abs(current - applied) < .001f; }

    private bool Overwritten()
    {
        return (moveSet && (!Same(local.GetWalkSpeed(), appliedWalk) || !Same(local.GetRunSpeed(), appliedRun) || !Same(local.GetStrafeSpeed(), appliedStrafe)))
            || (jumpSet && !Same(local.GetJumpImpulse(), appliedJump))
            || (gravitySet && !Same(local.GetGravityStrength(), appliedGravity));
    }

    // Each item takes the strongest hold on it; an item nobody holds any more goes back to the base value (only if it is
    // still what this lock put there, unless force).
    private void Apply(bool force)
    {
        float move = -1f, jump = -1f, gravity = -1f;
        for (int i = 0; i < count; i++)
        {
            if (moves[i] >= 0f) move = move < 0f ? moves[i] : Mathf.Min(move, moves[i]);
            if (jumps[i] >= 0f) jump = jump < 0f ? jumps[i] : Mathf.Min(jump, jumps[i]);
            if (gravities[i] >= 0f) gravity = gravity < 0f ? gravities[i] : Mathf.Min(gravity, gravities[i]);
        }
        if (move >= 0f)
        {
            appliedWalk = baseWalk * move; appliedRun = baseRun * move; appliedStrafe = baseStrafe * move;
            local.SetWalkSpeed(appliedWalk); local.SetRunSpeed(appliedRun); local.SetStrafeSpeed(appliedStrafe);
            moveSet = true;
        }
        else if (moveSet)
        {
            if (force || Same(local.GetWalkSpeed(), appliedWalk)) local.SetWalkSpeed(baseWalk);
            if (force || Same(local.GetRunSpeed(), appliedRun)) local.SetRunSpeed(baseRun);
            if (force || Same(local.GetStrafeSpeed(), appliedStrafe)) local.SetStrafeSpeed(baseStrafe);
            moveSet = false;
        }
        if (jump >= 0f) { appliedJump = baseJump * jump; local.SetJumpImpulse(appliedJump); jumpSet = true; }
        else if (jumpSet)
        {
            if (force || Same(local.GetJumpImpulse(), appliedJump)) local.SetJumpImpulse(baseJump);
            jumpSet = false;
        }
        if (gravity >= 0f) { appliedGravity = gravity; local.SetGravityStrength(appliedGravity); gravitySet = true; }
        else if (gravitySet)
        {
            // Zero gravity at the first hold is a flight; never hand back a permanent hover.
            if (force || Same(local.GetGravityStrength(), appliedGravity)) local.SetGravityStrength(baseGravity < .01f ? fallbackGravity : baseGravity);
            gravitySet = false;
        }
    }
}
