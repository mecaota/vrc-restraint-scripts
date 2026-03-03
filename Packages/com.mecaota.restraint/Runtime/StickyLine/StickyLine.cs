
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[RequireComponent(typeof(LineRenderer))]
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class StickyLine : UdonSharpBehaviour
{
    public GameObject targetObject;

    [Header("糸の設定")]
    [Tooltip("ランダムオフセットの範囲（例: 0.1なら-0.1～0.1の範囲でランダムに揺らぎが入る）")]
    public float randomOffsetRange = 0.1f;
    [Tooltip("線の本数")]
    public int lineCount = 1;
    [Tooltip("線の太さ")]
    public float lineWidth = 0.01f;
    [Tooltip("下方向のたわみ量")]
    public float sagAmount = 0.1f;
    [Tooltip("距離1メートルあたりのベジエ曲線分割数")]
    public int segmentsPerMeter = 1;

    private LineRenderer lineRenderer;
    private Vector3[] randomOffsets;

    void Start()
    {
        lineRenderer = gameObject.GetComponent<LineRenderer>();
        
        if (lineRenderer == null)
        {
            Debug.LogError("[StickyLine] LineRenderer component not found on " + gameObject.name);
            return;
        }
        
        // 線の太さのカーブを設定（中点で太く、両端で細く）
        AnimationCurve widthCurve = new AnimationCurve();
        widthCurve.AddKey(0f, 1f);  // 始点
        widthCurve.AddKey(0.05f, 0.5f);  // 始点直近
        widthCurve.AddKey(0.1f, 0.4f);
        widthCurve.AddKey(0.5f, 0.2f);  // 中点（細くする）
        widthCurve.AddKey(0.9f, 0.4f);
        widthCurve.AddKey(0.95f, 0.5f);  // 終点直近
        widthCurve.AddKey(1f, 1f);  // 終点
        
        lineRenderer.widthCurve = widthCurve;
        lineRenderer.widthMultiplier = lineWidth;

        // lineCount数だけランダムな揺らぎを生成
        randomOffsets = new Vector3[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            randomOffsets[i] = new Vector3(
                Random.Range(-randomOffsetRange, randomOffsetRange),
                Random.Range(-randomOffsetRange, randomOffsetRange),
                Random.Range(-randomOffsetRange, randomOffsetRange)
            );
        }
    }

    void Update()
    {
        UpdateLineRenderer();
    }

    private void UpdateLineRenderer()
    {
        if (lineRenderer == null) return;
        
        var targetPosition = targetObject.transform.position;
        var sourcePosition = transform.position;
        
        int totalPoints;
        int effectiveLineCount = Mathf.Max(1, lineCount);
        
        // sagAmountが0の場合は直線のみ
        if (sagAmount <= 0f)
        {
            totalPoints = effectiveLineCount * 2;
            lineRenderer.positionCount = totalPoints;
            
            for (int i = 0; i < effectiveLineCount; i++)
            {
                var endPosition = sourcePosition + (i < randomOffsets.Length ? randomOffsets[i] : Vector3.zero);
                lineRenderer.SetPosition(i * 2, targetPosition);
                lineRenderer.SetPosition(i * 2 + 1, endPosition);
            }
            return;
        }
        
        // 距離に応じて分割数を計算
        float distance = Vector3.Distance(targetPosition, sourcePosition);
        int segments = Mathf.Max(1, Mathf.RoundToInt(distance * segmentsPerMeter));
        
        // lineCountが1以下の場合は1本の線のみ（始点 + 中点 + 終点）
        if (effectiveLineCount == 1)
        {
            totalPoints = 1 + segments + 1;
            lineRenderer.positionCount = totalPoints;
            
            int currentIndex = 0;
            
            // 始点
            lineRenderer.SetPosition(currentIndex++, targetPosition);
            
            // 中点
            GenerateBezierPoints(targetPosition, sourcePosition, segments, currentIndex);
            currentIndex += segments;
            
            // 終点
            lineRenderer.SetPosition(currentIndex++, sourcePosition);
            return;
        }
        
        // 各線: 始点 + ベジエ中点 + 終点 + ベジエ中点 (往復)
        int pointsPerLine = 1 + segments + 1 + segments;
        totalPoints = effectiveLineCount * pointsPerLine;
        lineRenderer.positionCount = totalPoints;
        
        int currentIndex2 = 0;
        
        for (int i = 0; i < effectiveLineCount; i++)
        {
            var endPosition = sourcePosition + randomOffsets[i];
            
            // 始点
            lineRenderer.SetPosition(currentIndex2++, targetPosition);
            
            // 始点→終点のベジエ中点
            GenerateBezierPoints(targetPosition, endPosition, segments, currentIndex2);
            currentIndex2 += segments;
            
            // 終点
            lineRenderer.SetPosition(currentIndex2++, endPosition);
            
            // 終点→始点のベジエ中点
            GenerateBezierPoints(endPosition, targetPosition, segments, currentIndex2);
            currentIndex2 += segments;
        }
    }

    private void GenerateBezierPoints(Vector3 start, Vector3 end, int segments, int startIndex)
    {
        for (int i = 0; i < segments; i++)
        {
            float t = (float)(i + 1) / (segments + 1); // 始点と終点を除く中間点のみ
            var point = EvaluateSimpleSag(start, end, t, sagAmount);
            lineRenderer.SetPosition(startIndex + i, point);
        }
    }

    private Vector3 EvaluateSimpleSag(Vector3 start, Vector3 end, float t, float sag)
    {
        // 線形補間
        Vector3 linearPoint = Vector3.Lerp(start, end, t);
        
        // 放物線的なたわみ（中央で最大、両端で0）
        float sagFactor = 4f * t * (1f - t); // t=0.5で最大値1.0
        Vector3 sagOffset = Vector3.down * (sag * sagFactor);
        
        return linearPoint + sagOffset;
    }
}
