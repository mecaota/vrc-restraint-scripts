using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common;

namespace Mecaota.Restraint.MovePositionByContact
{
    public class MovePositionByContact : UdonSharpBehaviour
    {
        [Header("Constraint設定")]
        [Tooltip("PlayerBoneConstraintが付いているオブジェクトの配列")]
        public PlayerBoneConstraint[] boneConstraints;

        void Start()
        {
            if(boneConstraints == null || boneConstraints.Length == 0) {
                boneConstraints = gameObject.GetComponentsInChildren<PlayerBoneConstraint>();
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
            Debug.Log($"[MovePositionByContact] Contacted {player.displayName}");
            if (player == null || !player.IsValid())
            {
                return;
            }

            // 接触位置（このオブジェクトの位置）に最も近いボーンを特定
            Vector3 contactPoint = transform.position;
            HumanBodyBones closestBone = FindClosestBone(player, contactPoint);

            // 利用可能なConstraintを探してセット
            bool success = AssignToAvailableConstraint(player, closestBone);

            if (success)
            {
                Debug.Log($"[MovePositionByContact] Attached {player.displayName}'s {closestBone} to constraint");
            }
            else
            {
                Debug.LogWarning("[MovePositionByContact] No available constraint found");
            }
        }

        private bool AssignToAvailableConstraint(VRCPlayerApi player, HumanBodyBones bone)
        {
            // 各constraintから未使用のものを探す
            foreach (PlayerBoneConstraint constraint in boneConstraints)
            {
                if (constraint == null || constraint.IsAttached())
                {
                    continue;
                }
                else
                {
                    // 未使用のConstraintが見つかった
                    constraint.gameObject.SetActive(true);
                    constraint.SetTargetPlayer(player.playerId);
                    constraint.SetTargetBone(bone);
                    return true;
                }
            }

            return false;
        }

        // 指定したポイントに最も近いボーンを見つけるメソッド
        private HumanBodyBones FindClosestBone(VRCPlayerApi player, Vector3 point)
        {
            HumanBodyBones closestBone = HumanBodyBones.Hips;
            float closestDistance = float.MaxValue;

            // Udonの配列サポートゴミだから全列挙脳筋比較
            float headDistance = GetDistanceToBone(player, HumanBodyBones.Head, point);
            if (headDistance < closestDistance)
            {
                closestDistance = headDistance;
                closestBone = HumanBodyBones.Head;
            }

            float neckDistance = GetDistanceToBone(player, HumanBodyBones.Neck, point);
            if (neckDistance < closestDistance)
            {
                closestDistance = neckDistance;
                closestBone = HumanBodyBones.Neck;
            }

            float chestDistance = GetDistanceToBone(player, HumanBodyBones.Chest, point);
            if (chestDistance < closestDistance)
            {
                closestDistance = chestDistance;
                closestBone = HumanBodyBones.Chest;
            }

            float spineDistance = GetDistanceToBone(player, HumanBodyBones.Spine, point);
            if (spineDistance < closestDistance)
            {
                closestDistance = spineDistance;
                closestBone = HumanBodyBones.Spine;
            }

            float hipsDistance = GetDistanceToBone(player, HumanBodyBones.Hips, point);
            if (hipsDistance < closestDistance)
            {
                closestDistance = hipsDistance;
                closestBone = HumanBodyBones.Hips;
            }

            float leftShoulderDistance = GetDistanceToBone(player, HumanBodyBones.LeftShoulder, point);
            if (leftShoulderDistance < closestDistance)
            {
                closestDistance = leftShoulderDistance;
                closestBone = HumanBodyBones.LeftShoulder;
            }

            float leftUpperArmDistance = GetDistanceToBone(player, HumanBodyBones.LeftUpperArm, point);
            if (leftUpperArmDistance < closestDistance)
            {
                closestDistance = leftUpperArmDistance;
                closestBone = HumanBodyBones.LeftUpperArm;
            }

            float leftLowerArmDistance = GetDistanceToBone(player, HumanBodyBones.LeftLowerArm, point);
            if (leftLowerArmDistance < closestDistance)
            {
                closestDistance = leftLowerArmDistance;
                closestBone = HumanBodyBones.LeftLowerArm;
            }

            float leftHandDistance = GetDistanceToBone(player, HumanBodyBones.LeftHand, point);
            if (leftHandDistance < closestDistance)
            {
                closestDistance = leftHandDistance;
                closestBone = HumanBodyBones.LeftHand;
            }

            float rightShoulderDistance = GetDistanceToBone(player, HumanBodyBones.RightShoulder, point);
            if (rightShoulderDistance < closestDistance)
            {
                closestDistance = rightShoulderDistance;
                closestBone = HumanBodyBones.RightShoulder;
            }

            float rightUpperArmDistance = GetDistanceToBone(player, HumanBodyBones.RightUpperArm, point);
            if (rightUpperArmDistance < closestDistance)
            {
                closestDistance = rightUpperArmDistance;
                closestBone = HumanBodyBones.RightUpperArm;
            }

            float rightLowerArmDistance = GetDistanceToBone(player, HumanBodyBones.RightLowerArm, point);
            if (rightLowerArmDistance < closestDistance)
            {
                closestDistance = rightLowerArmDistance;
                closestBone = HumanBodyBones.RightLowerArm;
            }

            float rightHandDistance = GetDistanceToBone(player, HumanBodyBones.RightHand, point);
            if (rightHandDistance < closestDistance)
            {
                closestDistance = rightHandDistance;
                closestBone = HumanBodyBones.RightHand;
            }

            float leftUpperLegDistance = GetDistanceToBone(player, HumanBodyBones.LeftUpperLeg, point);
            if (leftUpperLegDistance < closestDistance)
            {
                closestDistance = leftUpperLegDistance;
                closestBone = HumanBodyBones.LeftUpperLeg;
            }

            float leftLowerLegDistance = GetDistanceToBone(player, HumanBodyBones.LeftLowerLeg, point);
            if (leftLowerLegDistance < closestDistance)
            {
                closestDistance = leftLowerLegDistance;
                closestBone = HumanBodyBones.LeftLowerLeg;
            }

            float leftFootDistance = GetDistanceToBone(player, HumanBodyBones.LeftFoot, point);
            if (leftFootDistance < closestDistance)
            {
                closestDistance = leftFootDistance;
                closestBone = HumanBodyBones.LeftFoot;
            }

            float rightUpperLegDistance = GetDistanceToBone(player, HumanBodyBones.RightUpperLeg, point);
            if (rightUpperLegDistance < closestDistance)
            {
                closestDistance = rightUpperLegDistance;
                closestBone = HumanBodyBones.RightUpperLeg;
            }

            float rightLowerLegDistance = GetDistanceToBone(player, HumanBodyBones.RightLowerLeg, point);
            if (rightLowerLegDistance < closestDistance)
            {
                closestDistance = rightLowerLegDistance;
                closestBone = HumanBodyBones.RightLowerLeg;
            }

            float rightFootDistance = GetDistanceToBone(player, HumanBodyBones.RightFoot, point);
            if (rightFootDistance < closestDistance)
            {
                closestDistance = rightFootDistance;
                closestBone = HumanBodyBones.RightFoot;
            }

            return closestBone;
        }

        private float GetDistanceToBone(VRCPlayerApi player, HumanBodyBones bone, Vector3 position)
        {
            Vector3 bonePosition = player.GetBonePosition(bone);
            return Vector3.Distance(bonePosition, position);
        } 
    }
}
