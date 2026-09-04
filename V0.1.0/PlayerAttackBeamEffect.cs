using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家攻擊光束特效系統 V0.2.0
/// - 玩家施展攻擊時顯示光束
/// - 圓形攻擊：橙色光束
/// - 三角形攻擊：紫色光束
/// - 光束從玩家指向敵人或中心
/// </summary>
public class PlayerAttackBeamEffect : MonoBehaviour
{
    [Header("== 光束特效設定 ==")]
    [SerializeField] private float beamWidth = 25f;
    [SerializeField] private float beamMaxDistance = 800f;
    [SerializeField] private Transform playerPosition;  // 玩家位置（或螢幕中心）

    [Header("== 顏色設定 ==")]
    [SerializeField] private Color circleBeamColor = new Color(1f, 0.6f, 0f, 0.8f);   // 橙色
    [SerializeField] private Color triangleBeamColor = new Color(0.8f, 0f, 1f, 0.8f);  // 紫色

    [Header("== 動畫設定 ==")]
    [SerializeField] private bool enableBeamPulsing = true;
    [SerializeField] private float pulseSpeed = 3f;
    [SerializeField] private float pulseIntensity = 0.4f;
    [SerializeField] private bool enableBeamShaking = true;
    [SerializeField] private float shakeIntensity = 3f;

    [Header("== 持續時間 ==")]
    [SerializeField] private float beamDisplayDuration = 0.5f;

    private Canvas uiCanvas;
    private Transform canvasParent;
    private BeamRenderer activeBeam;

    void Start()
    {
        InitializeComponents();
        Debug.Log($"[玩家攻擊光束] 初始化完成");
    }

    void Update()
    {
        if (activeBeam != null)
        {
            activeBeam.Update();
        }
    }

    private void InitializeComponents()
    {
        uiCanvas = FindObjectOfType<Canvas>();
        if (uiCanvas == null)
        {
            Debug.LogError("[玩家攻擊光束] 找不到 Canvas");
            return;
        }

        canvasParent = uiCanvas.GetComponent<RectTransform>().parent;
        if (canvasParent == null)
        {
            canvasParent = uiCanvas.transform;
        }

        // 如果沒有設定玩家位置，使用螢幕中心
        if (playerPosition == null)
        {
            Debug.Log("[玩家攻擊光束] 未設定玩家位置，將使用螢幕中心");
        }
    }

    /// <summary>
    /// ⭐ 玩家施展攻擊時調用
    /// 由 BattleManager 或 GestureChain 調用
    /// </summary>
    public void CreatePlayerBeam(string attackType)
    {
        // 銷毀現有光束
        if (activeBeam != null)
        {
            activeBeam.Destroy();
        }

        // ⭐ 根據攻擊類型選擇顏色
        Color beamColor = attackType switch
        {
            "Circle" => circleBeamColor,
            "Triangle" => triangleBeamColor,
            _ => circleBeamColor
        };

        activeBeam = new BeamRenderer(
            GetPlayerScreenPosition(),
            beamColor,
            beamWidth,
            beamMaxDistance,
            beamDisplayDuration,
            canvasParent,
            attackType,
            enableBeamPulsing,
            pulseSpeed,
            pulseIntensity,
            enableBeamShaking,
            shakeIntensity
        );

        Debug.Log($"[玩家攻擊光束] ✓ 創建玩家 {attackType} 光束 (顏色: {beamColor})");
    }

    /// <summary>
    /// ⭐ 取得玩家屏幕位置
    /// </summary>
    private Vector2 GetPlayerScreenPosition()
    {
        if (playerPosition != null)
        {
            Vector3 screenPos = RectTransformUtility.WorldToScreenPoint(null, playerPosition.position);
            return new Vector2(screenPos.x, screenPos.y);
        }
        else
        {
            // 使用螢幕中心
            return new Vector2(Screen.width / 2f, Screen.height / 2f);
        }
    }

    void OnDestroy()
    {
        if (activeBeam != null)
        {
            activeBeam.Destroy();
        }
    }

    /// <summary>
    /// ⭐ 內部類別：玩家光束渲染器
    /// </summary>
    public class BeamRenderer
    {
        private Vector2 playerScreenPos;
        private Color baseColor;
        private float beamWidth;
        private float beamMaxDistance;
        private float maxDuration;
        private float elapsedTime;
        private Transform canvasParent;
        private string attackType;

        private bool enableBeamPulsing;
        private float pulseSpeed;
        private float pulseIntensity;
        private bool enableBeamShaking;
        private float shakeIntensity;

        private Image beamImage;
        private RectTransform beamRectTransform;
        private float pulseTimer;
        private Vector2 shakeOffset;

