using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// GestureDrawer - 簡化版，只負責輸入檢測和手勢辨識
/// 線條繪製全部交給 UDPReceiver
/// </summary>
public class GestureDrawer : MonoBehaviour
{
    [Header("== 參數 ==")]
    [SerializeField] private bool enableSmoothing = true;
    [SerializeField] private float smoothingFactor = 0.3f;

    private List<Vector2> rawPoints = new List<Vector2>();
    private bool isDrawing = false;
    private UDPReceiver udpReceiver;
    private RectTransform drawingArea;
    private Canvas canvas;

    void Start()
    {
        // ⭐ 取得 UDPReceiver 引用
        udpReceiver = FindObjectOfType<UDPReceiver>();
        if (udpReceiver == null)
        {
            Debug.LogError("❌ 找不到 UDPReceiver！");
            enabled = false;
            return;
        }

        // ⭐ 取得 drawingArea 和 Canvas
        drawingArea = udpReceiver.drawingArea;
        if (drawingArea != null)
        {
            canvas = drawingArea.GetComponentInParent<Canvas>();
        }

        if (drawingArea == null)
        {
            Debug.LogError("❌ UDPReceiver.drawingArea 未設定！");
            enabled = false;
            return;
        }

        Debug.Log("✓ GestureDrawer 初始化完成");
    }

    void Update()
    {
        if (udpReceiver == null || drawingArea == null) return;

#if ENABLE_INPUT_SYSTEM
        HandleInputNew();
#else
        HandleInputOld();
#endif
    }

    private void HandleInputNew()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // 檢查是否在 drawingArea 範圍內
        if (!RectTransformUtility.RectangleContainsScreenPoint(drawingArea, mousePos))
            return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            StartGesture(mousePos);
        }
        else if (mouse.leftButton.isPressed)
        {
            UpdateGesture(mousePos);
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            FinishGesture();
        }
#endif
    }

    private void HandleInputOld()
    {
#if !ENABLE_INPUT_SYSTEM
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(drawingArea, mousePos))
                StartGesture(mousePos);
        }
        else if (Input.GetMouseButton(0))
        {
            Vector2 mousePos = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(drawingArea, mousePos))
                UpdateGesture(mousePos);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            FinishGesture();
        }
#endif
    }

    private void StartGesture(Vector2 screenPos)
    {
        if (isDrawing) return;
        isDrawing = true;
        rawPoints.Clear();
        rawPoints.Add(screenPos);

        // ⭐ 清除 UDPReceiver 的線條
        if (udpReceiver != null)
        {
            udpReceiver.ClearLine();
        }

        Debug.Log("▶ 開始繪製");
    }

    private void UpdateGesture(Vector2 screenPos)
    {
        if (!isDrawing) return;
        rawPoints.Add(screenPos);

        // ⭐ 呼叫 UDPReceiver 繪製線條
        if (udpReceiver != null)
        {
            udpReceiver.AddLinePoint(screenPos);
        }
    }

    private void FinishGesture()
    {
        if (!isDrawing) return;
        isDrawing = false;

        Debug.Log($"⏹ 完成繪製 ({rawPoints.Count} 個點)");

        List<Vector2> trajectory = udpReceiver != null ?
            udpReceiver.GetCurrentTrajectory() :
            rawPoints;

        Debug.Log($"[DEBUG] trajectory 點數: {trajectory.Count}");

        if (trajectory.Count >= 5)
        {
            Debug.Log($"[DEBUG] 點數足夠，開始平滑化");

            List<Vector2> smoothedPoints = enableSmoothing ?
                SmoothPoints(trajectory) :
                trajectory;

            Debug.Log($"[DEBUG] 平滑化後: {smoothedPoints.Count} 個點");

            // ⭐ 傳送給 GestureChain
            GestureChain gestureChain = FindObjectOfType<GestureChain>();

            Debug.Log($"[DEBUG] GestureChain 是否找到: {gestureChain != null}");

            if (gestureChain != null)
            {
                Debug.Log($"[DEBUG] 即將呼叫 RecognizeGesture");
                // ⭐ 改這裡：傳送 smoothedPoints 和 trajectory（原始軌跡用於顯示）
                gestureChain.RecognizeGesture(smoothedPoints, trajectory);
                Debug.Log($"✓ 已傳送 {trajectory.Count} 個點給 GestureChain");

                StartCoroutine(ClearLineDelayed(3.0f));
                return;
            }
            else
            {
                Debug.LogError("[ERROR] 找不到 GestureChain!");
            }
        }
        else
        {
            Debug.Log($"⚠ 點數不足 ({trajectory.Count} < 5)");
        }

        // 立即清除（如果失敗或點數不足）
        if (udpReceiver != null)
            udpReceiver.ClearLine();
    }

    private System.Collections.IEnumerator ClearLineDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (udpReceiver != null)
            udpReceiver.ClearLine();
    }

    private List<Vector2> SmoothPoints(List<Vector2> points)
    {
        if (points.Count < 3) return new List<Vector2>(points);

        List<Vector2> smoothed = new List<Vector2>();
        smoothed.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 sp = (points[i - 1] + points[i] * 2 + points[i + 1]) / 4f;
            smoothed.Add(Vector2.Lerp(points[i], sp, smoothingFactor));
        }

        smoothed.Add(points[points.Count - 1]);
        return smoothed;
    }
}