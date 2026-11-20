using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// targetObject から自位置へ糸(LineRenderer)を毎フレーム張る。たわみは spider-shader の SpiderSilk が描く。
/// core を設定すると、その RestraintCore が装着中のときだけ描画する(未装着時は LineRenderer を無効化)。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[RequireComponent(typeof(LineRenderer))]
public class StickyLine : UdonSharpBehaviour
{
    public GameObject targetObject;

    [Tooltip("設定すると、この RestraintCore が装着中のときだけ糸を描く(常時アクティブな糸を、装着中だけ見せる用)")]
    public RestraintCore core;

    [Tooltip("core の装着元ID(channel)がこの値のときだけ描く。-1 ならどの装着元でも描く")]
    public int channel = RestraintCore.CHANNEL_ANY;

    [Tooltip("糸の長さ 1m あたりのたわみ(m)。SpiderSilk のマテリアルでだけ効き、0 で直線")]
    public float sagAmount = 0.06f;

    // シェーダーが頂点を下げてたわませるので、直線上にも分割点が要る
    private const int Points = 9;

    private LineRenderer lineRenderer;
    private MaterialPropertyBlock block;

    void Start()
    {
        lineRenderer = gameObject.GetComponent<LineRenderer>();
        lineRenderer.positionCount = Points;
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        // core が無いときは表示の ON/OFF に触らない(アニメーションなどほかの仕組みで消している場合を壊さない)
        if (core != null)
        {
            bool visible = core.IsAttachedOnChannel(channel);
            if (lineRenderer.enabled != visible) { lineRenderer.enabled = visible; }
            if (!visible) return;
        }

        Vector3 from = targetObject.transform.position;
        Vector3 to = transform.position;
        for (int i = 0; i < Points; i++) { lineRenderer.SetPosition(i, Vector3.Lerp(from, to, i / (float)(Points - 1))); }

        float length = Vector3.Distance(from, to);
        lineRenderer.GetPropertyBlock(block);
        block.SetFloat("_GravityUseUV", 1f);
        block.SetFloat("_GravityLineLength", length);
        block.SetFloat("_GravitySag", sagAmount * length);
        lineRenderer.SetPropertyBlock(block);
    }
}