        public BeamRenderer(Vector2 playerPos, Color color, float width, float maxDist,
                          float duration, Transform parent, string type,
                          bool pulse, float pSpeed, float pIntensity,
                          bool shake, float sIntensity)
        {
            playerScreenPos = playerPos;
            baseColor = color;
            beamWidth = width;
            beamMaxDistance = maxDist;
            maxDuration = duration;
            canvasParent = parent;
            attackType = type;
            elapsedTime = 0f;
            pulseTimer = 0f;

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
            GameObject beamObj = new GameObject($"PlayerBeam_{attackType}");
            beamObj.transform.SetParent(canvasParent, false);

            beamImage = beamObj.AddComponent<Image>();
            beamImage.color = baseColor;

            // 使用簡單的矩形或漸變
            beamImage.sprite = CreateBeamSprite();

            beamRectTransform = beamObj.GetComponent<RectTransform>();
            beamRectTransform.anchorMin = Vector2.zero;
            beamRectTransform.anchorMax = Vector2.zero;
            beamRectTransform.pivot = new Vector2(0.5f, 0f);

            // 設定初始尺寸
            beamRectTransform.sizeDelta = new Vector2(beamWidth, 100f);
        }

        /// <summary>
        /// ⭐ 創建光束精靈（帶漸變效果）
        /// </summary>
        private Sprite CreateBeamSprite()
        {
            Texture2D texture = new Texture2D(2, 64, TextureFormat.RGBA32, false);

            // 創建漸變效果（頂部不透明，底部透明）
            for (int y = 0; y < 64; y++)
            {
                float alpha = 1f - (y / 64f);  // 從 1 漸變到 0
                Color pixelColor = new Color(1, 1, 1, alpha);

                texture.SetPixel(0, y, pixelColor);
                texture.SetPixel(1, y, pixelColor);
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 2, 64), Vector2.one * 0.5f);
        }

        /// <summary>
        /// ⭐ 更新光束位置和外觀
        /// </summary>
        public void Update()
        {
            if (beamRectTransform == null)
                return;

            elapsedTime += Time.deltaTime;

            // 檢查是否過期
            if (elapsedTime >= maxDuration)
            {
                Destroy();
                return;
            }

            // 更新光束位置（從玩家位置指向隨機敵人或中心）
            UpdateBeamPosition();

            // 脈衝動畫
            if (enableBeamPulsing)
            {
                UpdatePulseAnimation();
            }

            // 震動效果
            if (enableBeamShaking)
            {
                UpdateShakeEffect();
            }

            // 隨時間淡出
            UpdateFadeOut();
        }

        /// <summary>
        /// ⭐ 更新光束位置
        /// </summary>
        private void UpdateBeamPosition()
        {
            // 選擇隨機方向指向敵人或中心上方
            Vector2 targetPos = GetRandomEnemyOrCenter();
            Vector2 direction = (targetPos - playerScreenPos).normalized;
            float beamLength = Vector2.Distance(playerScreenPos, targetPos);
            beamLength = Mathf.Min(beamLength, beamMaxDistance);

            // 設定位置
            beamRectTransform.anchoredPosition = playerScreenPos + shakeOffset;
            beamRectTransform.sizeDelta = new Vector2(beamWidth, beamLength);

            // 計算旋轉角度
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            beamRectTransform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
        }

        /// <summary>
        /// ⭐ 取得隨機敵人位置或螢幕頂部中心
        /// </summary>
        private Vector2 GetRandomEnemyOrCenter()
        {
            // 查找所有敵人
            EnemyController[] enemies = Object.FindObjectsOfType<EnemyController>();

            if (enemies.Length > 0)
            {
                // 隨機選擇一個敵人
                EnemyController randomEnemy = enemies[Random.Range(0, enemies.Length)];
                Vector3 screenPos = RectTransformUtility.WorldToScreenPoint(null, randomEnemy.transform.position);
                return new Vector2(screenPos.x, screenPos.y);
            }
            else
            {
                // 如果沒有敵人，指向螢幕頂部中心
                return new Vector2(Screen.width / 2f, Screen.height);
            }
        }

        /// <summary>
        /// ⭐ 脈衝動畫
        /// </summary>
        private void UpdatePulseAnimation()
        {
            pulseTimer += Time.deltaTime * pulseSpeed;

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

        /// <summary>
        /// ⭐ 隨時間淡出
        /// </summary>
        private void UpdateFadeOut()
        {
            float fadeAlpha = Mathf.Lerp(1f, 0f, elapsedTime / maxDuration);
            Color fadeColor = baseColor;
            fadeColor.a = baseColor.a * fadeAlpha;

            if (beamImage != null)
            {
                beamImage.color = fadeColor;
            }
        }

        public void Destroy()
        {
            if (beamRectTransform != null)
            {
                Object.Destroy(beamRectTransform.gameObject);
            }
        }

        public int GetProgress() => (int)(elapsedTime / maxDuration * 100);
    }
}