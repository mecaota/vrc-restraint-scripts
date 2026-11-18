using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// プレイヤーごとの移動デバフの値(0〜255)を持ち、本人の移動速度を絞る部品。PlayerObject のテンプレートに置く。
/// ・値は本人(この PlayerObject の持ち主)の端末だけが書き、Manual 同期で全端末へ送る(0.2 秒分まとめて送る)。
/// ・値が immobileAt より小さい間は「1 − 値 ÷ immobileAt」倍に減速、immobileAt 以上で移動もジャンプもできない。
///   速度は PlayerMovementLock のまとめ役(シーンに 1 つ)に Hold / Release で頼む。値が 0 なら Release。
/// ・もがくと 1 回ごとに recoverAmount だけ値が減る。VR は手の速い反転、デスクトップは移動キーを新しく倒した時。
/// ・本人のリスポーンで 0 に戻る。時間では減らない。
/// ・animator を設定すると、全端末で値が変わるたびに float の Amount に 値 ÷ 255 を入れる。
/// 入口は _Add(int)(負なら減らす。値が変わらなくても速度を掛け直すので、immobileAt を変えた後は _Add(0))と
/// _Reset()(SendCustomEvent 可)だけ。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MovementDebuff : UdonSharpBehaviour
{
    private const string AmountParameter = "Amount";
    private const int MaxAmount = 255;
    private const float SyncDelay = .2f;
    // VR のもがき: 数える手の速さ(m/s、目の高さ 1.6 m 換算)と、同じ手で数える間隔の下限(秒)
    private const float ShakeSpeed = 1.2f;
    private const float MinStrokeGap = .06f;

    [Tooltip("この値以上で移動もジャンプもできない。それより下は値に比例して遅くなる(1 以上に丸める)")]
    public int immobileAt = 32;

    [Tooltip("もがき 1 回で減らす量")]
    public int recoverAmount = 2;

    [Tooltip("値 ÷ 255 を float の Amount に入れる Animator。空なら何もしない")]
    public Animator animator;

    [UdonSynced] private int amount;

    private PlayerMovementLock movementLock;
    private VRCPlayerApi local;
    private float syncAt = -1f;
    private bool tracking;
    private Vector3[] lastHand = new Vector3[2];
    private Vector3[] lastFast = new Vector3[2];
    private float[] lastStrokeAt = new float[2];
    private int[] keySign = new int[2]; // 移動の軸ごとに倒している向き(-1 / 0 / 1)

    // 値を delta だけ増やす(負なら減らす)。本人の端末でだけ効く。
    public void _Add(int delta)
    {
        if (!Mine()) return;
        int next = Mathf.Clamp(amount + Mathf.Clamp(delta, -MaxAmount, MaxAmount), 0, MaxAmount);
        if (next != amount)
        {
            amount = next;
            ApplyAnimator();
            if (syncAt < 0f) syncAt = Time.time + SyncDelay;
        }
        ApplyLock();
    }

    // 値を 0 に戻す(SendCustomEvent 可)
    public void _Reset() { _Add(-MaxAmount); }

    public override void OnDeserialization() { ApplyAnimator(); }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (Utilities.IsValid(player) && player.isLocal) _Reset();
    }

    private void Update()
    {
        if (syncAt >= 0f && Time.time >= syncAt) { syncAt = -1f; RequestSerialization(); }
        // もがきは本人の端末の VR だけで測る(デスクトップは移動キーで数える)
        if (!Mine() || !local.IsUserInVR()) { tracking = false; return; }
        Struggle();
    }

    // 本人の PlayerObject なら true。シーンに残るテンプレート(無効)と、ほかの人の複製では false。
    private bool Mine()
    {
        if (!Utilities.IsValid(local)) local = Networking.LocalPlayer;
        return Utilities.IsValid(local) && gameObject.activeInHierarchy && Networking.IsOwner(gameObject);
    }

    private void ApplyLock()
    {
        movementLock = PlayerMovementLock.Resolve(movementLock, null);
        if (movementLock == null) return;
        if (amount == 0) { movementLock.Release(this); return; }
        int at = Mathf.Max(immobileAt, 1);
        if (amount >= at) movementLock.Hold(this, 0f, 0f, PlayerMovementLock.Free);
        else movementLock.Hold(this, 1f - (float)amount / at, PlayerMovementLock.Free, PlayerMovementLock.Free);
    }

    private void ApplyAnimator()
    {
        if (animator != null) animator.SetFloat(AmountParameter, amount / (float)MaxAmount);
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
        if (!pressed || !Mine() || local.IsUserInVR()) return;
        _Add(-recoverAmount);
    }
}
