
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// VRC Position Constraintと同じ動作をPlayerに対してできるようにしたもの
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PlayerBoneConstraint : UdonSharpBehaviour
{
    // 高さのスケーリングの基準値（アバターの身長がこの値のとき、scaleOffsetが等倍で適用される）
    private const float DefaultAvatarHeightMeters = 2f;

    [Header("ターゲット設定")]
    [Tooltip("追従するプレイヤーのID")]
    public int targetPlayerId = -1;
    
    [Tooltip("追従するボーン")]
    public HumanBodyBones targetBone = HumanBodyBones.Hips;

    [Header("追従設定")]
    [Tooltip("追従の強さ（0-1）")]
    public float followStrength = 1f;

    [Header("位置追従設定")]
    [Tooltip("位置を追従するか")]
    public bool followPosition = true;

    [Tooltip("位置オフセット")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("回転追従設定")]
    [Tooltip("回転を追従するか")]
    public bool followRotation = false;
    
    [Tooltip("回転オフセット（Euler角）")]
    public Vector3 rotationOffset = Vector3.zero;
    
    [Header("スケール追従設定")]
    [Tooltip("スケールを追従するか")]
    public bool followScale = false;

    [Tooltip("スケールオフセット")]
    public Vector3 scaleOffset = Vector3.zero;

    [Header("設定")]
    [Tooltip("オブジェクト無効化時に初期位置に戻すか")]
    public bool resetPositionOnDisable = true;

    // スポーン時の初期位置を記録
    protected Vector3 initialPosition;
    // ターゲットプレイヤーのキャッシュ
    protected VRCPlayerApi playerCache;

    /* Hooks */
    protected void Start()
    {
        // 初期位置を記録
        initialPosition = transform.position;
    }

    protected void Update()
    {
        VRCPlayerApi player = GetPlayerByPlayerId(targetPlayerId);
        if (player != null)
        {
            UpdateConstraint(player);
        }
    }

    protected void OnDisable()
    {
        if (resetPositionOnDisable)
        {
            Detach();
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (IsTargetPlayer(player) && resetPositionOnDisable)
        {
            Detach();
        }
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (IsTargetPlayer(player) && resetPositionOnDisable)
        {
            Detach();
        }
    }
    /* Hooks */

    // ターゲットプレイヤーを設定するpublicメソッド
    public virtual bool SetTargetPlayer(int playerId)
    {
        VRCPlayerApi player = GetPlayerByPlayerId(playerId);

        if (player != null)
        {
            targetPlayerId = playerId;
            playerCache = player;
            Debug.Log($"[{GetType().Name}] Tracking {player.displayName}'s {targetBone}");
            return true;
        }
        else
        {
            // プレイヤーが見つからない場合は初期位置に戻す
            Detach();
            return false;
        }
    }

    // ターゲットボーンを変更するpublicメソッド
    public void SetTargetBone(HumanBodyBones bone)
    {
        targetBone = bone;
    }

    public virtual void Detach()
    {
        targetPlayerId = -1;
        playerCache = null;
        transform.position = initialPosition;
    }
    public bool IsAttached()
    {
        return GetPlayerByPlayerId(targetPlayerId) != null;
    }

    protected bool IsTargetPlayer(VRCPlayerApi player)
    {
        return player.playerId == targetPlayerId;
    }

    protected VRCPlayerApi GetPlayerByPlayerId(int playerId)
    {
        if (playerId < 0)
        {
            return null;
        }

        if(playerCache != null && playerCache.playerId == playerId)
        {
            return playerCache;
        }

        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);
        
        foreach (VRCPlayerApi player in players)
        {
            if (player != null && player.IsValid() && player.playerId == playerId)
            {
                return player;
            }
        }
        
        return null;
    }

    protected void SetConstraint(VRCPlayerApi targetPlayer)
    {
        // ボーンの位置と回転を取得
        Vector3 bonePosition = targetPlayer.GetBonePosition(targetBone);
        Quaternion boneRotation = targetPlayer.GetBoneRotation(targetBone);
        if (bonePosition != Vector3.zero) // ボーン位置が有効な場合
        {
            // 位置を追従
            if (followPosition)
            {
                // オフセットを適用した目標位置
                Vector3 targetPosition = bonePosition + boneRotation * positionOffset;
                
                // スムーズに追従
                transform.position = Vector3.Lerp(transform.position, targetPosition, followStrength);
            }
            
            // 回転を追従
            if (followRotation)
            {
                // 回転オフセットを適用
                Quaternion offsetRotation = Quaternion.Euler(rotationOffset);
                Quaternion targetRotation = boneRotation * offsetRotation;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, followStrength);
            }

            // スケールを追従
            if (followScale)
            {
                float avatarHeightMeters = targetPlayer.GetAvatarEyeHeightAsMeters();
                float heightScale = avatarHeightMeters / DefaultAvatarHeightMeters;
                Vector3 baseScale = Vector3.one * heightScale;
                Vector3 targetScale = Vector3.Scale(baseScale, scaleOffset);
                transform.localScale = Vector3.Lerp(transform.localScale, targetScale, followStrength);
            }
        }
    }

    protected virtual void UpdateConstraint(VRCPlayerApi targetPlayer)
    {

        SetConstraint(targetPlayer);
    }

}
