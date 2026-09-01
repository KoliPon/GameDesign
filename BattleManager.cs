using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 改進的戰鬥管理器 - 支援多敵人
/// 協調多個敵人與單個玩家的戰鬥
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
    [SerializeField] private EnemyController[] enemies;  // 場景中的敵人陣列
    private Dictionary<int, EnemyController> enemyDict = new Dictionary<int, EnemyController>();
    private int defeatedEnemyCount = 0;

    [Header("== 玩家單招特效 Prefabs ==")]
    public GameObject fxCirclePrefab;
    public GameObject fxSquarePrefab;
    public GameObject fxTrianglePrefab;

    [Header("== 玩家連體技特效 Prefabs ==")]
    [SerializeField] private GameObject fxCircleCombo;      // ⭐ Circle 開頭的連體技
    [SerializeField] private GameObject fxSquareCombo;      // ⭐ Square 開頭的連體技
    [SerializeField] private GameObject fxTriangleCombo;    // ⭐ Triangle 開頭的連體技

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

    private int playerShields;
    private bool isPlayerInCooldown = false;
    private Coroutine spellFlashCoroutine;

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

        // 初始化敵人
        InitializeEnemies();
    }

    /// <summary>
    /// 初始化所有敵人並綁定事件
    /// </summary>
    private void InitializeEnemies()
    {
        // 如果 enemies 陣列沒有設定，則自動查找場景中的所有 EnemyController
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
    }

    /// <summary>
    /// 敵人成功攻擊 - 事件回呼
    /// </summary>
    private void HandleEnemyCharged(int enemyID)
    {
        if (playerShields <= 0) return;  // 玩家已被擊敗

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
    /// 敵人被擊敗 - 事件回呼
    /// </summary>
    private void HandleEnemyDefeated(int enemyID)
    {
        defeatedEnemyCount++;
        Debug.Log($"[BattleManager] 敵人 {enemyID} 被擊敗 ({defeatedEnemyCount}/{enemies.Length})");

        if (defeatedEnemyCount >= enemies.Length)
        {
            TriggerVictory();
        }
    }

    /// <summary>
    /// 接收玩家施展的招式
    /// 支援單個或組合招式（如 "Circle" 或 "CircleSquare"）
    /// </summary>
    public void ReceiveSpellData(string spellName)
    {
        if (playerShields <= 0 || defeatedEnemyCount >= enemies.Length || string.IsNullOrEmpty(spellName))
            return;

        spellName = spellName.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (spellName == "None") return;

        if (isPlayerInCooldown) return;

        // ⭐ 提取第一個基礎招式用於 UI 和特效
        string firstGesture = ExtractFirstGestureFromCombo(spellName);

        // ⭐ 新增：判定是單招還是連體技
        bool isCombo = spellName.Length > firstGesture.Length;
        string secondGesture = isCombo ? spellName.Substring(firstGesture.Length) : "None";

        // ⭐ 詳細的形狀判定 LOG
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

        // ⭐ 根據第一個圖形決定特效（單招或連體技都用相同特效）
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

        // 🎯 檢查所有正在出招的敵人
        foreach (var enemy in enemies)
        {
            if (enemy.IsCharging())
            {
                if (enemy.TakeSpellDamage(spellName))
                {
                    Debug.Log($"玩家成功用 {spellName} 打斷敵人 {enemy.GetEnemyID()} 的攻擊！");
                    break;  // 只有第一個被打斷的敵人生效
                }
            }
        }
    }

    /// <summary>
    /// 從組合招式中提取第一個基礎招式
    /// 例如："CircleSquare" → "Circle"
    /// </summary>
    private string ExtractFirstGestureFromCombo(string comboSpell)
    {
        if (comboSpell.StartsWith("Circle")) return "Circle";
        if (comboSpell.StartsWith("Square")) return "Square";
        if (comboSpell.StartsWith("Triangle")) return "Triangle";
        return comboSpell;
    }

    /// <summary>
    /// ⭐ 改進：根據第一個招式取得特效（不分單招/連體技）
    /// 單招和連體技使用相同的特效
    /// </summary>
    private GameObject GetFxPrefabForSpell(string firstGesture)
    {
        return firstGesture switch
        {
            "Circle" => fxCirclePrefab,      // Circle 單招或 Circle 開頭的連體技
            "Square" => fxSquarePrefab,      // Square 單招或 Square 開頭的連體技
            "Triangle" => fxTrianglePrefab,  // Triangle 單招或 Triangle 開頭的連體技
            _ => null
        };
    }

    /// <summary>
    /// 根據第一個招式取得連體技特效（可選，目前未使用）
    /// 如果要為連體技設定獨立的特效，可以呼叫這個方法
    /// </summary>
    private GameObject GetComboFxPrefabForSpell(string firstGesture)
    {
        return firstGesture switch
        {
            "Circle" => fxCircleCombo,      // ⭐ Circle 開頭的連體技特效
            "Square" => fxSquareCombo,      // ⭐ Square 開頭的連體技特效
            "Triangle" => fxTriangleCombo,  // ⭐ Triangle 開頭的連體技特效
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