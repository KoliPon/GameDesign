using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 改進的戰鬥管理器 V0.2.0 - 支援進階敵人系統
/// 協調多個敵人與單個玩家的戰鬥
/// - 整合玩家護盾系統
/// - 整合敵人光束特效
/// - 整合敵人打斷/僵直系統
/// - 支援手勢反轉邏輯
/// </summary>
public class BattleManager : MonoBehaviour
{
    private static BattleManager instance;
    public static BattleManager Instance => instance;

    [Header("== 血條與冷卻條連動 ==")]
    public Image playerHPBarFill;
    public Image playerCDBarFill;

    [Header("== 玩家戰鬥屬性 ==")]
    [SerializeField] private int maxPlayerShields = 3;

    [Header("== 敵人管理 ==")]
    [SerializeField] private EnemyController[] enemies;
    private Dictionary<int, EnemyController> enemyDict = new Dictionary<int, EnemyController>();
    private int defeatedEnemyCount = 0;

    [Header("== 玩家單招特效 Prefabs ==")]
    public GameObject fxCirclePrefab;
    public GameObject fxSquarePrefab;
    public GameObject fxTrianglePrefab;

    [Header("== 玩家連體技特效 Prefabs ==")]
    [SerializeField] private GameObject fxCircleCombo;
    [SerializeField] private GameObject fxSquareCombo;
    [SerializeField] private GameObject fxTriangleCombo;

    [SerializeField] private Transform canvasParent;

    [Header("== 玩家角落浮空圖騰 UI ==")]
    public Image playerSpellFlashImage;
    public Sprite iconCircleSprite;
    public Sprite iconSquareSprite;
    public Sprite iconTriangleSprite;
    [SerializeField] private float flashFadeDuration = 0.6f;

    [Header("== 玩家 UI 連動與特效 ==")]
    public TMP_Text playerActiveSpellText;
    public Image playerCooldownOverlay;
    public ShieldFlashUi playerShieldJuice;

    [Header("== 戰鬥時間設定 ==")]
    [SerializeField] private float spellCooldownDuration = 0.8f;

    [Header("== V0.2.0 手勢邏輯反轉設定 ==")]
    [SerializeField] private bool enableGestureReversal = true;

    private int playerShields;
    private bool isPlayerInCooldown = false;
    private Coroutine spellFlashCoroutine;

    // ⭐ V0.2.0 新增系統引用
    private PlayerDefenseShield playerDefenseShield;
    private EnemyEffectManager enemyEffectManager;
    private PlayerAttackBeamEffect playerAttackBeamEffect;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void Start()
    {
        if (canvasParent == null) canvasParent = GameObject.Find("Canvas")?.transform;
        if (playerCooldownOverlay != null) playerCooldownOverlay.fillAmount = 0f;
        if (playerCDBarFill != null) playerCDBarFill.fillAmount = 0f;

        if (playerActiveSpellText != null) playerActiveSpellText.text = "準備對決...";
        if (playerSpellFlashImage != null) playerSpellFlashImage.gameObject.SetActive(false);

        playerShields = maxPlayerShields;
        if (playerHPBarFill != null) playerHPBarFill.fillAmount = 1f;

        // ⭐ V0.2.0 初始化新系統
        InitializeV0_2_0Systems();

        // 初始化敵人
        InitializeEnemies();
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：初始化玩家護盾和敵人特效系統
    /// </summary>
    private void InitializeV0_2_0Systems()
    {
        // 獲取或創建玩家防禦護盾
        playerDefenseShield = FindObjectOfType<PlayerDefenseShield>();
        if (playerDefenseShield == null)
        {
            Debug.LogWarning("[BattleManager V0.2.0] 未找到 PlayerDefenseShield，正在創建...");
            GameObject shieldObj = new GameObject("PlayerDefenseShield");
            shieldObj.transform.SetParent(transform);
            playerDefenseShield = shieldObj.AddComponent<PlayerDefenseShield>();
        }

        // 獲取或創建敵人特效管理器
        enemyEffectManager = FindObjectOfType<EnemyEffectManager>();
        if (enemyEffectManager == null)
        {
            Debug.LogWarning("[BattleManager V0.2.0] 未找到 EnemyEffectManager，正在創建...");
            GameObject effectObj = new GameObject("EnemyEffectManager");
            effectObj.transform.SetParent(transform);
            enemyEffectManager = effectObj.AddComponent<EnemyEffectManager>();
        }

        // ⭐ 新增：獲取或創建玩家攻擊光束系統
        playerAttackBeamEffect = FindObjectOfType<PlayerAttackBeamEffect>();
        if (playerAttackBeamEffect == null)
        {
            Debug.LogWarning("[BattleManager V0.2.0] 未找到 PlayerAttackBeamEffect，正在創建...");
            GameObject beamObj = new GameObject("PlayerAttackBeamEffect");
            beamObj.transform.SetParent(transform);
            playerAttackBeamEffect = beamObj.AddComponent<PlayerAttackBeamEffect>();
        }

        Debug.Log("[BattleManager V0.2.0] ✓ 新系統初始化完成");
        Debug.Log($"  - 玩家防禦護盾: {(playerDefenseShield != null ? "✓" : "✗")}");
        Debug.Log($"  - 敵人特效管理器: {(enemyEffectManager != null ? "✓" : "✗")}");
        Debug.Log($"  - 玩家攻擊光束: {(playerAttackBeamEffect != null ? "✓" : "✗")}");  // ⭐ 新增
        Debug.Log($"  - 手勢邏輯反轉: {(enableGestureReversal ? "啟用" : "禁用")}");
    }

    /// <summary>
    /// 初始化所有敵人並綁定事件
    /// </summary>
    private void InitializeEnemies()
    {
        if (enemies == null || enemies.Length == 0)
        {
            enemies = FindObjectsOfType<EnemyController>();
            Debug.Log($"自動查找到 {enemies.Length} 個敵人");
        }

        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i].OnEnemyChargeStart += HandleEnemyChargeStart;
            enemies[i].OnEnemyCharged += HandleEnemyCharged;
            enemies[i].OnEnemyDefeated += HandleEnemyDefeated;
            enemies[i].OnEnemyStunned += HandleEnemyStunned;

            int enemyID = enemies[i].GetEnemyID();
            enemyDict[enemyID] = enemies[i];
        }

