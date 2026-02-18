
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
// VRC Position Constraintと同じ動作をPlayerに対してできるようにしたもの
public class PlayerRestraintConstraint : PlayerBoneConstraint
{
    [Header("軸無視設定")]
    [Tooltip("X軸を無視するか")]
    public bool ignoreX = false;

    [Tooltip("Y軸を無視するか")]
    public bool ignoreY = false;

    [Tooltip("Z軸を無視するか")]
    public bool ignoreZ = false;

    // 初回更新フラグ、プレイヤー追従開始時にオブジェクトをプレイヤー位置に合わせるために使用
    private bool isFirstUpdate = false;

    // ターゲットプレイヤーを設定するpublicメソッド（playerId指定）
    public override bool SetTargetPlayer(int playerId)
    {
        isFirstUpdate = base.SetTargetPlayer(playerId);
        return isFirstUpdate;
    }

    public override void Detach()
    {
        base.Detach();
        isFirstUpdate = false;
    }

    protected virtual void restraintPlayer(VRCPlayerApi targetPlayer)
    {
        // ボーンの位置を取得
        Vector3 bonePosition = targetPlayer.GetBonePosition(targetBone);
        Vector3 restraintPosition = transform.position + positionOffset;
        bool isPlayerGrounded = targetPlayer.IsPlayerGrounded();

        // プレイヤーが地面にいる場合は、拘束位置のYをプレイヤーのY以上にする（地面に埋まらないように）
        restraintPosition.y = isPlayerGrounded ? Mathf.Max(restraintPosition.y, bonePosition.y) : restraintPosition.y;

        Vector3 diff = restraintPosition - bonePosition;
        if (ignoreX)
        {
            diff.x = 0f;
        }
        if (ignoreY)
        {
            diff.y = 0f;
        }
        if (ignoreZ)
        {
            diff.z = 0f;
        }
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 velocity = diff / deltaTime * followStrength;
        targetPlayer.SetVelocity(velocity);
    }

    protected override void UpdateConstraint(VRCPlayerApi targetPlayer)
    {        
        // 初回更新の場合は、オブジェクトをプレイヤー位置に合わせる
        if (isFirstUpdate)
        {
            base.SetConstraint(targetPlayer);
            isFirstUpdate = false;
            return;
        } else
        {
            // 2回目以降は、プレイヤーをオブジェクト位置に拘束
            restraintPlayer(targetPlayer);
        }
    }
}
