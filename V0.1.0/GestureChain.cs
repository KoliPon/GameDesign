using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 手勢識別鏈 V0.2.0 - 支援進階防禦系統
/// - 支援正方形作為防禦
/// - 正方形可單獨使用（不需要揮動確認）
/// - 其他手勢流程保持不變
/// - 整合玩家防禦護盾系統
/// </summary>
public class GestureChain : MonoBehaviour
{
    [System.Serializable]
    public class ComboRecognition
    {
        public string firstGesture;
        public string secondGesture;
        public string resultSpell;

        public ComboRecognition(string first, string second, string result)
        {
            firstGesture = first;
            secondGesture = second;
            resultSpell = result;
        }
    }

    [Header("== 手勢辨識參數 ==")]
    [SerializeField] private OneDollarRecognizer.GestureTemplate[] gestureTemplates;
    [SerializeField] private float gestureConfidenceThreshold = 0.75f;

    [Header("== 組合招式配置 ==")]
    [SerializeField] private List<ComboRecognition> combos = new List<ComboRecognition>();

    [Header("== 向前揮動確認設定 ==")]
    [SerializeField] private float forwardSwipeGThreshold = 150f;
    [SerializeField] private float forwardAxisThreshold = 100f;
    [SerializeField] private float minSwipeDuration = 0.1f;
    [SerializeField] private float maxSwipeDuration = 1.0f;
    [SerializeField] private bool requireDirectionalSwipe = true;

    [Header("== 雙手勢設定 ==")]
    [SerializeField] private float comboTimeout = 3.0f;

    [Header("== 冷卻設定 ==")]
    [SerializeField] private float attackCooldown = 1.5f;

    [Header("== 重力加速度補正 ==")]
    [SerializeField] private bool useAccelerationFilter = true;
    [SerializeField] private float lowPassFilterFactor = 0.3f;

    [Header("== UI 軌跡顯示面板 ==")]
    [SerializeField] private CanvasLineRenderer firstTrajectoryPanel;
    [SerializeField] private CanvasLineRenderer secondTrajectoryPanel;
    [SerializeField] private float trajectoryDisplayDuration = 5.0f;

    [Header("== V0.2.0 防禦設定 ==")]
    [SerializeField] private bool enableDefenseMode = true;
    [SerializeField] private float squareDefenseTimeout = 0.5f;  // 正方形防禦的識別超時

    [Header("== 除錯與 UI ==")]
    [SerializeField] private bool debugMode = true;
    [SerializeField] private TextMeshProUGUI debugText;

    private enum GestureState
    {
        Idle,
        WaitingForCombo,
        WaitingForSwipe,
        Cooldown,
        DefenseMode  // ⭐ V0.2.0 新增
    }

    private GestureState currentState = GestureState.Idle;

    private List<string> gestureSequence = new List<string>();
    private List<Vector2> firstTrajectory = new List<Vector2>();
    private List<Vector2> secondTrajectory = new List<Vector2>();

    private float lastGestureTime = -1f;
    private float lastGValue = 0f;
    private float lastRawGValue = 0f;
    private float swipeDetectionStartTime = -1f;
    private bool isDetectingSwipe = false;
    private float peakGValueDuringSwipe = 0f;

    private float cooldownTimer = 0f;
    private float comboWaitTimer = 0f;

    private Vector3 lastAcceleration = Vector3.zero;
    private Vector3 filteredAcceleration = Vector3.zero;

    private float firstTrajectoryPanelTimer = 0f;
    private float secondTrajectoryPanelTimer = 0f;

    private List<OneDollarRecognizer.GestureTemplate> internalTemplates = new List<OneDollarRecognizer.GestureTemplate>();

    // ⭐ V0.2.0 新增：防禦系統引用
    private PlayerDefenseShield playerDefenseShield;
    private float defenseModeTImer = 0f;

    void Start()
    {
#if !ENABLE_INPUT_SYSTEM
        Input.compensateSensors = true;
#endif

        cooldownTimer = 0f;
        comboWaitTimer = 0f;
        currentState = GestureState.Idle;

        InitializeDefaultCombos();
        InitializeDefaultTemplates();
        HideAllTrajectoryPanels();

        // ⭐ V0.2.0：獲取防禦護盾系統
        playerDefenseShield = FindObjectOfType<PlayerDefenseShield>();
        if (playerDefenseShield == null)
        {
            Debug.LogWarning("[GestureChain V0.2.0] 未找到 PlayerDefenseShield");
        }

        Debug.Log($"✓ 手勢系統初始化完成 [V0.2.0]");
        Debug.Log($"向前揮動確認 G 值: {forwardSwipeGThreshold}");
        Debug.Log($"攻擊冷卻時間: {attackCooldown}s");
        Debug.Log($"防禦模式: {(enableDefenseMode ? "啟用" : "禁用")}");
    }

