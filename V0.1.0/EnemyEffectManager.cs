using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敵人攻擊光束特效管理器 V0.2.0 - 修正版
/// - 敵人圓形攻擊：橙色光束
/// - 敵人三角形攻擊：紫色光束
/// - 光束從敵人指向玩家
/// - 支援多敵人同時攻擊
/// </summary>
public class EnemyEffectManager : MonoBehaviour
{
    [Header("== 光束特效設定 ==")]
    [SerializeField] private float beamWidth = 20f;            // 光束寬度
    [SerializeField] private float beamStartDistance = 50f;    // 光束起始距離
    [SerializeField] private float beamMaxDistance = 1000f;    // 光束最大距離

    [Header("== 顏色設定 ==")]
    [SerializeField] private Color circleBeamColor = new Color(1f, 0.6f, 0f, 0.8f);  // 橙色
    [SerializeField] private Color triangleBeamColor = new Color(0.8f, 0f, 1f, 0.8f); // 紫色
    [SerializeField] private Color shieldBeamColor = new Color(0.7f, 0.7f, 0.7f, 0.6f); // 灰色（護盾）

    [Header("== 動畫設定 ==")]
    [SerializeField] private bool enableBeamPulsing = true;
    [SerializeField] private float pulseSpeed = 3f;
    [SerializeField] private float pulseIntensity = 0.4f;
    [SerializeField] private bool enableBeamShaking = true;
    [SerializeField] private float shakeIntensity = 5f;

    [Header("== 粒子特效設定 ==")]
    [SerializeField] private GameObject beamImpactParticlePrefab;
    [SerializeField] private float particleScale = 1f;

    private Dictionary<int, BeamRenderer> activeBeams = new Dictionary<int, BeamRenderer>();
    private Canvas uiCanvas;
    private Transform canvasParent;

    void Start()
    {
        InitializeComponents();
        Debug.Log($"[敵人光束特效] 初始化完成");
    }

    void Update()
    {
        UpdateAllBeams();
    }

    private void InitializeComponents()
    {
        uiCanvas = FindObjectOfType<Canvas>();
        if (uiCanvas == null)
        {
            Debug.LogError("[敵人光束特效] 找不到 Canvas");
            return;
        }

        canvasParent = uiCanvas.transform;
    }

    /// <summary>
    /// ⭐ 創建敵人攻擊光束
    /// 由 EnemyController 在出招時調用
    /// </summary>
    public void CreateEnemyBeam(int enemyID, Transform enemyTransform, string attackType, float duration)
    {
        if (enemyTransform == null)
        {
            Debug.LogWarning($"[敵人光束特效] 敵人 {enemyID} 的 Transform 為空");
            return;
        }

        // 檢查是否已有該敵人的光束
        if (activeBeams.ContainsKey(enemyID))
        {
            DestroyEnemyBeam(enemyID);
        }

        // ⭐ 根據攻擊類型選擇顏色
        Color beamColor = attackType switch
        {
            "Circle" => circleBeamColor,
            "Triangle" => triangleBeamColor,
            "Shield" => shieldBeamColor,
            _ => circleBeamColor
        };

        BeamRenderer beamRenderer = new BeamRenderer(
            enemyID,
            enemyTransform,
            beamColor,
            beamWidth,
            duration,
            canvasParent,
            attackType,
            beamMaxDistance,
            enableBeamPulsing,
            pulseSpeed,
            pulseIntensity,
            enableBeamShaking,
            shakeIntensity
        );

        activeBeams[enemyID] = beamRenderer;

        Debug.Log($"[敵人光束特效] ✓ 創建敵人 {enemyID} 的 {attackType} 光束 (顏色: {beamColor})");
    }

    /// <summary>
    /// ⭐ 銷毀敵人光束
    /// </summary>
    public void DestroyEnemyBeam(int enemyID)
    {
        if (activeBeams.ContainsKey(enemyID))
        {
            activeBeams[enemyID].Destroy();
            activeBeams.Remove(enemyID);
            Debug.Log($"[敵人光束特效] ✗ 銷毀敵人 {enemyID} 的光束");
        }
    }

    /// <summary>
    /// ⭐ 更新所有活躍的光束
    /// </summary>
    private void UpdateAllBeams()
    {
        List<int> beamsToRemove = new List<int>();

        foreach (var kvp in activeBeams)
        {
            int enemyID = kvp.Key;
            BeamRenderer beam = kvp.Value;

            if (beam.Update())
            {
                beamsToRemove.Add(enemyID);
            }
        }

        // 移除已過期的光束
        foreach (int enemyID in beamsToRemove)
        {
            DestroyEnemyBeam(enemyID);
        }
    }

    public Dictionary<int, BeamRenderer> GetActiveBeams() => new Dictionary<int, BeamRenderer>(activeBeams);

    void OnDestroy()
    {
        foreach (var beam in activeBeams.Values)
        {
            beam.Destroy();
        }
        activeBeams.Clear();
    }

    /// <summary>
    /// ⭐ 內部類別：光束渲染器
    /// </summary>
    public class BeamRenderer
    {
        private int enemyID;
        private Transform enemyTransform;
        private Color baseColor;
        private float beamWidth;
        private float maxDuration;
        private float elapsedTime;
        private Transform canvasParent;
        private string attackType;

