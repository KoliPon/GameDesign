using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UDPReceiver : MonoBehaviour
{
    private static UDPReceiver instance;
    public static UDPReceiver Instance => instance;

    [Header("=== 參照核心 ===")]
    public BattleManager battleManager;
    public TutorialManager tutorialManager;
    public GestureChain gestureChain;

    [Header("=== 網路設定 ===")]
    public int port = 5065;

    [Header("=== 2D UI 置中劃線設定 ===")]
    public RectTransform drawingArea;
    public Graphic uiLineRenderer;
    public float sensitivity = 120f;
    public float gThreshold = 1.05f;
    public float recordDuration = 1.5f;

    [Header("=== ⭐ 採樣點數優化設定 ===")]
    [SerializeField] private float fixedIMUDt = 0.002f;
    [SerializeField] private int maxPointsPerUpdate = 5;

    [Header("=== 辨識精準度設定 ===")]
    [SerializeField] private int resamplePointCount = 64;
    [SerializeField] private float angularDistanceWeight = 1.0f;
    [SerializeField] private bool useStrokeSpeedAnalysis = true;

    [Header("=== 防抖設定 ===")]
    [SerializeField] private float minPointDistanceThreshold = 0.8f;
    [SerializeField] private float maxGForceDelta = 3.0f;

    [Header("=== Vision 融合 ===")]
    [SerializeField] private bool useVisionFusion = true;

    private Thread receiveThread;
    private UdpClient client;
    private ConcurrentQueue<string> receiveQueue = new ConcurrentQueue<string>();
    private bool isRunning = true;
    private System.Diagnostics.Process pythonProcess;

    // 幾何辨識相關變數
    private List<Vector2> currentTrajectory = new List<Vector2>();
    private List<OneDollarRecognizer.GestureTemplate> templates = new List<OneDollarRecognizer.GestureTemplate>();
    private bool isRecording = false;
    private float startRecordingTime = 0f;
    private Vector2 virtualCursor = Vector2.zero;
    private Vector2 lastRecordedPosition = Vector2.zero;
    private float lastGForceForAccelCheck = 0f;

    // 9 軸感測器數據
    private float lastGX = 0f;
    private float lastGY = 0f;
    private float lastGZ = 0f;
    private float lastAX = 0f;
    private float lastAY = 0f;
    private float lastAZ = 0f;
    private float lastMX = 0f;
    private float lastMY = 0f;
    private float lastMZ = 0f;
    private float lastGValue = 0f;
    private float lastRawGValue = 0f;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;

        if (transform.parent == null)
        {
            DontDestroyOnLoad(this.gameObject);
        }
    }

    void Start()
    {
        InitializeTemplates();
        ClearLineImmediate();

        if (drawingArea == null)
            Debug.LogError("[Serial] ❌ drawingArea 沒有指定!");
        if (uiLineRenderer == null)
            Debug.LogError("[Serial] ❌ uiLineRenderer 沒有指定!");

        // ⭐ 在 Start 時取得一次 GestureChain 參考
        if (gestureChain == null)
        {
            gestureChain = FindAnyObjectByType<GestureChain>();
            if (gestureChain != null)
                Debug.Log("[UDP] ✓ 找到 GestureChain");
            else
                Debug.LogWarning("[UDP] ⚠ 暫時找不到 GestureChain (可能還未初始化)");
        }

        Debug.Log($"[UDP] 檢查 Python 是否在運行: {IsPythonRunning()}");

        if (!IsPythonRunning())
        {
            Debug.Log("[UDP] Python 未運行，嘗試啟動...");
            LaunchPython();
        }
        else
        {
            Debug.Log("[UDP] Python 已在運行");
        }

        Debug.Log($"[UDP] 準備啟動 UDP 接收線程，端口: {port}");
        Debug.Log($"[UDP] ⭐ 採樣間隔: {fixedIMUDt}s (預期點數: ~{recordDuration / fixedIMUDt:F0})");

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("[UDP] UDP 接收線程已啟動");
    }

    void Update()
    {
        // ⭐ 如果 gestureChain 還沒找到，嘗試再次查找
        if (gestureChain == null)
            gestureChain = FindAnyObjectByType<GestureChain>();

        bool trajectoryChanged = false;
        int queueCount = 0;

        while (receiveQueue.TryDequeue(out string incomingText))
        {
            queueCount++;
            
            // ⭐ 只在非冷卻狀態下處理 UDP 數據
            bool shouldProcess = true;
            if (gestureChain != null && gestureChain.IsInCooldown())
            {
                shouldProcess = false;
                Debug.Log($"[冷卻中] 忽略 UDP 數據 (cooldown active)");
            }

            if (shouldProcess)
            {
                ProcessIMUString(incomingText);
                trajectoryChanged = true;
            }
        }

        if (queueCount > 0)
        {
            Debug.Log($"[UDP] 收到 {queueCount} 個數據包，已處理: {(queueCount > 0 && gestureChain != null && !gestureChain.IsInCooldown() ? "✓" : "✗")}");
        }

        if (isRecording)
        {
            Debug.Log($"[UDP] 正在記錄，時間: {Time.time - startRecordingTime:F2}s，點數: {currentTrajectory.Count}");
        }

        if (isRecording && (Time.time - startRecordingTime >= recordDuration))
        {
            Debug.Log("時間到，自動觸發辨識...");
            EndDrawingAndRecognize();
        }

        if (trajectoryChanged && isRecording)
        {
            UpdateRealTimeLineRenderer();
        }
    }

    private void InitializeTemplates()
    {
        // 1. 圓形模板 (64 點)
        List<Vector2> circle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64f;
            circle.Add(new Vector2(Mathf.Cos(a) * 50f, Mathf.Sin(a) * 50f));
        }
        templates.Add(new OneDollarRecognizer.GestureTemplate("Circle", circle));

        // 2. 正方形模板 (64 點)
        List<Vector2> square = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float t = i / 64f;
            if (t < 0.25f) square.Add(new Vector2(Mathf.Lerp(-50, 50, t * 4f), 50));
            else if (t < 0.5f) square.Add(new Vector2(50, Mathf.Lerp(50, -50, (t - 0.25f) * 4f)));
            else if (t < 0.75f) square.Add(new Vector2(Mathf.Lerp(50, -50, (t - 0.5f) * 4f), -50));
            else square.Add(new Vector2(-50, Mathf.Lerp(-50, 50, (t - 0.75f) * 4f)));
        }
        templates.Add(new OneDollarRecognizer.GestureTemplate("Square", square));

        // 3. 三角形模板 (64 點)
        List<Vector2> triangle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float t = i / 64f;
            if (t < 0.33f) triangle.Add(new Vector2(Mathf.Lerp(0, 50, t * 3f), Mathf.Lerp(50, -50, t * 3f)));
            else if (t < 0.66f) triangle.Add(new Vector2(Mathf.Lerp(50, -50, (t - 0.33f) * 3f), -50));
            else triangle.Add(new Vector2(Mathf.Lerp(-50, 0, (t - 0.66f) * 3f), Mathf.Lerp(-50, 50, (t - 0.66f) * 3f)));
        }
        templates.Add(new OneDollarRecognizer.GestureTemplate("Triangle", triangle));
    }

    private List<Vector2> displayTrajectory = new List<Vector2>();

    private void ProcessIMUString(string dataStr)
    {
        string[] parts = dataStr.Split(',');
        if (parts.Length < 9) return;

        try
        {
            lastAX = float.Parse(parts[0]);
            lastAY = float.Parse(parts[1]);
            lastAZ = float.Parse(parts[2]);
            lastGX = float.Parse(parts[3]);
            lastGY = float.Parse(parts[4]);
            lastGZ = float.Parse(parts[5]);
            lastMX = float.Parse(parts[6]);
            lastMY = float.Parse(parts[7]);
            lastMZ = float.Parse(parts[8]);

            float gForce = Mathf.Sqrt(lastAX * lastAX + lastAY * lastAY + lastAZ * lastAZ);
            lastRawGValue = gForce;
            lastGValue = gForce;

            if (!isRecording && gForce > gThreshold)
            {
                isRecording = true;
                startRecordingTime = Time.time;
                currentTrajectory.Clear();
                displayTrajectory.Clear();
                virtualCursor = Vector2.zero;
                lastRecordedPosition = Vector2.zero;
                lastGForceForAccelCheck = gForce;
                Debug.Log($"[Arduino] 開始記錄 (G值: {gForce:F2})");
            }

            if (isRecording)
            {
                // ⭐ 新增：加速度突變視為噪聲，過濾該筆數據避免手抖誤記錄
                float gForceDelta = Mathf.Abs(gForce - lastGForceForAccelCheck);

                if (gForceDelta > maxGForceDelta)
                {
                    Debug.Log($"[防抖] 加速度突變過大，已過濾 (Delta: {gForceDelta:F2} > {maxGForceDelta})");
                    return;
                }

                lastGForceForAccelCheck = gForce;

                float horizontalInput = -lastGZ;
                float verticalInput = lastGY;

                virtualCursor.x += horizontalInput * sensitivity * fixedIMUDt;
                virtualCursor.y += verticalInput * sensitivity * fixedIMUDt;

                float distanceToLast = Vector2.Distance(virtualCursor, lastRecordedPosition);

                // ⭐ 新增：距離閾值過濾，相鄰點過近視為噪聲不記錄
                if (distanceToLast > minPointDistanceThreshold)
                {
                    int pointsToAdd = Mathf.Max(1, Mathf.FloorToInt(distanceToLast / minPointDistanceThreshold));
                    pointsToAdd = Mathf.Min(pointsToAdd, maxPointsPerUpdate);

                    for (int i = 1; i <= pointsToAdd; i++)
                    {
                        float t = (float)i / (pointsToAdd + 1);
                        Vector2 interpolatedPoint = Vector2.Lerp(lastRecordedPosition, virtualCursor, t);
                        currentTrajectory.Add(interpolatedPoint);
                        displayTrajectory.Add(interpolatedPoint);
                    }

                    lastRecordedPosition = virtualCursor;
                }
            }
        }
        catch (Exception e) { Debug.LogError("解析數據錯誤: " + e.Message); }
    }

    private void UpdateRealTimeLineRenderer()
    {
        if (uiLineRenderer == null || currentTrajectory.Count == 0 || drawingArea == null)
            return;

        List<Vector2> smoothedRaw = ApplyGaussianSmoothing(currentTrajectory);
        List<Vector2> renderPoints = GetSmoothedTrajectory(smoothedRaw);

        float targetWidth = drawingArea.rect.width * 0.8f;
        float targetHeight = drawingArea.rect.height * 0.8f;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in renderPoints)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float currentWidth = maxX - minX;
        float currentHeight = maxY - minY;
        Vector2 center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);

        if (currentWidth == 0) currentWidth = 1;
        if (currentHeight == 0) currentHeight = 1;

        float scale = Mathf.Min(targetWidth / currentWidth, targetHeight / currentHeight);
        scale = Mathf.Min(scale, 15f);

        List<Vector2> uiPoints = new List<Vector2>();
        foreach (var p in renderPoints)
        {
            Vector2 centeredPoint = (p - center) * scale;
            centeredPoint.x = Mathf.Clamp(centeredPoint.x, -targetWidth / 2f, targetWidth / 2f);
            centeredPoint.y = Mathf.Clamp(centeredPoint.y, -targetHeight / 2f, targetHeight / 2f);
            uiPoints.Add(centeredPoint);
        }

        CanvasLineRenderer canvasRenderer = uiLineRenderer as CanvasLineRenderer;
        if (canvasRenderer != null)
        {
            canvasRenderer.UpdateLine(uiPoints);
        }
    }

    public float GetGyroY()
    {
        return lastGY;
    }

    private List<Vector2> ApplyGaussianSmoothing(List<Vector2> input)
    {
        if (input.Count < 3) return new List<Vector2>(input);

        List<Vector2> smoothed = new List<Vector2>();

        float[] kernel = { 0.1f, 0.8f, 0.1f };

        for (int i = 0; i < input.Count; i++)
        {
            Vector2 result = Vector2.zero;
            float weightSum = 0f;

            for (int k = -1; k <= 1; k++)
            {
                int index = i + k;
                if (index >= 0 && index < input.Count)
                {
                    float weight = kernel[k + 1];
                    result += input[index] * weight;
                    weightSum += weight;
                }
            }

            smoothed.Add(result / weightSum);
        }

        return smoothed;
    }

    public void EndDrawingAndRecognize()
    {
        isRecording = false;

        Debug.Log($"[Arduino] 記錄結束，軌跡點數: {currentTrajectory.Count}");

        if (currentTrajectory.Count > 15)
        {
            List<Vector2> displayCopy = new List<Vector2>(displayTrajectory);
            List<Vector2> resampledTrajectory = ResampleTrajectory(currentTrajectory, resamplePointCount);
            List<Vector2> smoothedTrajectory = GetSmoothedTrajectory(resampledTrajectory);

            SensorFusionManager fusionManager = FindObjectOfType<SensorFusionManager>();
            if (fusionManager != null)
            {
                Debug.Log($"[Arduino] ✓ 呼叫 SensorFusionManager");
                fusionManager.OnIMUGestureDetected(
                    smoothedTrajectory,
                    startRecordingTime,
                    Time.time
                );
            }
            else
            {
                Debug.LogError("[Arduino] ❌ 找不到 SensorFusionManager");
            }

            displayTrajectory.Clear();
            StartCoroutine(ClearLineDelayed(0.6f));
        }
        else
        {
            Debug.Log($"[Arduino] ⚠ 軌跡點數不足: {currentTrajectory.Count} < 15");
        }
    }

    public List<Vector2> GetDisplayTrajectory()
    {
        return new List<Vector2>(displayTrajectory);
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

    private List<Vector2> GetSmoothedTrajectory(List<Vector2> input)
    {
        if (input.Count < 3) return new List<Vector2>(input);

        List<Vector2> padded = new List<Vector2>();
        padded.Add(input[0]);
        padded.AddRange(input);
        padded.Add(input[input.Count - 1]);

        List<Vector2> smoothed = new List<Vector2>();
        for (int i = 1; i < padded.Count - 2; i++)
        {
            smoothed.Add(padded[i]);
            for (int j = 1; j <= 3; j++)
            {
                float t = j / 4f;
                smoothed.Add(CalculateCatmullRom(t, padded[i - 1], padded[i], padded[i + 1], padded[i + 2]));
            }
        }
        smoothed.Add(padded[padded.Count - 2]);
        return smoothed;
    }

    private Vector2 CalculateCatmullRom(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        Vector2 a = 2f * p1;
        Vector2 b = p2 - p0;
        Vector2 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector2 d = -p0 + 3f * p1 - 3f * p2 + p3;
        return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
    }

    private void ClearLineImmediate()
    {
        CanvasLineRenderer canvasRenderer = uiLineRenderer as CanvasLineRenderer;
        if (canvasRenderer != null)
        {
            canvasRenderer.ClearLine();
        }
    }

    private System.Collections.IEnumerator ClearLineDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!isRecording)
        {
            ClearLineImmediate();
        }
    }

    private void ReceiveData()
    {
        try
        {
            Debug.Log($"[UDP] 開始監聽 UDP 端口 {port}");

            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPEndPoint bindPoint = new IPEndPoint(IPAddress.Any, port);
            client.Client.Bind(bindPoint);
            client.Client.ReceiveTimeout = 1000;

            Debug.Log($"[UDP] ✓ 已成功綁定到 0.0.0.0:{port}");

            int packetCount = 0;

            while (isRunning)
            {
                try
                {
                    IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = client.Receive(ref anyIP);
                    if (data != null && data.Length > 0)
                    {
                        packetCount++;
                        string cleanText = Encoding.UTF8.GetString(data).Trim();
                        receiveQueue.Enqueue(cleanText);

                        if (packetCount <= 5 || packetCount % 100 == 0)
                        {
                            Debug.Log($"[UDP] 已接收 {packetCount} 個數據包，最新: {cleanText.Substring(0, Mathf.Min(50, cleanText.Length))}");
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.TimedOut)
                    {
                        Debug.LogError($"[UDP] Socket 錯誤: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception err)
        {
            if (isRunning)
                Debug.LogError($"[UDP] 核心異常: {err.Message}\n{err.StackTrace}");
        }
    }

    public void ClearLine()
    {
        ClearLineImmediate();
        currentTrajectory.Clear();
        isRecording = false;
    }

    public void AddLinePoint(Vector2 screenPos)
    {
        if (drawingArea == null) return;

        Canvas canvas = drawingArea.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            drawingArea, screenPos, canvas.worldCamera, out Vector2 localPoint))
        {
            currentTrajectory.Add(localPoint);
            UpdateRealTimeLineRenderer();
        }
    }

    public void StartRecording()
    {
        if (!isRecording)
        {
            isRecording = true;
            startRecordingTime = Time.time;
            currentTrajectory.Clear();
            displayTrajectory.Clear();
            virtualCursor = Vector2.zero;
            lastRecordedPosition = Vector2.zero;
            Debug.Log($"[UDP] 開始記錄");
        }
    }

    public void FinishGesture()
    {
        if (isRecording)
        {
            EndDrawingAndRecognize();
        }
    }

    public float GetCurrentGValue()
    {
        return lastGValue;
    }

    public void TriggerRecognition()
    {
        EndDrawingAndRecognize();
    }

    public List<Vector2> GetCurrentTrajectory()
    {
        return new List<Vector2>(currentTrajectory);
    }

    public float GetGyroX()
    {
        return lastGX;
    }

    bool IsPythonRunning() { var processes = System.Diagnostics.Process.GetProcessesByName("python"); return processes.Length > 0; }
    void LaunchPython() { try { string projectRoot = Directory.GetParent(Application.dataPath).FullName; string batPath = Path.Combine(projectRoot, "MagicWand", "一鍵通用啟動器.bat"); if (File.Exists(batPath)) { pythonProcess = new System.Diagnostics.Process(); pythonProcess.StartInfo.FileName = batPath; pythonProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(batPath); pythonProcess.StartInfo.CreateNoWindow = false; pythonProcess.StartInfo.UseShellExecute = true; pythonProcess.Start(); Debug.Log("Python 雷達核心已啟動。"); } } catch (Exception e) { Debug.LogError("啟動失敗: " + e.Message); } }
    void OnDisable() { isRunning = false; if (client != null) { client.Close(); client = null; } if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500); if (pythonProcess != null && !pythonProcess.HasExited) { try { pythonProcess.Kill(); pythonProcess.Dispose(); } catch { } } }
}