    void Update()
    {
        UpdateAccelerationWithGravityCompensation();

        if (cooldownTimer > 0)
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer <= 0)
            {
                cooldownTimer = 0f;
                currentState = GestureState.Idle;
                Debug.Log($"✓ 冷卻結束，恢復到 Idle");
            }
        }

        if (comboWaitTimer > 0)
        {
            comboWaitTimer -= Time.deltaTime;
            if (comboWaitTimer <= 0)
            {
                comboWaitTimer = 0f;
                Debug.Log($"[Combo 超時] 自動轉移到等待揮動");
                TransitionToWaitingForSwipe();
            }
        }

        // ⭐ V0.2.0 新增：防禦模式計時
        if (currentState == GestureState.DefenseMode)
        {
            defenseModeTImer += Time.deltaTime;
            if (defenseModeTImer >= squareDefenseTimeout)
            {
                Debug.Log("[防禦模式] 超時，重設為 Idle");
                currentState = GestureState.Idle;
                defenseModeTImer = 0f;
            }
        }

        if (currentState == GestureState.WaitingForCombo || currentState == GestureState.WaitingForSwipe)
        {
            UpdateSwipeDetection();
        }

        UpdateTrajectoryPanelsDisplay();

        if (debugMode && debugText != null)
        {
            debugText.text = $"狀態: {currentState}\n" +
                           $"手勢: {string.Join("-", gestureSequence)}\n" +
                           $"冷卻: {Mathf.Max(0, cooldownTimer):F1}s\n" +
                           $"Combo等待: {Mathf.Max(0, comboWaitTimer):F1}s\n" +
                           $"原始G值: {lastRawGValue:F2}\n" +
                           $"淨G值: {lastGValue:F2}";
        }
    }

    private void UpdateAccelerationWithGravityCompensation()
    {
        Vector3 currentAcceleration = GetAcceleration();
        lastRawGValue = currentAcceleration.magnitude;

        if (!useAccelerationFilter)
        {
            lastGValue = lastRawGValue;
            return;
        }

        filteredAcceleration = Vector3.Lerp(
            filteredAcceleration,
            currentAcceleration,
            lowPassFilterFactor
        );

        Vector3 accelerationDelta = currentAcceleration - lastAcceleration;
        float deltaGValue = accelerationDelta.magnitude;
        lastGValue = deltaGValue;

        lastAcceleration = currentAcceleration;
    }

    private void UpdateSwipeDetection()
    {
        UDPReceiver udpReceiver = UDPReceiver.Instance;
        if (udpReceiver == null) return;

        float gyroY = udpReceiver.GetGyroY();
        float gyroMagnitude = Mathf.Abs(gyroY);

        if (!isDetectingSwipe && gyroMagnitude > forwardAxisThreshold)
        {
            isDetectingSwipe = true;
            swipeDetectionStartTime = Time.time;
            peakGValueDuringSwipe = gyroMagnitude;
            Debug.Log($"[揮動檢測] ✓ 開始 - GY: {gyroY:F2}");
            return;
        }

        if (isDetectingSwipe)
        {
            float swipeDuration = Time.time - swipeDetectionStartTime;
            peakGValueDuringSwipe = Mathf.Max(peakGValueDuringSwipe, gyroMagnitude);

            if (gyroMagnitude < forwardAxisThreshold * 0.3f || swipeDuration > maxSwipeDuration)
            {
                if (swipeDuration >= minSwipeDuration && peakGValueDuringSwipe >= forwardSwipeGThreshold)
                {
                    Debug.Log($"[揮動確認] ✓ 有效 - 持續時間: {swipeDuration:F2}s, 峰值GY: {peakGValueDuringSwipe:F2}");
                    ExecuteSpell();
                }
                else
                {
                    Debug.Log($"[揮動確認] ✗ 無效 - 時間: {swipeDuration:F2}s, 峰值GY: {peakGValueDuringSwipe:F2}");
                }

                isDetectingSwipe = false;
                swipeDetectionStartTime = -1f;
                peakGValueDuringSwipe = 0f;
            }
        }
    }

    private Vector3 GetAcceleration()
    {
#if ENABLE_INPUT_SYSTEM
        if (Accelerometer.current != null)
        {
            return Accelerometer.current.acceleration.ReadValue();
        }
        return Vector3.zero;
#else
        return Input.acceleration;
#endif
    }

    private void UpdateTrajectoryPanelsDisplay()
    {
        if (firstTrajectoryPanelTimer > 0)
        {
            firstTrajectoryPanelTimer -= Time.deltaTime;
            if (firstTrajectoryPanelTimer <= 0)
            {
                HideFirstTrajectoryPanel();
            }
        }

        if (secondTrajectoryPanelTimer > 0)
        {
            secondTrajectoryPanelTimer -= Time.deltaTime;
            if (secondTrajectoryPanelTimer <= 0)
            {
                HideSecondTrajectoryPanel();
            }
        }
    }

    /// <summary>
    /// ⭐ V0.2.0 改進：完整的 RecognizeGesture 方法
    /// 支援正方形防禦直接執行（不需要揮動）
    /// </summary>
    public void RecognizeGesture(List<Vector2> points, List<Vector2> displayPoints = null)
    {
        Debug.Log($"[GestureChain V0.2.0] RecognizeGesture 被呼叫, 狀態: {currentState}");
        Debug.Log($"[GestureChain] points 點數: {points.Count}, displayPoints 點數: {displayPoints?.Count ?? 0}");

        if (currentState == GestureState.Cooldown)
        {
            Debug.Log($"⏳ 仍在冷卻中，無法繪製");
            return;
        }

        if (currentState != GestureState.Idle && currentState != GestureState.WaitingForCombo && currentState != GestureState.DefenseMode)
        {
            Debug.Log($"⚠ 目前狀態 {currentState} 無法辨識手勢");
            return;
        }

        List<OneDollarRecognizer.GestureTemplate> templates = gestureTemplates != null && gestureTemplates.Length > 0 ?
            new List<OneDollarRecognizer.GestureTemplate>(gestureTemplates) :
            internalTemplates;

        if (templates.Count == 0)
        {
            Debug.LogWarning("❌ 未設定手勢範本");
            return;
        }

        Debug.Log($"[GestureChain] 開始辨識，使用 {templates.Count} 個範本");

        string recognizedGesture = OneDollarRecognizer.Classify(
            points,
            templates,
            out float confidence
        );

        Debug.Log($"[GestureChain] 辨識結果: {recognizedGesture} (信度: {confidence * 100f:F1}%)");

        if (recognizedGesture == "None")
        {
            Debug.Log("未能辨識手勢");
            return;
        }

        Debug.Log($"✓ 辨識到: {recognizedGesture}");

        // ⭐ V0.2.0 新增：正方形防禦直接執行
        if (enableDefenseMode && recognizedGesture == "Square")
        {
            Debug.Log("[防禦模式] ★ 玩家施展正方形防禦！");
            ExecuteDefense();
            return;
        }

        List<Vector2> trajectoryToDisplay = displayPoints ?? points;
        Debug.Log($"[GestureChain] 使用顯示軌跡，點數: {trajectoryToDisplay.Count}");

        AddGestureToSequence(recognizedGesture, points, trajectoryToDisplay);
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：執行防禦動作
    /// 正方形防禦直接激活玩家護盾，不需要揮動確認
    /// </summary>
    private void ExecuteDefense()
    {
        if (playerDefenseShield != null)
        {
            playerDefenseShield.ActivateShield();
        }

        BattleManager battleManager = FindObjectOfType<BattleManager>();
        if (battleManager != null)
        {
            battleManager.ReceiveSpellData("Square");
        }

        cooldownTimer = attackCooldown;
        currentState = GestureState.Cooldown;
        Debug.Log($"⏳ 防禦冷卻開始 ({attackCooldown}s)");

        StartCoroutine(ResetDelayed(0.5f));
    }

    private void AddGestureToSequence(string gestureName, List<Vector2> trajectory, List<Vector2> displayTrajectory)
    {
        if (gestureSequence.Count == 0)
        {
            gestureSequence.Add(gestureName);
            firstTrajectory = new List<Vector2>(trajectory);
            lastGestureTime = Time.time;

            ShowFirstTrajectoryPanel(displayTrajectory);

            Debug.Log($"[第一個手勢] {gestureName}");
            Debug.Log($"[狀態轉移] Idle → WaitingForCombo (等待 {comboTimeout}s)");

            currentState = GestureState.WaitingForCombo;
            comboWaitTimer = comboTimeout;
            return;
        }

        if (gestureSequence.Count == 1 && currentState == GestureState.WaitingForCombo)
        {
            float timeSinceLastGesture = Time.time - lastGestureTime;

            if (timeSinceLastGesture <= comboTimeout)
            {
                gestureSequence.Add(gestureName);
                secondTrajectory = new List<Vector2>(trajectory);
                ShowSecondTrajectoryPanel(displayTrajectory);
                comboWaitTimer = 0f;

                Debug.Log($"[第二個手勢] {gestureName} (間隔: {timeSinceLastGesture:F2}s)");
                Debug.Log($"[組合完成] {gestureSequence[0]} + {gestureSequence[1]}");
                Debug.Log($"[狀態轉移] WaitingForCombo → WaitingForSwipe");

                TransitionToWaitingForSwipe();
                return;
            }
        }

        Debug.LogWarning($"⚠ 無法添加手勢 {gestureName}，目前狀態: {currentState}");
    }

    private void TransitionToWaitingForSwipe()
    {
        currentState = GestureState.WaitingForSwipe;
        Debug.Log($"✓ 已準備好，等待揮動確認");
    }

    private void ExecuteSpell()
    {
        if (gestureSequence.Count == 0)
        {
            Debug.Log("⚠ 沒有手勢可執行");
            return;
        }

        string spellToExecute = null;

        foreach (var combo in combos)
        {
            bool matches = false;

            if (gestureSequence.Count == 1 && string.IsNullOrEmpty(combo.secondGesture))
            {
                matches = gestureSequence[0] == combo.firstGesture;
            }
            else if (gestureSequence.Count == 2 && !string.IsNullOrEmpty(combo.secondGesture))
            {
                matches = gestureSequence[0] == combo.firstGesture &&
                         gestureSequence[1] == combo.secondGesture;
            }

            if (matches)
            {
                spellToExecute = combo.resultSpell;
                break;
            }
        }

        if (spellToExecute == null)
        {
            Debug.Log($"✗ 組合未配置: {string.Join("-", gestureSequence)}");
            return;
        }

        Debug.Log($"★ 施展招式: {spellToExecute}");
        BattleManager battleManager = FindObjectOfType<BattleManager>();
        if (battleManager != null)
        {
            battleManager.ReceiveSpellData(spellToExecute);
        }

        cooldownTimer = attackCooldown;
        currentState = GestureState.Cooldown;
        Debug.Log($"⏳ 開始冷卻 ({attackCooldown}s)");

        StartCoroutine(ResetDelayed(0.5f));
    }

    private IEnumerator ResetDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        gestureSequence.Clear();
        firstTrajectory.Clear();
        secondTrajectory.Clear();
        lastGestureTime = -1f;
        HideAllTrajectoryPanels();
        isDetectingSwipe = false;
        defenseModeTImer = 0f;
        Debug.Log("手勢系統已重設");
    }

    private void ShowFirstTrajectoryPanel(List<Vector2> trajectory)
    {
        if (firstTrajectoryPanel == null)
        {
            Debug.LogError("[ERROR] firstTrajectoryPanel 為 null！");
            return;
        }

        if (trajectory.Count < 2)
        {
            Debug.LogError($"[ERROR] 軌跡點數不足: {trajectory.Count}");
            return;
        }

        List<Vector2> uiTrajectory = NormalizeTrajectoryForDisplay(trajectory);
        firstTrajectoryPanel.UpdateLine(uiTrajectory);
        firstTrajectoryPanel.gameObject.SetActive(true);
        firstTrajectoryPanelTimer = trajectoryDisplayDuration;

        Debug.Log($"✅ [UI] 第一個軌跡面板顯示: {trajectory.Count} 個點");
    }

    private void HideFirstTrajectoryPanel()
    {
        if (firstTrajectoryPanel == null) return;
        firstTrajectoryPanel.ClearLine();
        firstTrajectoryPanel.gameObject.SetActive(false);
        firstTrajectoryPanelTimer = 0f;
    }

    private void ShowSecondTrajectoryPanel(List<Vector2> trajectory)
    {
        if (secondTrajectoryPanel == null)
        {
            Debug.LogError("[ERROR] secondTrajectoryPanel 為 null！");
            return;
        }

        if (trajectory.Count < 2)
        {
            Debug.LogError($"[ERROR] 軌跡點數不足: {trajectory.Count}");
            return;
        }

        List<Vector2> uiTrajectory = NormalizeTrajectoryForDisplay(trajectory);
        secondTrajectoryPanel.UpdateLine(uiTrajectory);
        secondTrajectoryPanel.gameObject.SetActive(true);
        secondTrajectoryPanelTimer = trajectoryDisplayDuration;

        Debug.Log($"✅ [UI] 第二個軌跡面板顯示: {trajectory.Count} 個點");
    }

    private void HideSecondTrajectoryPanel()
    {
        if (secondTrajectoryPanel == null) return;
        secondTrajectoryPanel.ClearLine();
        secondTrajectoryPanel.gameObject.SetActive(false);
        secondTrajectoryPanelTimer = 0f;
    }

    private void HideAllTrajectoryPanels()
    {
        HideFirstTrajectoryPanel();
        HideSecondTrajectoryPanel();
    }

    private List<Vector2> NormalizeTrajectoryForDisplay(List<Vector2> trajectory)
    {
        if (trajectory.Count < 2) return new List<Vector2>(trajectory);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in trajectory)
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

        float targetWidth = 50f;
        float targetHeight = 50f;
        float scale = Mathf.Min(targetWidth / currentWidth, targetHeight / currentHeight);
        scale = Mathf.Min(scale, 15f);

        List<Vector2> normalized = new List<Vector2>();
        foreach (var p in trajectory)
        {
            Vector2 centeredPoint = (p - center) * scale;
            centeredPoint.x = Mathf.Clamp(centeredPoint.x, -targetWidth / 2f, targetWidth / 2f);
            centeredPoint.y = Mathf.Clamp(centeredPoint.y, -targetHeight / 2f, targetHeight / 2f);
            normalized.Add(centeredPoint);
        }

        return normalized;
    }

    private void InitializeDefaultTemplates()
    {
        internalTemplates.Clear();

        List<Vector2> circle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64f;
            circle.Add(new Vector2(Mathf.Cos(a) * 50f, Mathf.Sin(a) * 50f));
        }
        internalTemplates.Add(new OneDollarRecognizer.GestureTemplate("Circle", circle));

        List<Vector2> square = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float t = i / 64f;
            if (t < 0.25f) square.Add(new Vector2(Mathf.Lerp(-50, 50, t * 4f), 50));
            else if (t < 0.5f) square.Add(new Vector2(50, Mathf.Lerp(50, -50, (t - 0.25f) * 4f)));
            else if (t < 0.75f) square.Add(new Vector2(Mathf.Lerp(50, -50, (t - 0.5f) * 4f), -50));
            else square.Add(new Vector2(-50, Mathf.Lerp(-50, 50, (t - 0.75f) * 4f)));
        }
        internalTemplates.Add(new OneDollarRecognizer.GestureTemplate("Square", square));

        List<Vector2> triangle = new List<Vector2>();
        for (int i = 0; i < 64; i++)
        {
            float t = i / 64f;
            if (t < 0.33f) triangle.Add(new Vector2(Mathf.Lerp(0, 50, t * 3f), Mathf.Lerp(50, -50, t * 3f)));
            else if (t < 0.66f) triangle.Add(new Vector2(Mathf.Lerp(50, -50, (t - 0.33f) * 3f), -50));
            else triangle.Add(new Vector2(Mathf.Lerp(-50, 0, (t - 0.66f) * 3f), Mathf.Lerp(-50, 50, (t - 0.66f) * 3f)));
        }
        internalTemplates.Add(new OneDollarRecognizer.GestureTemplate("Triangle", triangle));

        Debug.Log($"✓ 已初始化 {internalTemplates.Count} 個內部手勢範本");
    }

    private void InitializeDefaultCombos()
    {
        if (combos.Count == 0)
        {
            combos.Add(new ComboRecognition("Circle", null, "Circle"));
            combos.Add(new ComboRecognition("Square", null, "Square"));
            combos.Add(new ComboRecognition("Triangle", null, "Triangle"));
            combos.Add(new ComboRecognition("Circle", "Square", "CircleSquare"));
            combos.Add(new ComboRecognition("Circle", "Triangle", "CircleTriangle"));
            combos.Add(new ComboRecognition("Square", "Triangle", "SquareTriangle"));
            combos.Add(new ComboRecognition("Square", "Circle", "SquareCircle"));
            combos.Add(new ComboRecognition("Triangle", "Circle", "TriangleCircle"));
            combos.Add(new ComboRecognition("Triangle", "Square", "TriangleSquare"));
        }
    }

    public List<string> GetCurrentGestureSequence() => new List<string>(gestureSequence);
    public float GetCurrentGValue() => lastGValue;

    void OnDestroy() { StopAllCoroutines(); }
}