using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// ローカルプレイヤーの移動速度・ジャンプ・重力を、複数のギミックから重ねて絞るための共有部品(1ワールドに1つ)。
/// 各ギミックは自分を holder として Hold / Release するだけで、元の値の保存と復元はこの部品が受け持つ。
/// ・最初の Hold で、その時点の歩く/走る/横移動/ジャンプ/重力を「元の値」として保存する。
/// ・複数のギミックが同時に掛けたときは、項目ごとに一番強い制限(最小値)を使う。掛けた順に依存しない。
/// ・最後の Release で元の値に戻す。ただし、この部品が入れた値のままの項目だけ戻す(別のシステムが途中で変えた値は残す)。
///   ReleaseAndRestore は無条件で戻す(空中迎撃の引き渡しなど)。
/// ・ローカルプレイヤーがリスポーンしたら、全部の制限を外して元に戻す。
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

    [Tooltip("元の重力がほぼ0(飛行中など)だったときに、解除で戻す重力")]
    public float fallbackGravity = 1f;

    private UdonSharpBehaviour[] holders = new UdonSharpBehaviour[MaxHolders];
    private float[] moves = new float[MaxHolders], jumps = new float[MaxHolders], gravities = new float[MaxHolders];
    private int count;
    private VRCPlayerApi local;
    private float baseWalk, baseRun, baseStrafe, baseJump, baseGravity;
    private float appliedWalk, appliedRun, appliedStrafe, appliedJump, appliedGravity;
    private bool moveSet, jumpSet, gravitySet;

    // シーン内のロックを名前で探し、無ければ prefab から作る(ギミックの Prefab からシーンを参照しなくて済む)。
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

    // holder がローカルプレイヤーを絞る。move: 歩く/走る/横移動を元の何倍にするか、jump: ジャンプを元の何倍にするか、
    // gravity: 重力の値そのもの。Free はその項目に触らない。同じ値で毎フレーム呼んでもよい(別のシステムに上書きされていたら掛け直す)。
    public void Hold(UdonSharpBehaviour holder, float move, float jump, float gravity)
    {
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

    // holder の制限を外す。最後の一つなら、この部品が入れた値のままの項目だけ元に戻す。
    public void Release(UdonSharpBehaviour holder) { Remove(holder, false); }

    // holder の制限を外し、最後の一つなら別のシステムが変えた値も含めて元に戻す(元の重力がほぼ0なら fallbackGravity)。
    public void ReleaseAndRestore(UdonSharpBehaviour holder) { Remove(holder, true); }

    public bool Holds(UdonSharpBehaviour holder) { return IndexOf(holder) >= 0; }

    // 制限を掛ける前の、プレイヤー本来の値(誰も掛けていなければ今の値)。
    public float BaseRun() { return count > 0 ? baseRun : (Ready() ? local.GetRunSpeed() : 0f); }
    public float BaseWalk() { return count > 0 ? baseWalk : (Ready() ? local.GetWalkSpeed() : 0f); }
    // 元の重力(ほぼ0、つまり飛行中に掛けられていたなら fallbackGravity)。
    public float BaseGravity()
    {
        float g = count > 0 ? baseGravity : (Ready() ? local.GetGravityStrength() : 1f);
        return g < .01f ? fallbackGravity : g;
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal || count == 0) return;
        for (int i = 0; i < count; i++) holders[i] = null;
        count = 0;
        Apply(false);
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
        return (moveSet && !Same(local.GetWalkSpeed(), appliedWalk)) || (jumpSet && !Same(local.GetJumpImpulse(), appliedJump))
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
