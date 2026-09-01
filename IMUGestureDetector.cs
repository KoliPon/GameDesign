using UnityEngine;

public class IMUGestureDetector : MonoBehaviour
{
    [Header("== IMU 手勢偵測 ==")]
    [SerializeField] private float gThreshold = 0.7f;  // ⭐ 改：降低閾值，更容易觸發
    [SerializeField] private int minFramesToStart = 5;      // ⭐ 改：10 → 5（更快開始）
    [SerializeField] private int minFramesToEnd = 60;       // ⭐ 改：25 → 60（不易中斷）
    [SerializeField] private float recordDuration = 3.0f;   // ⭐ 改：2.0 → 3.0（更寬鬆）

    private UDPReceiver udpReceiver;
    private bool isRecording = false;
    private float recordingStartTime = -1f;
    private int aboveThresholdCount = 0;
    private int belowThresholdCount = 0;

    void Start()
    {
        udpReceiver = FindObjectOfType<UDPReceiver>();
        if (udpReceiver == null)
        {
            Debug.LogError("❌ 找不到 UDPReceiver");
            enabled = false;
            return;
        }

        Debug.Log("[IMU] 手勢偵測器初始化完成");
        Debug.Log($"[IMU] 觸發閾值: {gThreshold}, 開始幀數: {minFramesToStart}, 結束幀數: {minFramesToEnd}");
    }

    void Update()
    {
        if (udpReceiver == null) return;

        DetectGesture();
    }

    private void DetectGesture()
    {
        float currentG = udpReceiver.GetCurrentGValue();

        // 檢測開始
        if (!isRecording)
        {
            if (currentG > gThreshold)
            {
                aboveThresholdCount++;
                belowThresholdCount = 0;

                if (aboveThresholdCount >= minFramesToStart)
                {
                    StartRecording();
                }
            }
            else
            {
                aboveThresholdCount = 0;
            }
        }
        // 檢測結束
        else
        {
            // ⭐ 改：條件更嚴格，不易誤判
            if (currentG < gThreshold * 0.8f)  // 改成 threshold 的 80%
            {
                belowThresholdCount++;
                aboveThresholdCount = 0;

                if (belowThresholdCount >= minFramesToEnd)
                {
                    EndRecording();
                }
            }
            else
            {
                belowThresholdCount = 0;  // ⭐ 改：重置計數
            }

            // 超時結束
            if (Time.time - recordingStartTime >= recordDuration)
            {
                Debug.Log("[IMU] 記錄超時，自動結束");
                EndRecording();
            }
        }

        // ⭐ 添加 DEBUG（每 60 幀打一次）
        if (Time.frameCount % 60 == 0 && isRecording)
        {
            Debug.Log($"[IMU] 記錄中... G值: {currentG:F2}, 低於閾值計數: {belowThresholdCount}/{minFramesToEnd}");
        }
    }

    private void StartRecording()
    {
        isRecording = true;
        recordingStartTime = Time.time;
        aboveThresholdCount = 0;
        belowThresholdCount = 0;

        udpReceiver.StartRecording();
        Debug.Log("[IMU] ✓ 開始記錄手勢");
    }

    private void EndRecording()
    {
        isRecording = false;
        belowThresholdCount = 0;
        aboveThresholdCount = 0;

        Debug.Log("[IMU] ✓ 結束記錄手勢");
        udpReceiver.FinishGesture();
    }

    // ⭐ 新增：取消 UDPReceiver 的自動記錄
    public void CancelAutoRecording()
    {
        if (udpReceiver != null)
        {
            udpReceiver.CancelAutoRecording();
        }
    }

    // ⭐ 新增：公開方法讓外部調整敏感度
    public void SetSensitivity(float threshold, int framesToEnd)
    {
        gThreshold = threshold;
        minFramesToEnd = framesToEnd;
        Debug.Log($"[IMU] 敏感度已調整: threshold={threshold}, framesToEnd={framesToEnd}");
    }
}
