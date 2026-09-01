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

    [Header("=== 網路設定 ===")]
    public int port = 5065;

    [Header("=== 2D UI 置中劃線設定 ===")]
    public RectTransform drawingArea;
    public Graphic uiLineRenderer;
    public float sensitivity = 120f;
    public float gThreshold = 1.05f;
    public float recordDuration = 1.5f;

    [Header("=== 辨識精準度設定 ===")]
    [SerializeField] private int resamplePointCount = 64;
    [SerializeField] private float angularDistanceWeight = 1.0f;
    [SerializeField] private bool useStrokeSpeedAnalysis = true;

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

    // ⭐ 新增：9 軸感測器數據
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

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("[UDP] UDP 接收線程已啟動");
    }

    void Update()
    {
        FindManagers();

        bool trajectoryChanged = false;
        int queueCount = 0;

        while (receiveQueue.TryDequeue(out string incomingText))
        {
            queueCount++;
            ProcessIMUString(incomingText);
            trajectoryChanged = true;
        }

        if (queueCount > 0)
        {
            Debug.Log($"[UDP] 收到 {queueCount} 個數據包");
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

    private void FindManagers()
    {
        if (battleManager == null) battleManager = BattleManager.Instance;
        if (battleManager == null) battleManager = FindAnyObjectByType<BattleManager>();
        if (tutorialManager == null) tutorialManager = FindAnyObjectByType<TutorialManager>();
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

    private List<Vector2> displayTrajectory = new List<Vector2>();  // ⭐ 確保有宣告

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
                displayTrajectory.Clear();  // ⭐ 新增：清空顯示軌跡
                virtualCursor = Vector2.zero;
                Debug.Log($"[Arduino] 開始記錄");  // ⭐ 新增：debug log
            }

            if (isRecording)
            {
                float fixedIMUDt = 0.01f;
                float horizontalInput = -lastGZ;
                float verticalInput = lastGY;

                virtualCursor.x += horizontalInput * sensitivity * fixedIMUDt;
                virtualCursor.y += verticalInput * sensitivity * fixedIMUDt;

                currentTrajectory.Add(virtualCursor);
                displayTrajectory.Add(virtualCursor);  // ⭐ 同時保存顯示用
            }
        }
        catch (Exception e) { Debug.LogError("解析數據錯誤: " + e.Message); }
    }

    private void UpdateRealTimeLineRenderer()
    {
        if (uiLineRenderer == null || currentTrajectory.Count == 0 || drawingArea == null)
            return;

        // ⭐ 改進：先進行高斯平滑
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

    /// <summary>
    /// ⭐ 新增：高斯平滑（對抖動數據特別有效）
    /// </summary>
    private List<Vector2> ApplyGaussianSmoothing(List<Vector2> input)
    {
        if (input.Count < 3) return new List<Vector2>(input);

        List<Vector2> smoothed = new List<Vector2>();

        // 高斯核（3 點）
        float[] kernel = { 0.25f, 0.5f, 0.25f };

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

            // ⭐ 呼叫 Fusion
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

    private string ClassifyGestureWithImprovedLogic(List<Vector2> points, out float score)
    {
        score = 0f;

        string baseResult = OneDollarRecognizer.Classify(points, templates, out float baseScore);
        float circleRoundness = AnalyzeRoundness(points);
        float squareAngularity = AnalyzeAngularity(points);
        float triangleSharpness = AnalyzeTriangleSharpness(points);
        float aspectRatio = AnalyzeAspectRatio(points);

        // ⭐ 改進：更寬鬆的判定邏輯

        // 1. 圓形判定（最圓）
        if (circleRoundness > 0.65f)  // 改：0.72 → 0.65
        {
            if (circleRoundness > squareAngularity && circleRoundness > triangleSharpness)
            {
                score = circleRoundness;
                Debug.Log($"✓ Circle (圓度: {circleRoundness:F3})");
                return "Circle";
            }
        }

        // 2. 三角形判定（最尖銳）
        if (triangleSharpness > 0.55f)  // 改：0.62 → 0.55
        {
            if (triangleSharpness > circleRoundness && triangleSharpness > squareAngularity)
            {
                score = triangleSharpness;
                Debug.Log($"✓ Triangle (尖銳度: {triangleSharpness:F3})");
                return "Triangle";
            }
        }

        // 3. 正方形判定（最有角）
        if (squareAngularity > 0.60f && aspectRatio > 0.65f)  // 改低要求
        {
            score = squareAngularity;
            Debug.Log($"✓ Square (角度: {squareAngularity:F3}, 長寬比: {aspectRatio:F3})");
            return "Square";
        }

        // 4. One Dollar 備選
        if (baseScore > 0.50f)  // 改：0.58 → 0.50
        {
            score = baseScore;
            Debug.Log($"✓ {baseResult} (One Dollar: {baseScore:F3})");
            return baseResult;
        }

        // 5. 都不確定，用最高信度
        string[] results = { "Circle", "Square", "Triangle" };
        float[] scores = { circleRoundness, squareAngularity, triangleSharpness };

        int maxIndex = 0;
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i] > scores[maxIndex]) maxIndex = i;
        }

        score = scores[maxIndex];
        Debug.Log($"⚠ 備選: {results[maxIndex]} (信度: {score:F3})");
        return results[maxIndex];
    }

    private float AnalyzeTriangleSharpness(List<Vector2> points)
    {
        if (points.Count < 5) return 0f;

        // 採樣均勻的點（避免噪聲）
        List<Vector2> sampledPoints = new List<Vector2>();
        int sampleStep = Mathf.Max(1, points.Count / 20);  // 採樣 20 個點
        for (int i = 0; i < points.Count; i += sampleStep)
        {
            sampledPoints.Add(points[i]);
        }

        if (sampledPoints.Count < 3) return 0f;

        int sharpCorners = 0;
        float totalAngle = 0f;
        int angleCount = 0;

        // 檢查轉折點的銳利度
        for (int i = 1; i < sampledPoints.Count - 1; i++)
        {
            Vector2 v1 = (sampledPoints[i] - sampledPoints[i - 1]).normalized;
            Vector2 v2 = (sampledPoints[i + 1] - sampledPoints[i]).normalized;

            float angle = Vector2.Angle(v1, v2);
            angleCount++;

            // 銳角（> 70°）
            if (angle > 70f)
            {
                sharpCorners++;
                totalAngle += angle;
            }
        }

        if (angleCount == 0) return 0f;

        // 三角形應該有 3 個明顯的銳角
        float cornerRatio = Mathf.Clamp01(sharpCorners / 3f);
        float avgAngle = totalAngle / Mathf.Max(1, sharpCorners);
        float angleSharpness = Mathf.Clamp01((avgAngle - 70f) / 90f);  // 70~160° 映射到 0~1

        float sharpness = Mathf.Lerp(cornerRatio, angleSharpness, 0.6f);

        Debug.Log($"[三角檢測] 銳角數: {sharpCorners}/3, 平均角度: {avgAngle:F1}°, 尖銳度: {sharpness:F2}");

        return Mathf.Clamp01(sharpness);
    }

    private float AnalyzeRoundness(List<Vector2> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width == 0 || height == 0) return 0f;

        float aspectRatio = Mathf.Min(width, height) / Mathf.Max(width, height);

        Vector2 center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        float avgDistance = 0f;
        float distanceVariance = 0f;

        foreach (var p in points)
        {
            avgDistance += Vector2.Distance(p, center);
        }
        avgDistance /= points.Count;

        foreach (var p in points)
        {
            float dist = Vector2.Distance(p, center);
            distanceVariance += (dist - avgDistance) * (dist - avgDistance);
        }
        distanceVariance = Mathf.Sqrt(distanceVariance / points.Count);

        float circularity = aspectRatio * (1f - Mathf.Clamp01(distanceVariance / avgDistance));
        return Mathf.Clamp01(circularity);
    }

    private float AnalyzeAngularity(List<Vector2> points)
    {
        if (points.Count < 5) return 0f;

        // 採樣均勻的點
        List<Vector2> sampledPoints = new List<Vector2>();
        int sampleStep = Mathf.Max(1, points.Count / 16);  // 採樣 16 個點
        for (int i = 0; i < points.Count; i += sampleStep)
        {
            sampledPoints.Add(points[i]);
        }

        if (sampledPoints.Count < 4) return 0f;

        int sharpAngles = 0;
        float totalAngleChange = 0f;
        int angleCount = 0;

        for (int i = 1; i < sampledPoints.Count - 1; i++)
        {
            Vector2 v1 = (sampledPoints[i] - sampledPoints[i - 1]).normalized;
            Vector2 v2 = (sampledPoints[i + 1] - sampledPoints[i]).normalized;

            float angle = Vector2.Angle(v1, v2);
            totalAngleChange += angle;
            angleCount++;

            // 檢測 60~120° 的角（正方形的角通常在 80~100°）
            if (angle > 60f && angle < 130f)
            {
                sharpAngles++;
            }
        }

        if (angleCount == 0) return 0f;

        // 正方形應該有約 4 個轉折角
        float cornerRatio = Mathf.Clamp01(sharpAngles / 4f);
        float avgAngle = totalAngleChange / angleCount;
        float angleConsistency = 1f - Mathf.Abs((avgAngle - 90f) / 90f);  // 越接近 90° 越好

        float angularity = Mathf.Lerp(cornerRatio, angleConsistency, 0.5f);

        Debug.Log($"[角度檢測] 轉折數: {sharpAngles}/4, 平均角度: {avgAngle:F1}°, 角度性: {angularity:F2}");

        return Mathf.Clamp01(angularity);
    }

    private float AnalyzeAspectRatio(List<Vector2> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width == 0 || height == 0) return 0f;

        return Mathf.Min(width, height) / Mathf.Max(width, height);
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

            // ⭐ 改：用 IPAddress.Any 而不是 127.0.0.1
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

    // ⭐ 新增：公開方法讓 GestureChain 取得陀螺儀 GX 軸
    public float GetGyroX()
    {
        return lastGX;
    }

    bool IsPythonRunning() { var processes = System.Diagnostics.Process.GetProcessesByName("python"); return processes.Length > 0; }
    void LaunchPython() { try { string projectRoot = Directory.GetParent(Application.dataPath).FullName; string batPath = Path.Combine(projectRoot, "MagicWand", "一鍵通用啟動器.bat"); if (File.Exists(batPath)) { pythonProcess = new System.Diagnostics.Process(); pythonProcess.StartInfo.FileName = batPath; pythonProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(batPath); pythonProcess.StartInfo.CreateNoWindow = false; pythonProcess.StartInfo.UseShellExecute = true; pythonProcess.Start(); Debug.Log("Python 雷達核心已啟動。"); } } catch (Exception e) { Debug.LogError("啟動失敗: " + e.Message); } }
    void OnDisable() { isRunning = false; if (client != null) { client.Close(); client = null; } if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500); if (pythonProcess != null && !pythonProcess.HasExited) { try { pythonProcess.Kill(); pythonProcess.Dispose(); } catch { } } }
}