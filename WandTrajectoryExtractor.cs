using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WandTrajectoryExtractor : MonoBehaviour
{
    [Header("== 紅色標記追蹤設定 ==")]
    [SerializeField] private float redThreshold = 0.7f;
    [SerializeField] private float redDominance = 0.3f;
    [SerializeField] private int minPointCount = 5;
    [SerializeField] private float markerMinSize = 10f;
    [SerializeField] private float markerMaxSize = 5000f;

    [Header("== 偵錯 ==")]
    [SerializeField] private bool drawDebugVisuals = true;
    [SerializeField] private RawImage debugDisplay;
    [SerializeField] private Text debugText;

    private Texture2D frameTexture;
    private Texture2D debugTexture;
    private List<Vector2> currentWandPoints = new List<Vector2>();

    public List<Vector2> CurrentWandPoints => new List<Vector2>(currentWandPoints);
    public Vector2 CurrentWandPosition { get; private set; }
    public float TrackingConfidence { get; private set; }

    private bool isInitialized = false;

    void Start()
    {
        Debug.Log("[Wand] ========== START 開始 ==========");
        // ⭐ 改成 Coroutine，非同步初始化
        StartCoroutine(InitializeAsync());
    }

    /// <summary>
    /// ⭐ 非同步初始化
    /// </summary>
    private IEnumerator InitializeAsync()
    {
        Debug.Log("[Wand] 等待 CameraInputManager 初始化...");

        CameraInputManager cameraManager = null;
        int waitCount = 0;

        // ⭐ 每幀檢查一次，不要同步卡住
        while (cameraManager == null || !cameraManager.IsInitialized)
        {
            cameraManager = CameraInputManager.Instance;
            waitCount++;

            if (waitCount > 300)  // 300 幀 ≈ 5 秒
            {
                Debug.LogError("❌ 攝像頭初始化超時 (等了 300 幀)");
                enabled = false;
                yield break;
            }

            if (waitCount % 60 == 0)
            {
                Debug.Log($"[Wand] 等待中... ({waitCount} 幀)");
            }

            yield return null;  // ⭐ 關鍵：每幀等一下
        }

        Debug.Log($"[Wand] ✓ CameraInputManager 已初始化 (等了 {waitCount} 幀)");

        if (cameraManager.CurrentFrame == null)
        {
            Debug.LogError("❌ 攝像頭 Texture 為 null");
            enabled = false;
            yield break;
        }

        int width = cameraManager.CurrentFrame.width;
        int height = cameraManager.CurrentFrame.height;

        Debug.Log($"[Wand] 攝像頭解析度: {width}x{height}");

        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"❌ 攝像頭解析度無效: {width}x{height}");
            enabled = false;
            yield break;
        }

        frameTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        debugTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        isInitialized = true;

        Debug.Log($"[Wand] ✓ 初始化完成");
        Debug.Log("[Wand] ========== START 結束 ==========\n");
    }

    void Update()
    {
        if (!isInitialized) return;

        CameraInputManager cameraManager = CameraInputManager.Instance;
        if (cameraManager == null || !cameraManager.IsInitialized) return;

        // ⭐ 改：不是每幀都讀，改成每 2 幀讀一次
        if (Time.frameCount % 2 == 0)
        {
            TrackRedMarker();
        }
    }

    private void TrackRedMarker()
    {
        CameraInputManager cameraManager = CameraInputManager.Instance;
        if (cameraManager == null) return;

        Texture sourceTexture = cameraManager.CurrentFrame;
        if (sourceTexture == null) return;

        if (frameTexture == null) return;

        RenderTexture rt = null;

        try
        {
            rt = new RenderTexture(sourceTexture.width, sourceTexture.height, 0);

            if (rt == null) return;

            Graphics.Blit(sourceTexture, rt);
            RenderTexture.active = rt;

            if (frameTexture.width != rt.width || frameTexture.height != rt.height)
            {
                Destroy(frameTexture);
                frameTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            }

            frameTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            frameTexture.Apply();

            RenderTexture.active = null;

            // ⭐ 提取紅色
            List<Vector2> redPixels = ExtractRedMarkerPixels();

            // ⭐ DEBUG：有紅色或每秒打一次
            if (redPixels.Count > 0)
            {
                //Debug.Log($"[Wand] 紅色像素: {redPixels.Count}");
            }

            if (redPixels.Count >= minPointCount)
            {
                CurrentWandPosition = CalculateCentroid(redPixels);
                currentWandPoints.Add(CurrentWandPosition);
                TrackingConfidence = Mathf.Clamp01(redPixels.Count / 500f);

                //Debug.Log($"[Wand] ✓ 追蹤: 信度={TrackingConfidence:F2}, 像素={redPixels.Count}");
            }
            else
            {
                TrackingConfidence = 0f;
            }

            if (currentWandPoints.Count > 200)
            {
                currentWandPoints.RemoveAt(0);
            }

            if (drawDebugVisuals && debugDisplay != null)
            {
                debugDisplay.texture = frameTexture;
            }

            if (debugText != null)
            {
                debugText.text = $"紅色標記追蹤\n位置: {CurrentWandPosition}\n信度: {TrackingConfidence:F2}\n像素: {redPixels.Count}\n軌跡: {currentWandPoints.Count}";
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ TrackRedMarker 異常: {ex.Message}");
        }
        finally
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        }
    }

    private List<Vector2> ExtractRedMarkerPixels()
    {
        List<Vector2> result = new List<Vector2>();
        if (frameTexture == null) return result;

        Color[] pixels = frameTexture.GetPixels();
        if (pixels == null || pixels.Length == 0) return result;

        int width = frameTexture.width;
        int height = frameTexture.height;
        int sampleStep = 4;

        for (int y = 0; y < height; y += sampleStep)
        {
            for (int x = 0; x < width; x += sampleStep)
            {
                int i = y * width + x;
                if (i < pixels.Length && IsRedMarker(pixels[i]))
                {
                    result.Add(new Vector2(x, height - y));
                }
            }
        }

        return result;
    }

    private bool IsRedMarker(Color pixel)
    {
        // ⭐ 使用 HSV 方法更精確
        float h, s, v;
        Color.RGBToHSV(pixel, out h, out s, out v);

        // 紅色範圍：H ≈ 0 或 ≈ 1（0-360°）
        bool isRedHue = (h < 0.05f || h > 0.95f);  // ±30° 範圍

        // 飽和度和亮度要求
        bool isSaturated = s > 0.30f;  // 足夠鮮豔
        bool isBright = v > 0.30f;      // 足夠明亮

        return isRedHue && isSaturated && isBright;
    }

    private Vector2 CalculateCentroid(List<Vector2> points)
    {
        if (points.Count == 0) return Vector2.zero;

        Vector2 sum = Vector2.zero;
        foreach (var p in points)
        {
            sum += p;
        }
        return sum / points.Count;
    }

    public void ClearTrajectory()
    {
        currentWandPoints.Clear();
        CurrentWandPosition = Vector2.zero;
        TrackingConfidence = 0f;
    }

    void OnDisable()
    {
        if (frameTexture != null)
        {
            Destroy(frameTexture);
            frameTexture = null;
        }
        if (debugTexture != null)
        {
            Destroy(debugTexture);
            debugTexture = null;
        }
    }
}