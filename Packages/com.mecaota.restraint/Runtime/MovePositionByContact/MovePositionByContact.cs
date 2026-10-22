using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 接触(トリガー/コリジョン/パーティクル)したプレイヤーの、このオブジェクトに最も近いボーンへ
/// 空いている RestraintCore を装着する。
///
/// 接触イベントは各クライアントで発火するが、装着の書き込みは接触した本人のクライアントだけが行い
/// (player.isLocal)、Core の同期で全員に反映する。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MovePositionByContact : UdonSharpBehaviour
{
    [Header("Core設定")]
    [Tooltip("装着先の RestraintCore。空なら子から自動収集する")]
    public RestraintCore[] cores;

    [Tooltip("装着時に Core へ記録する装着元ID(channel)")]
    public int channel = 0;

    void Start()
    {
        if (cores == null || cores.Length == 0)
        {
            cores = gameObject.GetComponentsInChildren<RestraintCore>();
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        HandlePlayerContact(player);
    }

    public override void OnPlayerCollisionEnter(VRCPlayerApi player)
    {
        HandlePlayerContact(player);
    }

    public override void OnPlayerParticleCollision(VRCPlayerApi player)
    {
        HandlePlayerContact(player);
    }

    private void HandlePlayerContact(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) { return; }
        if (!player.isLocal) { return; } // 書き込みは本人のクライアントだけ(同期で全員に反映)
        if (cores == null) { return; }

        HumanBodyBones bone = RestraintCore.FindClosestBone(player, transform.position);
        for (int i = 0; i < cores.Length; i++)
        {
            RestraintCore core = cores[i];
            if (core == null || core.targetPlayerId >= 0) { continue; }
            if (core.AttachToBoneOnChannel(player.playerId, bone, channel)) { return; }
        }
    }
}
