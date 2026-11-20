using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// ローカルプレイヤーの移動速度・ジャンプ・重力を複数のギミックから重ねて絞り、動きにくさ(0〜255)も持つ部品。置いた場所で役割が 2 つに分かれる。
/// ・まとめ役: ローカルプレイヤーの PlayerObject の複製(PlayerObject のテンプレートの根に置く)。Resolve でそこから取る。
///   各ギミックは自分を holder として Hold / Release するだけで、元の値の保存と復元はまとめ役が受け持つ。
///   ・最初の Hold で、その時点の歩く/走る/横移動/ジャンプ/重力を「元の値」として保存する。
///   ・複数のギミックが同時に掛けたときは、項目ごとに一番強い制限(最小値)を使う。掛けた順に依存しない。
///   ・最後の Release で元の値に戻す。ただし、まとめ役が入れた値のままの項目だけ戻す(別のシステムが途中で変えた値は残す)。
///     ReleaseAndRestore は無条件で戻す(移動の制御をほかの仕組みに引き渡すときなど)。
///   ・ローカルプレイヤーがリスポーンしたら、全部の制限を外して元に戻し、動きにくさも 0 にする。
/// ・窓口(それ以外の場所に置いたもの): 速度のイベントを受け、自分を holder としてまとめ役に Hold する。
///   窓口の Hold / Release なども、そのまままとめ役に渡す。まとめ役が見つかるまでは 1 秒ごとに掛け直す。
/// 速度のイベント(どれも引数なしなので SendCustomEvent で呼べる。値はインスペクタで、またはスクリプトから先に入れておく):
/// ・SetSpeed: setRatio にする / AddSpeed・SubtractSpeed: stepRatio だけ増やす・減らす / ResetSpeed: defaultRatio に戻す。
///   倍率は「元の速度の何倍か」。1 = 元の速度、0.5 = 半分、0 = 移動できない。1 のときは Release する。
/// ・増減は範囲(minRatio〜maxRatio)の外に出た分も覚えておき、掛けるときだけ範囲に収める。
///   SubtractSpeed と AddSpeed を対で呼べば、何回重なっても必ず元に戻る(対で送る送り手がいくつあっても、窓口は 1 つで済む)。
/// ・core を設定して Core の receivers に入れると、Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する(他人の装着では動かない)。
/// ・リスポーンでは倍率を変えない(まとめ役がリスポーンで外した分は、次のフレームで掛け直す)。
/// 動きにくさ(まとめ役だけが持つ):
/// ・_Add(int) で増減(負なら減らす。値が変わらなくても掛け直すので、immobileAt を変えた後は _Add(0))、_Reset() で 0。
///   値と immobileAt は本人の端末でだけ書き、Manual 同期で全端末へ送る(0.2 秒分まとめて送る)。
/// ・値が immobileAt より小さい間は倍率を「1 − 値 ÷ immobileAt」以下に、immobileAt 以上で移動もジャンプもできない。
/// ・もがくと 1 回ごとに recoverAmount だけ減る。VR は手の速い反転、デスクトップは移動キーを新しく倒した時。時間では減らない。
/// ・animator を設定すると、全端末で値が変わるたびに float の Amount に 値 ÷ 255 を、bool の Immobile に「値 ≥ immobileAt」を入れる。
/// 速度はローカル専用。送り手は自分のクライアントで、自分に関係するときだけ送ること。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PlayerEncumbranceController : UdonSharpBehaviour
{
    // Hold の項目をそのままにする値。
    public const float Free = -1f;
    public const int MaxHolders = 16;
    private const string AmountParameter = "Amount";
    private const string ImmobileParameter = "Immobile";
    private const int MaxAmount = 255;
    private const float SyncDelay = .2f;
    // VR のもがき: 数える手の速さ(m/s、目の高さ 1.6 m 換算)と、同じ手で数える間隔の下限(秒)
    private const float ShakeSpeed = 1.2f;
    private const float MinStrokeGap = .06f;

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

    [Header("RestraintCore(任意)")]
    [Tooltip("設定すると、この Core に自分が装着されたら SetSpeed、外れたら ResetSpeed する。Core の receivers にこの部品を入れる")]
    public RestraintCore core;

    [Tooltip("反応する装着元ID(channel)。-1 ならどの装着元にも反応する")]
    public int channel = RestraintCore.CHANNEL_ANY;

    [Header("動きにくさ(まとめ役だけが使う)")]
    [Tooltip("この値以上で移動もジャンプもできない。それより下は値に比例して遅くなる(1 以上に丸める)")]
    [UdonSynced] public int immobileAt = 32;

    [Tooltip("もがき 1 回で減らす量")]
    public int recoverAmount = 2;

    [Tooltip("値 ÷ 255 を float の Amount に、値 ≥ immobileAt を bool の Immobile に入れる Animator。空なら何もしない")]
    public Animator animator;

    [UdonSynced] private int amount;

    private UdonSharpBehaviour[] holders = new UdonSharpBehaviour[MaxHolders];
    private float[] moves = new float[MaxHolders], jumps = new float[MaxHolders], gravities = new float[MaxHolders];
    private int count;
    private VRCPlayerApi local;
    private float baseWalk, baseRun, baseStrafe, baseJump, baseGravity;
    private float appliedWalk, appliedRun, appliedStrafe, appliedJump, appliedGravity;
    private bool moveSet, jumpSet, gravitySet;
    private PlayerEncumbranceController hub;
    private float ratio;
    private bool started;
    private bool coreHeld;
    private bool retryPending;
    private float syncAt = -1f;
    private bool tracking;
    private Vector3[] lastHand = new Vector3[2];
    private Vector3[] lastFast = new Vector3[2];
    private float[] lastStrokeAt = new float[2];
    private int[] keySign = new int[2]; // 移動の軸ごとに倒している向き(-1 / 0 / 1)

    // ローカルプレイヤーの PlayerObject の複製からまとめ役を取る。まだ複製が無ければ null。
    public static PlayerEncumbranceController Resolve(PlayerEncumbranceController cached)
    {
        if (cached != null) return cached;
        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return null;
        GameObject[] objects = Networking.GetPlayerObjects(player);
        if (objects == null) return null;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null) continue;
            PlayerEncumbranceController found = objects[i].GetComponent<PlayerEncumbranceController>();
            if (found != null) return found;
        }
        return null;
    }

    // 有効になったら、倍率と(本人のまとめ役なら)動きにくさで掛け直す
    private void OnEnable()
    {
        if (!started) { started = true; ratio = defaultRatio; }
        ApplySpeed();
        ResyncCore();
    }

    // 無効の間はイベントが届かないので、自分の分の制限を外しておく(倍率と値は覚えておき、有効に戻ったら掛け直す)
    private void OnDisable()
    {
        retryPending = false;
        tracking = false;
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

    // いま頼まれている移動の倍率(Hold の move のうち Free を除いた最小値)。誰も掛けていなければ 1。
    // 返すのは頼まれた値で、別のシステムが上書きした実際の速度ではない。重力は扱わない。
    public float HeldMove()
    {
        if (!IsHub() && hub != null) return hub.HeldMove();
        return HeldRatio(moves, count);
    }

    // いま頼まれているジャンプの倍率(Hold の jump のうち Free を除いた最小値)。誰も掛けていなければ 1。
    // HeldMove と同じく、頼まれた値であって実際の値ではない。
    public float HeldJump()
    {
        if (!IsHub() && hub != null) return hub.HeldJump();
        return HeldRatio(jumps, count);
    }

    // setRatio にする(SendCustomEvent 可)
    public void SetSpeed() { ratio = setRatio; ApplySpeed(); }

    // stepRatio だけ増やす(SendCustomEvent 可)
    public void AddSpeed() { ratio += stepRatio; ApplySpeed(); }

    // stepRatio だけ減らす(SendCustomEvent 可)
    public void SubtractSpeed() { ratio -= stepRatio; ApplySpeed(); }

    // defaultRatio に戻す(SendCustomEvent 可)。増減で覚えていた分も捨てる
    public void ResetSpeed() { ratio = defaultRatio; ApplySpeed(); }

    // いま掛けている倍率(範囲に収めた後の値。動きにくさは含まない)
    public float GetRatio() { return Mathf.Clamp(ratio, minRatio, maxRatio); }

    public void OnBoneAttach() { ResyncCore(); }

    public void OnBoneDetach() { ResyncCore(); }

    // 動きにくさを delta だけ増やす(負なら減らす)。本人のまとめ役で、有効なときだけ効く。
    public void _Add(int delta)
    {
        if (!IsHub() || !enabled || !gameObject.activeInHierarchy) return;
        int next = Mathf.Clamp(amount + Mathf.Clamp(delta, -MaxAmount, MaxAmount), 0, MaxAmount);
        if (next != amount || delta == 0)
        {
            amount = next;
            ApplyAnimator();
            if (syncAt < 0f) syncAt = Time.time + SyncDelay;
        }
        ApplySpeed();
    }

    // 動きにくさを 0 に戻す(SendCustomEvent 可)
    public void _Reset() { _Add(-MaxAmount); }

    public override void OnDeserialization() { ApplyAnimator(); }

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
        _Reset();
        // どの OnPlayerRespawn が先に走るか決まらないので、自分の倍率は次のフレームで掛け直す
        SendCustomEventDelayedFrames(nameof(_ReapplySpeed), 1);
    }

    public void _ReapplySpeed() { ApplySpeed(); }

    private void Update()
    {
        if (syncAt >= 0f && Time.time >= syncAt) { syncAt = -1f; RequestSerialization(); }
        // もがきは本人のまとめ役の VR だけで測る(デスクトップは移動キーで数える)
        if (!IsHub() || !Ready() || !local.IsUserInVR() || recoverAmount == 0) { tracking = false; return; }
        Struggle();
    }

    // まとめ役なら true。窓口なら hub にまとめ役が入る(まだ見つからなければ null)
    private bool IsHub()
    {
        hub = Resolve(hub);
        return hub == this;
    }

    // 倍率と、まとめ役なら動きにくさから、自分の分の制限を掛ける。無効の間は掛けない(有効に戻ったら OnEnable で掛ける)
    private void ApplySpeed()
    {
        if (!enabled || !gameObject.activeInHierarchy) return;
        float move = GetRatio(), jump = Free;
        int value = IsHub() ? amount : 0;
        int at = Mathf.Max(immobileAt, 1);
        if (value >= at) { move = 0f; jump = 0f; }
        else if (value > 0) move = Mathf.Min(move, 1f - (float)value / at);
        if (jump == Free && Mathf.Abs(move - 1f) < .0001f) { Release(this); return; }
        Hold(this, Mathf.Max(move, 0f), jump, Free);
        // まとめ役(ローカルプレイヤーの PlayerObject の複製)がまだ無いときは、見つかるまで 1 秒ごとに掛け直す
        if (hub == null && !retryPending)
        {
            retryPending = true;
            SendCustomEventDelayedSeconds(nameof(_RetrySpeed), 1f);
        }
    }

    public void _RetrySpeed()
    {
        retryPending = false;
        ApplySpeed();
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

    private void ApplyAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(AmountParameter, amount / (float)MaxAmount);
        animator.SetBool(ImmobileParameter, amount >= Mathf.Max(immobileAt, 1));
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

    // values の先頭 count 件から、Free(負の値)を除いた最小値(一番強い制限)を返す。全部 Free か 0 件なら Free。
    // Apply と HeldMove / HeldJump が同じ計算を使うために切り出した純粋関数。
    public static float StrongestHold(float[] values, int count)
    {
        float result = Free;
        if (values == null) return result;
        int n = Mathf.Min(count, values.Length);
        for (int i = 0; i < n; i++)
        {
            if (values[i] >= 0f) result = result < 0f ? values[i] : Mathf.Min(result, values[i]);
        }
        return result;
    }

    // StrongestHold と同じだが、誰も掛けていない(全部 Free か 0 件)なら 1(元のまま)を返す。
    public static float HeldRatio(float[] values, int count)
    {
        float held = StrongestHold(values, count);
        return held < 0f ? 1f : held;
    }

    private bool Overwritten()
    {
        return (moveSet && (!Same(local.GetWalkSpeed(), appliedWalk) || !Same(local.GetRunSpeed(), appliedRun) || !Same(local.GetStrafeSpeed(), appliedStrafe)))
            || (jumpSet && !Same(local.GetJumpImpulse(), appliedJump))
            || (gravitySet && !Same(local.GetGravityStrength(), appliedGravity));
    }

    // Each item takes the strongest hold on it; an item nobody holds any more goes back to the base value (only if it is
    // still what this controller put there, unless force).
    private void Apply(bool force)
    {
        float move = StrongestHold(moves, count), jump = StrongestHold(jumps, count), gravity = StrongestHold(gravities, count);
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

    // ---- VR: 手の速い反転を 1 回と数える ----
    private void Struggle()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // プレイスペースの座標で測るので、移動・旋回・運ばれることでは数えない
        VRCPlayerApi.TrackingData origin = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
        Quaternion toSpace = Quaternion.Inverse(origin.rotation);
        float scale = 1.6f / Mathf.Max(local.GetAvatarEyeHeightAsMeters(), .1f);
        for (int h = 0; h < 2; h++)
        {
            VRCPlayerApi.TrackingData hand = local.GetTrackingData(h == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
            Vector3 p = toSpace * (hand.position - origin.position) * scale;
            if (!tracking) lastFast[h] = Vector3.zero;
            else
            {
                Vector3 v = (p - lastHand[h]) / dt;
                float speed = v.magnitude;
                if (speed > 20f) lastFast[h] = Vector3.zero; // トラッキングの飛び。振りではない
                else if (speed >= ShakeSpeed)
                {
                    Vector3 dir = v / speed;
                    // 直前の速い動きと逆向きになった最初の速いフレームで数える
                    if (lastFast[h] != Vector3.zero && Vector3.Dot(dir, lastFast[h]) < -.3f && Time.time - lastStrokeAt[h] >= MinStrokeGap)
                    {
                        lastStrokeAt[h] = Time.time;
                        _Add(-recoverAmount);
                    }
                    lastFast[h] = dir;
                }
            }
            lastHand[h] = p;
        }
        tracking = true;
    }

    // ---- デスクトップ: 移動の軸を新しく倒した時を 1 回と数える ----
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { MoveKey(0, value); }
    public override void InputMoveVertical(float value, UdonInputEventArgs args) { MoveKey(1, value); }

    // 0.3〜0.5 の間は前の向きを保つので、半端に倒したスティックでは数え続けない
    private void MoveKey(int axis, float value)
    {
        int sign = value >= .5f ? 1 : value <= -.5f ? -1 : Mathf.Abs(value) < .3f ? 0 : keySign[axis];
        bool pressed = sign != 0 && sign != keySign[axis];
        keySign[axis] = sign;
        if (!pressed || recoverAmount == 0 || !IsHub() || !Ready() || local.IsUserInVR()) return;
        _Add(-recoverAmount);
    }
}