        // ⭐ 修正：將參數改為本地變數
        private float beamMaxDistance;
        private bool enableBeamPulsing;
        private float pulseSpeed;
        private float pulseIntensity;
        private bool enableBeamShaking;
        private float shakeIntensity;

        private Image beamImage;
        private RectTransform beamRectTransform;
        private float pulseTimer;
        private Vector2 shakeOffset;

        public BeamRenderer(int id, Transform transform, Color color, float width,
                          float duration, Transform parent, string type,
                          float maxDist, bool pulse, float pSpeed, float pIntensity,
                          bool shake, float sIntensity)
        {
            enemyID = id;
            enemyTransform = transform;
            baseColor = color;
            beamWidth = width;
            maxDuration = duration;
            canvasParent = parent;
            attackType = type;
            elapsedTime = 0f;
            pulseTimer = 0f;

            // ⭐ 初始化動畫參數
            beamMaxDistance = maxDist;
            enableBeamPulsing = pulse;
            pulseSpeed = pSpeed;
            pulseIntensity = pIntensity;
            enableBeamShaking = shake;
            shakeIntensity = sIntensity;

            CreateBeamVisual();
        }

        /// <summary>
        /// ⭐ 創建光束視覺
        /// </summary>
        private void CreateBeamVisual()
        {
            GameObject beamObj = new GameObject($"EnemyBeam_Enemy{enemyID}_{attackType}");
            beamObj.transform.SetParent(canvasParent, false);

            beamImage = beamObj.AddComponent<Image>();
            beamImage.color = baseColor;

            // 設定為簡單的矩形
            beamImage.sprite = Resources.Load<Sprite>("UI/DefaultSprite") ?? CreateDefaultSprite();

            beamRectTransform = beamObj.GetComponent<RectTransform>();
            beamRectTransform.anchorMin = Vector2.zero;
            beamRectTransform.anchorMax = Vector2.zero;
            beamRectTransform.pivot = Vector2.zero;

            // 設定初始尺寸
            beamRectTransform.sizeDelta = new Vector2(beamWidth, 100f);
        }

        /// <summary>
        /// ⭐ 建立預設的白色精靈
        /// </summary>
        private Sprite CreateDefaultSprite()
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.white);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        }

        /// <summary>
        /// ⭐ 更新光束位置和外觀
        /// 返回 true 表示光束已過期
        /// </summary>
        public bool Update()
        {
            if (enemyTransform == null || beamRectTransform == null)
                return true;

            elapsedTime += Time.deltaTime;

            // ⭐ 檢查是否過期
            if (elapsedTime >= maxDuration)
            {
                return true;
            }

            // ⭐ 更新光束位置（從敵人指向中心）
            UpdateBeamPosition();

            // ⭐ 脈衝動畫
            if (enableBeamPulsing)
            {
                UpdatePulseAnimation();
            }

            // ⭐ 震動效果
            if (enableBeamShaking)
            {
                UpdateShakeEffect();
            }

            return false;
        }

        /// <summary>
        /// ⭐ 更新光束位置
        /// </summary>
        private void UpdateBeamPosition()
        {
            // 敵人屏幕座標
            Vector3 enemyScreenPos = RectTransformUtility.WorldToScreenPoint(null, enemyTransform.position);

            // 計算光束長度（從敵人到畫布中心）
            Vector2 canvasCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Vector2 direction = (canvasCenter - new Vector2(enemyScreenPos.x, enemyScreenPos.y)).normalized;
            float beamLength = Vector2.Distance(new Vector2(enemyScreenPos.x, enemyScreenPos.y), canvasCenter);

            // 限制光束長度
            beamLength = Mathf.Min(beamLength, beamMaxDistance);

            // 設定光束位置和旋轉
            beamRectTransform.anchoredPosition = new Vector2(enemyScreenPos.x, enemyScreenPos.y) + shakeOffset;
            beamRectTransform.sizeDelta = new Vector2(beamWidth, beamLength);

            // 計算旋轉角度
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            beamRectTransform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
        }

        /// <summary>
        /// ⭐ 脈衝動畫
        /// </summary>
        private void UpdatePulseAnimation()
        {
            pulseTimer += Time.deltaTime * pulseSpeed;

            // 正弦波脈衝
            float pulseValue = Mathf.Sin(pulseTimer * Mathf.PI) * pulseIntensity;
            Color pulsedColor = baseColor;
            pulsedColor.a = Mathf.Clamp01(baseColor.a + pulseValue);

            if (beamImage != null)
            {
                beamImage.color = pulsedColor;
            }

            if (pulseTimer > 1f)
            {
                pulseTimer = 0f;
            }
        }

        /// <summary>
        /// ⭐ 震動效果
        /// </summary>
        private void UpdateShakeEffect()
        {
            shakeOffset = Random.insideUnitCircle * shakeIntensity;
        }

        public void Destroy()
        {
            if (beamRectTransform != null)
            {
                Object.Destroy(beamRectTransform.gameObject);
            }
        }

        // Getters
        public int GetEnemyID() => enemyID;
        public string GetAttackType() => attackType;
        public float GetProgress() => elapsedTime / maxDuration;
    }
}