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

        // ⭐ 優先用 Vision，沒有就用 IMU
        List<Vector2> trajectoryToUse = null;
        string source = "";

        if (useVisionFusion && visionBuffer.Count >= 15)
        {
            trajectoryToUse = ExtractVisionTrajectory(startTime, endTime);

            if (trajectoryToUse.Count >= 15)
            {
                source = "Vision";
                Debug.Log($"[Fusion] ✓ 使用 Vision 軌跡 ({trajectoryToUse.Count} 點)");
            }
            else
            {
                trajectoryToUse = new List<Vector2>(imuTrajectory);
                source = "IMU (Vision 不足)";
                Debug.Log($"[Fusion] 回退到 IMU");
            }
        }
        else
        {
            trajectoryToUse = new List<Vector2>(imuTrajectory);
            source = "IMU";
            Debug.Log($"[Fusion] 使用 IMU 軌跡");
        }

        Debug.Log($"[Fusion] 最終軌跡: {trajectoryToUse.Count} 點 (來源: {source})");

        gestureChain.RecognizeGesture(trajectoryToUse, trajectoryToUse);

        visionBuffer.Clear();
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