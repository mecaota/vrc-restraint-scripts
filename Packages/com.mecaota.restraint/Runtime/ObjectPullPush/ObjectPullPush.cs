using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

public enum PullPushMode
{
    Pull,
    Push
}

namespace Mecaota.Restraint.ObjectPullPush
{

    public class ObjectPullPush : UdonSharpBehaviour
    {
        // Rigidbodyキャッシュ（複数対応）
        private Rigidbody[] cachedRigidbodies = null;

        [Header("閾値")]
        public float minDistance = 0.1f;
        [Header("引き寄せ/押し出し対象(複数可)")]
        public GameObject[] targetObjects; // 対象オブジェクト群
        [Header("引き寄せ/押し出しの強さ")]
        public float force = 5f;
        [Header("InteractでPush/Pull切替を有効にする")]
        public bool enableInteractToggle = true;
        [Header("モード (Pull/Push)")]
        public PullPushMode mode = PullPushMode.Pull;
        [Header("自動動作")]
        public bool autoUpdate = false;


        // 外部からPush/Pull切替
        // mode: PullPushMode.Pull または PullPushMode.Push を直接指定
        public void SetPullMode(PullPushMode newMode)
        {
            mode = newMode;
        }

        // forceを掛けたベクトルを返す
        private Vector3 GetForceVector(GameObject obj)
        {
            if (obj == null) return Vector3.zero;
            float dist = Vector3.Distance(transform.position, obj.transform.position);
            if (dist < minDistance)
            {
                return Vector3.zero;
            }
            Vector3 dir = (transform.position - obj.transform.position).normalized;
            if (mode == PullPushMode.Push) dir = -dir;
            return dir * force;
        }

        // targetObjectsセット時にRigidbodyをキャッシュ
        private void CacheRigidbodies()
        {
            if (targetObjects == null)
            {
                cachedRigidbodies = null;
                return;
            }
            cachedRigidbodies = new Rigidbody[targetObjects.Length];
            for (int i = 0; i < targetObjects.Length; i++)
            {
                if (targetObjects[i] != null)
                {
                    cachedRigidbodies[i] = targetObjects[i].GetComponent<Rigidbody>();
                }
                else
                {
                    cachedRigidbodies[i] = null;
                }
            }
        }

        // targetObjectsのプロパティ変更時にキャッシュ更新
        public void SetTargetObjects(GameObject[] objs)
        {
            targetObjects = objs;
            CacheRigidbodies();
        }

        void Update()
        {
            if (autoUpdate)
            {
                DoAction();
            }
        }

        // VRC_Pickup連携: InteractでPush/Pull切替、Useで実行
        public override void Interact()
        {
            if (enableInteractToggle)
            {
                ToggleMode(); // Interact時はprivateトグル関数を呼ぶ
            }
        }

        public override void OnPickupUseDown()
        {
            autoUpdate = true;
        }

        public override void OnPickupUseUp()
        {
            autoUpdate = false;
        }

        // モードをトグルするprivate関数
        private void ToggleMode()
        {
            mode = (mode == PullPushMode.Pull) ? PullPushMode.Push : PullPushMode.Pull;
        }

        // 外部から即時Push/Pull実行（複数対応）
        public void DoAction()
        {
            if (targetObjects == null) return;
            for (int i = 0; i < targetObjects.Length; i++)
            {
                GameObject obj = targetObjects[i];
                if (obj == null) continue;
                Vector3 forceVec = GetForceVector(obj);
                Rigidbody rb = (cachedRigidbodies != null && i < cachedRigidbodies.Length) ? cachedRigidbodies[i] : null;
                if (rb != null)
                {
                    rb.AddForce(forceVec, ForceMode.VelocityChange);
                }
                else
                {
                    obj.transform.position += forceVec * Time.deltaTime;
                }
            }
        }
    }
}