        Debug.Log($"已初始化 {enemies.Length} 個敵人");
    }

    /// <summary>
    /// 敵人開始出招 - 事件回呼
    /// </summary>
    private void HandleEnemyChargeStart(int enemyID)
    {
        Debug.Log($"[BattleManager] 敵人 {enemyID} 開始出招");

        // ⭐ V0.2.0：創建敵人光束特效
        if (enemyDict.ContainsKey(enemyID))
        {
            EnemyController enemy = enemyDict[enemyID];
            string attackType = "Circle"; // 預設值，實際應該從敵人系統獲取

            if (enemyEffectManager != null)
            {
                enemyEffectManager.CreateEnemyBeam(enemyID, enemy.transform, attackType, 3.5f);
            }
        }
    }

    /// <summary>
    /// 敵人成功攻擊 - 事件回呼
    /// </summary>
    private void HandleEnemyCharged(int enemyID)
    {
        if (playerShields <= 0) return;

        Debug.Log($"[BattleManager] 敵人 {enemyID} 嘗試攻擊");

        // ⭐ V0.2.0：先檢查玩家護盾是否能擋下
        if (playerDefenseShield != null && playerDefenseShield.IsShieldActive())
        {
            bool blocked = playerDefenseShield.TryBlockDamage();
            if (blocked)
            {
                Debug.Log($"[BattleManager] ✓ 玩家護盾成功擋下敵人 {enemyID} 的攻擊！");
                return;
            }
        }

        // 沒有護盾或護盾失效，則玩家受傷
        playerShields--;
        Debug.Log($"[BattleManager] 敵人 {enemyID} 成功攻擊！玩家剩餘盾牌: {playerShields}");

        if (playerHPBarFill != null)
        {
            playerHPBarFill.fillAmount = (float)playerShields / maxPlayerShields;
        }

        if (playerShieldJuice != null)
        {
            playerShieldJuice.PlayShieldFlash();
        }

        if (playerShields <= 0)
        {
            TriggerPlayerDefeat();
        }
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：敵人進入僵直狀態事件
    /// </summary>
    private void HandleEnemyStunned(int enemyID, float duration)
    {
        Debug.Log($"[BattleManager] 敵人 {enemyID} 進入僵直狀態，持續 {duration}s");

        // 銷毀敵人光束特效
        if (enemyEffectManager != null)
        {
            enemyEffectManager.DestroyEnemyBeam(enemyID);
        }
    }

    /// <summary>
    /// 敵人被擊敗 - 事件回呼
    /// </summary>
    private void HandleEnemyDefeated(int enemyID)
    {
        defeatedEnemyCount++;
        Debug.Log($"[BattleManager] 敵人 {enemyID} 被擊敗 ({defeatedEnemyCount}/{enemies.Length})");

        // ⭐ V0.2.0：銷毀敵人光束特效
        if (enemyEffectManager != null)
        {
            enemyEffectManager.DestroyEnemyBeam(enemyID);
        }

        if (defeatedEnemyCount >= enemies.Length)
        {
            TriggerVictory();
        }
    }

    /// <summary>
    /// ⭐ V0.2.0 改進：接收玩家施展的招式
    /// 支援正方形作為防禦護盾
    /// 支援手勢邏輯反轉
    /// </summary>
    public void ReceiveSpellData(string spellName)
    {
        if (playerShields <= 0 || defeatedEnemyCount >= enemies.Length || string.IsNullOrEmpty(spellName))
            return;

        spellName = spellName.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (spellName == "None") return;

        if (isPlayerInCooldown) return;

        // ⭐ V0.2.0：正方形改為防禦護盾
        if (spellName.Contains("Square"))
        {
            Debug.Log("[BattleManager] ★ 玩家施展護盾防禦！");
            playerDefenseShield.ActivateShield();

            // 播放護盾視覺特效
            if (playerActiveSpellText != null)
                playerActiveSpellText.text = "施展防禦護盾！";

            StartCoroutine(SpellCooldownAnimationLoop());
            return;
        }

        // ⭐ V0.2.0：手勢邏輯反轉
        if (enableGestureReversal)
        {
            spellName = ReverseGestureLogic(spellName);
            Debug.Log($"[BattleManager] 手勢已反轉: {spellName}");
        }

        string firstGesture = ExtractFirstGestureFromCombo(spellName);

        bool isCombo = spellName.Length > firstGesture.Length;
        string secondGesture = isCombo ? spellName.Substring(firstGesture.Length) : "None";

        Debug.Log($"========== [招式判定結果] ==========");
        Debug.Log($"原始招式名稱: {spellName}");
        Debug.Log($"招式類型: {(isCombo ? "連體技" : "單招")}");
        Debug.Log($"第一個圖形: {firstGesture}");
        if (isCombo)
        {
            Debug.Log($"第二個圖形: {secondGesture}");
        }
        Debug.Log($"使用特效: {GetFxPrefabForSpell(firstGesture)?.name ?? "無特效"}");
        Debug.Log($"====================================");

        TriggerSpellFlash(firstGesture);

        if (playerActiveSpellText != null) playerActiveSpellText.text = $"目前施展：{spellName}";

        GameObject activeFxPrefab = GetFxPrefabForSpell(firstGesture);
        StartCoroutine(SpellCooldownAnimationLoop());

        if (activeFxPrefab != null)
        {
            GameObject fx = Instantiate(activeFxPrefab, canvasParent != null ? canvasParent : transform);
            fx.transform.localPosition = Vector3.zero;
            ParticleSystemRenderer psr = fx.GetComponent<ParticleSystemRenderer>();
            if (psr != null) { psr.sortingLayerName = "UI"; psr.sortingOrder = 1000; }
            Destroy(fx, spellCooldownDuration);

            Debug.Log($"[特效播放] 根據第一個圖形 '{firstGesture}' 播放特效 (招式: {spellName})");
        }
        else
        {
            Debug.LogWarning($"[警告] 找不到特效 Prefab for {firstGesture}");
        }

        if (playerAttackBeamEffect != null && !spellName.Contains("Square"))
        {
            playerAttackBeamEffect.CreatePlayerBeam(firstGesture);
            Debug.Log($"[玩家光束] 創建 {firstGesture} 光束");
        }

        // 🎯 檢查所有正在出招的敵人
        foreach (var enemy in enemies)
        {
            if (enemy.IsCharging() && !enemy.IsStunned())
            {
                if (enemy.TakeSpellDamage(spellName))
                {
                    Debug.Log($"玩家成功用 {spellName} 打斷敵人 {enemy.GetEnemyID()} 的攻擊！");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：手勢邏輯反轉
    /// 敵人用圓形 → 玩家用三角形
    /// 敵人用三角形 → 玩家用圓形
    /// 正方形保持不變
    /// </summary>
    private string ReverseGestureLogic(string originalSpell)
    {
        // 單個手勢反轉
        if (originalSpell == "Circle")
            return "Triangle";
        if (originalSpell == "Triangle")
            return "Circle";
        if (originalSpell == "Square")
            return "Square";

        // 組合手勢反轉
        if (originalSpell.Contains("CircleSquare"))
            return "TriangleSquare";
        if (originalSpell.Contains("TriangleSquare"))
            return "CircleSquare";
        if (originalSpell.Contains("CircleTriangle"))
            return "TriangleCircle";
        if (originalSpell.Contains("TriangleCircle"))
            return "CircleTriangle";
        if (originalSpell.Contains("SquareCircle"))
            return "SquareTriangle";
        if (originalSpell.Contains("SquareTriangle"))
            return "SquareCircle";

        return originalSpell; // 預設不改變
    }

    /// <summary>
    /// 從組合招式中提取第一個基礎招式
    /// </summary>
    private string ExtractFirstGestureFromCombo(string comboSpell)
    {
        if (comboSpell.StartsWith("Circle")) return "Circle";
        if (comboSpell.StartsWith("Square")) return "Square";
        if (comboSpell.StartsWith("Triangle")) return "Triangle";
        return comboSpell;
    }

    /// <summary>
    /// 根據第一個招式取得特效
    /// </summary>
    private GameObject GetFxPrefabForSpell(string firstGesture)
    {
        return firstGesture switch
        {
            "Circle" => fxCirclePrefab,
            "Square" => fxSquarePrefab,
            "Triangle" => fxTrianglePrefab,
            _ => null
        };
    }

    private void TriggerSpellFlash(string spellName)
    {
        if (playerSpellFlashImage == null) return;

        playerSpellFlashImage.sprite = spellName switch
        {
            "Circle" => iconCircleSprite,
            "Square" => iconSquareSprite,
            "Triangle" => iconTriangleSprite,
            _ => playerSpellFlashImage.sprite
        };

        if (spellFlashCoroutine != null) StopCoroutine(spellFlashCoroutine);
        spellFlashCoroutine = StartCoroutine(SpellFlashFadeEffect());
    }

    IEnumerator SpellFlashFadeEffect()
    {
        playerSpellFlashImage.gameObject.SetActive(true);
        float elapsed = 0f;
        Color baseColor = Color.white;

        while (elapsed < flashFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / flashFadeDuration;
            playerSpellFlashImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(1f, 0f, t));
            yield return null;
        }

        playerSpellFlashImage.gameObject.SetActive(false);
        spellFlashCoroutine = null;
    }

    private void TriggerPlayerDefeat()
    {
        if (playerActiveSpellText != null) playerActiveSpellText.text = "戰敗...請重新挑戰！";
        Debug.Log("玩家戰敗！");
    }

    private void TriggerVictory()
    {
        if (playerActiveSpellText != null) playerActiveSpellText.text = "戰勝！恭喜獲得勝利！";
        Debug.Log("玩家戰勝所有敵人！");
    }

    IEnumerator SpellCooldownAnimationLoop()
    {
        isPlayerInCooldown = true;
        float elapsed = 0f;

        if (playerCooldownOverlay != null) playerCooldownOverlay.fillAmount = 1f;
        if (playerCDBarFill != null) playerCDBarFill.fillAmount = 1f;

        while (elapsed < spellCooldownDuration)
        {
            elapsed += Time.deltaTime;
            float progress = 1f - (elapsed / spellCooldownDuration);

            if (playerCooldownOverlay != null) playerCooldownOverlay.fillAmount = progress;
            if (playerCDBarFill != null) playerCDBarFill.fillAmount = progress;

            yield return null;
        }

        if (playerCooldownOverlay != null) playerCooldownOverlay.fillAmount = 0f;
        if (playerCDBarFill != null) playerCDBarFill.fillAmount = 0f;

        isPlayerInCooldown = false;
    }

    public void StartBattleFromTutorial()
    {
        playerActiveSpellText.text = "對決開始！請準備迎擊！";
        defeatedEnemyCount = 0;
        playerShields = maxPlayerShields;
    }

    void OnDestroy() { StopAllCoroutines(); }
}