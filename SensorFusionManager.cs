using System.Collections.Generic;
using UnityEngine;

public class SensorFusionManager : MonoBehaviour
{
    private static SensorFusionManager instance;
    public static SensorFusionManager Instance => instance;

    [Header("== 融合參數 ==")]
    [SerializeField] private float minVisionConfidence = 0.3f;
    [SerializeField] private bool useVisionFusion = true;

    private UDPReceiver imuReceiver;
    private WandTrajectoryExtractor visionExtractor;
    private GestureChain gestureChain;

    private List<(float timestamp, Vector2 point)> visionBuffer = new List<(float, Vector2)>();
    private const float BUFFER_DURATION = 5.0f;
    private const int MAX_BUFFER_SIZE = 2000;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;
    }

    void Start()
    {
        imuReceiver = FindObjectOfType<UDPReceiver>();
        visionExtractor = FindObjectOfType<WandTrajectoryExtractor>();
        gestureChain = FindObjectOfType<GestureChain>();

        if (imuReceiver == null)
            Debug.LogError("❌ 找不到 UDPReceiver");
        if (visionExtractor == null)
            Debug.LogError("❌ 找不到 WandTrajectoryExtractor");
        if (gestureChain == null)
            Debug.LogError("❌ 找不到 GestureChain");

        Debug.Log("✓ SensorFusionManager 初始化完成");
    }

    void Update()
    {
        if (imuReceiver == null || visionExtractor == null || gestureChain == null)
            return;

        CleanVisionBuffer();

        // ⭐ 蒐集 Vision 數據
        if (useVisionFusion && visionExtractor.TrackingConfidence >= minVisionConfidence)
        {
            visionBuffer.Add((Time.time, visionExtractor.CurrentWandPosition));
        }
    }

    public void OnIMUGestureDetected(List<Vector2> imuTrajectory, float startTime, float endTime)
    {
        Debug.Log($"[Fusion] IMU 軌跡: {imuTrajectory.Count} 點, Vision: {visionBuffer.Count} 點");

        // 提取對應時間段的視覺軌跡
        List<Vector2> visionTrajectory = ExtractVisionTrajectory(startTime, endTime);

        // ⭐ 改進：不是"覆蓋"，而是評估兩條軌跡的質量
        float imuQuality = EvaluateTrajectoryQuality(imuTrajectory);
        float visionQuality = EvaluateTrajectoryQuality(visionTrajectory);

        Debug.Log($"[Fusion] IMU 質量: {imuQuality:F2}, Vision 質量: {visionQuality:F2}");

        List<Vector2> trajectoryToUse = null;

        // 情況 1：兩條都可用 → 融合
        if (imuQuality > 0.5f && visionQuality > 0.5f && visionTrajectory.Count > 15)
        {
            trajectoryToUse = FuseTrajectories(imuTrajectory, visionTrajectory, imuQuality, visionQuality);
            Debug.Log($"[Fusion] 使用融合軌跡 ({trajectoryToUse.Count} 點)");
        }
        // 情況 2：只有一條可用 → 用那一條
        else if (visionQuality > imuQuality)
        {
            trajectoryToUse = visionTrajectory;
            Debug.Log($"[Fusion] 使用 Vision");
        }
        else
        {
            trajectoryToUse = imuTrajectory;
            Debug.Log($"[Fusion] 使用 IMU");
        }

        gestureChain.RecognizeGesture(trajectoryToUse, trajectoryToUse);
        visionBuffer.Clear();
    }

    private float EvaluateTrajectoryQuality(List<Vector2> trajectory)
    {
        if (trajectory.Count < 10) return 0f;

        // 評估標準：
        // 1. 點數充足
        // 2. 路徑不碎片
        // 3. 軌跡長度合理

        float pathLength = CalculatePathLength(trajectory);
        float minExpected = 50f;   // 最小軌跡長度
        float maxExpected = 2000f; // 最大軌跡長度

        float score = Mathf.Clamp01(
            (trajectory.Count / 64f) * 0.3f +
            Mathf.Clamp01(pathLength / maxExpected) * 0.7f
        );

        return score;
    }

    private List<Vector2> FuseTrajectories(
        List<Vector2> imu,
        List<Vector2> vision,
        float imuWeight,
        float visionWeight)
    {
        float totalWeight = imuWeight + visionWeight;
        float imuRatio = imuWeight / totalWeight;
        float visionRatio = visionWeight / totalWeight;

        // 重採樣到相同點數
        int targetPoints = 64;
        List<Vector2> imuResampled = ResampleTrajectory(imu, targetPoints);
        List<Vector2> visionResampled = ResampleTrajectory(vision, targetPoints);

        List<Vector2> fused = new List<Vector2>();
        for (int i = 0; i < targetPoints; i++)
        {
            Vector2 p = Vector2.Lerp(imuResampled[i], visionResampled[i], visionRatio);
            fused.Add(p);
        }

        return fused;
    }

    private List<Vector2> ResampleTrajectory(List<Vector2> input, int targetPoints)
    {
        if (input.Count <= 2) return new List<Vector2>(input);

        List<Vector2> output = new List<Vector2>();
        float totalDistance = 0f;

        for (int i = 1; i < input.Count; i++)
        {
            totalDistance += Vector2.Distance(input[i - 1], input[i]);
        }

        if (totalDistance == 0) return new List<Vector2>(input);

        float interval = totalDistance / (targetPoints - 1);
        float currentDistance = 0f;
        output.Add(input[0]);

        for (int i = 1; i < input.Count; i++)
        {
            float segmentLength = Vector2.Distance(input[i - 1], input[i]);
            currentDistance += segmentLength;

            while (output.Count < targetPoints && currentDistance >= interval * output.Count)
            {
                float t = (interval * output.Count - (currentDistance - segmentLength)) / segmentLength;
                Vector2 point = Vector2.Lerp(input[i - 1], input[i], Mathf.Clamp01(t));
                output.Add(point);
            }
        }

        while (output.Count < targetPoints)
        {
            output.Add(input[input.Count - 1]);
        }

        return output;
    }

    private float CalculatePathLength(List<Vector2> points)
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    private List<Vector2> ExtractVisionTrajectory(float startTime, float endTime)
    {
        List<Vector2> result = new List<Vector2>();

        foreach (var (timestamp, point) in visionBuffer)
        {
            if (timestamp >= startTime && timestamp <= endTime)
            {
                result.Add(point);
            }
        }

        return result;
    }

    private void CleanVisionBuffer()
    {
        float cutoffTime = Time.time - BUFFER_DURATION;
        visionBuffer.RemoveAll(item => item.timestamp < cutoffTime);
    }
}
