using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敵人控制器 V0.2.0 - 支持進階攻擊系統
/// - 敵人被打斷進入僵直（無敵但可被打斷期間繼續受傷）
/// - 敵人屬性護盾系統
/// - 敵人特殊攻擊方式配置
/// </summary>
public class EnemyController : MonoBehaviour
{
    [Header("== 敵人基本資訊 ==")]
    [SerializeField] private int enemyID = 0;
    [SerializeField] private string enemyName = "dummy_bear";
    [SerializeField] private int maxHealth = 5;

    [Header("== 敵人出招時間設定 ==")]
    [SerializeField] private float minChargeInterval = 3.0f;
    [SerializeField] private float maxChargeInterval = 5.0f;
    [SerializeField] private float attackWindow = 3.5f;

    [Header("== 特效 Prefabs ==")]
    [SerializeField] private GameObject glowCircleParticlePrefab;
    [SerializeField] private GameObject glowSquareParticlePrefab;
    [SerializeField] private GameObject glowTriangleParticlePrefab;
    [SerializeField] private GameObject enemyDeathParticlePrefab;

    [Header("== 粒子效果設定 ==")]
    [SerializeField] private Vector3 glowOffset = Vector3.zero;
    [SerializeField] private float glowScale = 1.0f;

    [Tooltip("強制粒子特效（含所有子系統）循環播放，填滿整個出招視窗。" +
             "Unity 的 Looping 是逐一 ParticleSystem 的設定，勾根物件不會套用到子系統")]
    [SerializeField] private bool forceParticleLooping = true;

    [Tooltip("循環週期（秒）。0 = 保留 Prefab 原本的 Duration。" +
             "爆發式（burst）特效建議設成接近最長粒子壽命，否則兩次爆發之間會出現空檔")]
    [SerializeField] private float loopDurationOverride = 0f;

    [Header("== 敵人特殊行為 ==")]
    [SerializeField] private bool useRandomSpells = true;
    // ⚠ 不可放入 Square：Square 是玩家的防禦手勢，BattleManager 會在傳給敵人前攔截，
    //   敵人若要求 Square 破解會變成無解攻擊（見 SanitizeSpellPool）
    [SerializeField] private string[] preferredSpells = new string[] { "Circle", "Triangle" };

    [Header("== V0.2.0 進階攻擊系統 ==")]
    [SerializeField] private bool enableAdvancedAttacks = true;
    [SerializeField]
    private AdvancedAttackMode[] availableAttackModes = new AdvancedAttackMode[]
    {
        AdvancedAttackMode.SingleAttack,
        AdvancedAttackMode.ShieldedAttack
    };

    [Header("== 護盾設定 ==")]
    [Tooltip("ShieldedAttack 模式下可能出現的護盾屬性")]
    [SerializeField]
    private ShieldType[] availableShieldTypes = new ShieldType[]
    {
        ShieldType.Circle,
        ShieldType.Triangle,
        ShieldType.Square
    };

    [Tooltip("護盾專用特效；留空則退回同形狀的出招特效")]
    [SerializeField] private GameObject shieldCircleParticlePrefab;
    [SerializeField] private GameObject shieldTriangleParticlePrefab;
    [SerializeField] private GameObject shieldSquareParticlePrefab;

    [Tooltip("在敵人身上顯示護盾光環與形狀圖示")]
    [SerializeField] private bool showShieldIndicator = true;
    [SerializeField] private EnemyShieldIndicator shieldIndicator;

    [Header("== 僵直狀態設定 ==")]
    [SerializeField] private float stunDuration = 2.0f;
    [SerializeField] private Color stunTintColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    // ⭐ 自動讀取的 Live2D 組件
    private Animator animator;
    private CanvasGroup canvasGroup;
    private ImageJuiceUi enemyHurtJuice;
    private Image enemyImage;

    // ⭐ 粒子系統實例
    private GameObject currentGlowParticles;
    private ParticleSystem currentParticleSystem;

    // 戰鬥狀態
    private int currentHealth;
    private bool isCharging = false;
    private bool isStunned = false;
    private bool hasActiveShield = false;
    private string requiredSpellToBreak = "Circle";
    private ShieldType shieldType = ShieldType.None;   // 護盾屬性
    private AdvancedAttackMode currentAttackMode = AdvancedAttackMode.SingleAttack;
    private Coroutine chargeTimerCoroutine;
    private Coroutine glowControlCoroutine;
    private Coroutine stunCoroutine;
    private Transform canvasParent;
    private Color originalColor;
    private bool shieldVfxHintShown = false;   // 護盾特效未指定的提示只印一次

    // 事件
    public delegate void EnemyEvent(int enemyID);
    public event EnemyEvent OnEnemyChargeStart;
    public event EnemyEvent OnEnemyCharged;
    public event EnemyEvent OnEnemyDefeated;
    public delegate void EnemyStunEvent(int enemyID, float duration);
    public event EnemyStunEvent OnEnemyStunned;

    void Start()
    {
        InitializeComponents();
        SanitizeSpellPool();

        currentHealth = maxHealth;
        canvasParent = GameObject.Find("Canvas")?.transform;

        Debug.Log($"敵人 {enemyID} ({enemyName}) 初始化完成 [V0.2.0]");
        Debug.Log($"  - Animator: {(animator != null ? "✓" : "✗")}");
        Debug.Log($"  - CanvasGroup: {(canvasGroup != null ? "✓" : "✗")}");
        Debug.Log($"  - ImageJuiceUi: {(enemyHurtJuice != null ? "✓" : "✗")}");
        Debug.Log($"  - 粒子縮放: {glowScale}x");
        Debug.Log($"  - 血量: {currentHealth}");
        Debug.Log($"  - 進階攻擊系統: {(enableAdvancedAttacks ? "啟用" : "禁用")}");

        StartCoroutine(EnemyAILoop());
    }

    private void InitializeComponents()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"敵人 {enemyID}: 找不到 Animator 組件！");
        }

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        enemyHurtJuice = GetComponent<ImageJuiceUi>();
        enemyImage = GetComponent<Image>();
        if (enemyImage != null)
        {
            originalColor = enemyImage.color;
        }

        // ⭐ V0.2.2：護盾指示（找不到就自動掛上，不需要 Prefab）
        if (showShieldIndicator)
        {
            if (shieldIndicator == null)
                shieldIndicator = GetComponentInChildren<EnemyShieldIndicator>(true);

            if (shieldIndicator == null)
                shieldIndicator = gameObject.AddComponent<EnemyShieldIndicator>();
        }
    }

    /// <summary>
    /// ⭐ V0.2.1 修正：Square 是玩家的防禦手勢，BattleManager.ReceiveSpellData() 會在
    /// 傳給敵人之前就攔截掉含 Square 的招式，因此敵人若要求 Square 破解，該次攻擊將無法被打斷。
    /// 這裡在執行期把 Square 從招式池剔除（Inspector 上殘留的舊序列化值也會一併修正）。
    /// </summary>
    private void SanitizeSpellPool()
    {
        List<string> pool = new List<string>();

        if (preferredSpells != null)
        {
            foreach (string spell in preferredSpells)
            {
                if (string.IsNullOrEmpty(spell)) continue;

                if (spell.Contains("Square"))
                {
                    Debug.LogWarning($"敵人 {enemyID}: 招式池含 '{spell}'，Square 為玩家防禦手勢、不可作為攻擊要求，已自動剔除");
                    continue;
                }

                pool.Add(spell);
            }
        }

        if (pool.Count == 0)
        {
            Debug.LogWarning($"敵人 {enemyID}: 招式池為空，回退為 Circle / Triangle");
            pool.Add("Circle");
            pool.Add("Triangle");
        }

        preferredSpells = pool.ToArray();
    }

    IEnumerator EnemyAILoop()
    {
        float initialDelay = Random.Range(minChargeInterval, maxChargeInterval);
        yield return new WaitForSeconds(initialDelay);

        while (currentHealth > 0)
        {
            // 等待僵直狀態結束
            while (isStunned)
            {
                yield return null;
            }

            if (useRandomSpells)
            {
                requiredSpellToBreak = preferredSpells[Random.Range(0, preferredSpells.Length)];
            }
            else
            {
                requiredSpellToBreak = preferredSpells[0];
            }

            // ⭐ V0.2.1：先決定攻擊模式與護盾，再送出事件，
            //    外部監聽者（BattleManager／UI）才讀得到正確的護盾資訊
            DecideAttackMode();

            isCharging = true;
            OnEnemyChargeStart?.Invoke(enemyID);

            chargeTimerCoroutine = StartCoroutine(ChargeTimer());

            while (isCharging)
            {
                yield return null;
            }

            StopEnemyCoroutines();

            if (currentHealth <= 0) break;

            float cooldownTime = Random.Range(minChargeInterval, maxChargeInterval);
            yield return new WaitForSeconds(cooldownTime);
        }
    }

    /// <summary>
    /// ⭐ V0.2.1 新增：依 availableAttackModes 決定本次出招模式
    /// （舊版此 enum 完全沒被讀取，模式寫死在一行 Random.value > 0.5f）
    /// </summary>
    private void DecideAttackMode()
    {
        hasActiveShield = false;
        shieldType = ShieldType.None;

        if (!enableAdvancedAttacks || availableAttackModes == null || availableAttackModes.Length == 0)
        {
            currentAttackMode = AdvancedAttackMode.SingleAttack;
            return;
        }

        currentAttackMode = availableAttackModes[Random.Range(0, availableAttackModes.Length)];

        switch (currentAttackMode)
        {
            case AdvancedAttackMode.SingleAttack:
                break;

            case AdvancedAttackMode.ShieldedAttack:
                ActivateShield();
                break;

            case AdvancedAttackMode.DualElementAttack:
            case AdvancedAttackMode.WheelSpinAttack:
                // TODO: 尚未實作，本次退回單次攻擊
                Debug.LogWarning($"敵人 {enemyID}: {currentAttackMode} 尚未實作，本次退回 SingleAttack");
                currentAttackMode = AdvancedAttackMode.SingleAttack;
                break;
        }
    }

    /// <summary>
    /// ⭐ V0.2.1 新增：從 availableShieldTypes 隨機挑一種護盾並啟動
    /// </summary>
    private void ActivateShield()
    {
        if (availableShieldTypes == null || availableShieldTypes.Length == 0)
        {
            Debug.LogWarning($"敵人 {enemyID}: availableShieldTypes 為空，本次不開盾");
            return;
        }

        ShieldType picked = availableShieldTypes[Random.Range(0, availableShieldTypes.Length)];
        if (picked == ShieldType.None) return;

        hasActiveShield = true;
        shieldType = picked;
        Debug.Log($"敵人 {enemyID} 啟動{GetShieldDisplayName(picked)}（{GetShieldHintText(picked)}）");
    }

    IEnumerator ChargeTimer()
    {
        // ⭐ V0.2.2：舉盾是防禦姿態，不是攻擊
        bool isShieldStance = hasActiveShield && shieldType != ShieldType.None;

        if (isShieldStance)
        {
            Debug.Log($"敵人 {enemyID} 舉起{GetShieldDisplayName(shieldType)}，{GetShieldHintText(shieldType)}（此姿態不會攻擊玩家）");
        }
        else
        {
            Debug.Log($"敵人 {enemyID} 出招，請施展 {requiredSpellToBreak} 打斷");
        }

        if (animator != null)
        {
            animator.SetBool("IsCharging", true);
        }

        // ⭐ 啟動粒子系統：舉盾播護盾特效（屬性與護盾一致），一般出招播出招特效
        if (glowControlCoroutine != null)
            StopCoroutine(glowControlCoroutine);

        GameObject vfxPrefab = isShieldStance
            ? GetShieldParticlePrefab(shieldType)
            : GetGlowParticlePrefab(requiredSpellToBreak);

        if (vfxPrefab != null)
        {
            glowControlCoroutine = StartCoroutine(ControlCustomGlowParticles(vfxPrefab, attackWindow));
        }
        else if (!isShieldStance)
        {
            // 出招卻沒有粒子 Prefab 才是設定錯誤；舉盾沒有粒子是允許的
            Debug.LogWarning($"❌ 敵人 {enemyID}: 找不到 {requiredSpellToBreak} 的出招粒子 Prefab");
        }

        // ⭐ 顯示護盾指示
        if (isShieldStance && shieldIndicator != null)
        {
            shieldIndicator.Show(shieldType);
        }

        yield return new WaitForSeconds(attackWindow);

        if (isCharging)
        {
            isCharging = false;

            if (currentParticleSystem != null)
            {
                currentParticleSystem.Stop();
            }

            if (shieldIndicator != null) shieldIndicator.Hide();

            if (isShieldStance)
            {
                // ⭐ V0.2.2：護盾時間結束就自然收起，不對玩家造成任何傷害
                hasActiveShield = false;
                shieldType = ShieldType.None;

                if (animator != null)
                {
                    animator.SetBool("IsCharging", false);
                }

                Debug.Log($"敵人 {enemyID} 收起護盾（未造成傷害）");
            }
            else
            {
                if (animator != null)
                {
                    animator.SetTrigger("Punch");
                    animator.SetBool("IsCharging", false);
                }

                OnEnemyCharged?.Invoke(enemyID);
            }
        }
    }

    /// <summary>
    /// 控制粒子系統
    /// </summary>
    IEnumerator ControlCustomGlowParticles(GameObject particlePrefab, float duration)
    {
        if (particlePrefab == null)
        {
            Debug.LogWarning($"❌ 敵人 {enemyID}: 未指定粒子 Prefab，略過特效");
            yield break;
        }

        currentGlowParticles = Instantiate(particlePrefab, transform.position + glowOffset, Quaternion.identity, transform);
        currentGlowParticles.transform.localScale = Vector3.one * glowScale;

        currentParticleSystem = currentGlowParticles.GetComponent<ParticleSystem>();
        if (currentParticleSystem == null)
            currentParticleSystem = currentGlowParticles.GetComponentInChildren<ParticleSystem>(true);

        if (currentParticleSystem == null)
        {
            Debug.LogError($"❌ 敵人 {enemyID}: 粒子 Prefab 沒有 ParticleSystem 組件");
            Destroy(currentGlowParticles);
            yield break;
        }

        // ⭐ V0.2.3：強制整組粒子循環，讓特效撐滿整個出招視窗
        if (forceParticleLooping)
        {
            ApplyLoopingToAll(currentGlowParticles);
        }

        currentParticleSystem.Play();   // withChildren 預設為 true，會一併播放子系統
        Debug.Log($"✓ 敵人 {enemyID}: 播放 {particlePrefab.name} 粒子效果 (縮放: {glowScale}x)");

        float elapsed = 0f;
        while (elapsed < duration && isCharging)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (currentParticleSystem != null)
        {
            currentParticleSystem.Stop();
        }

        yield return new WaitForSeconds(2f);
        if (currentGlowParticles != null)
        {
            Destroy(currentGlowParticles);
        }
    }

    /// <summary>
    /// ⭐ V0.2.3：把整棵子樹的 ParticleSystem 全部設成循環播放
    ///
    /// 為什麼需要：Unity 的 Looping 是每個 ParticleSystem 各自的設定，
    /// 在根物件勾 Looping 不會套用到子系統。像 impact 類的特效通常是
    /// 「根 + 數個子系統，各自在 t=0 爆發一次」，只勾根物件的話子系統
    /// 播完就停，出招視窗剩下的時間會沒有畫面。
    /// </summary>
    private void ApplyLoopingToAll(GameObject root)
    {
        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem ps in systems)
        {
            // Duration 不能在播放中修改，先停下並清空（Prefab 多半 playOnAwake = true）
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;

            if (loopDurationOverride > 0f)
            {
                main.duration = loopDurationOverride;
            }
        }
    }

    /// <summary>
    /// 根據招式名稱獲取對應的粒子 Prefab
    /// </summary>
    private GameObject GetGlowParticlePrefab(string spellName)
    {
        return spellName switch
        {
            "Circle" => glowCircleParticlePrefab,
            "Square" => glowSquareParticlePrefab,
            "Triangle" => glowTriangleParticlePrefab,
            _ => null
        };
    }

    /// <summary>
    /// ⭐ V0.2.2：取得護盾專用特效
    /// 敵人的提示屬性與護盾屬性一致 —— 出圓形盾就播圓形盾特效，其餘同理。
    ///
    /// ⚠ 未指定時回傳 null，「不會」退回出招特效：
    ///   出招特效代表的是攻擊，拿來當護盾會讓玩家把防禦姿態誤判成攻擊。
    ///   此時護盾的視覺由 EnemyShieldIndicator（光環 + 形狀圖示）負責。
    /// </summary>
    private GameObject GetShieldParticlePrefab(ShieldType shield)
    {
        GameObject prefab = shield switch
        {
            ShieldType.Circle => shieldCircleParticlePrefab,
            ShieldType.Triangle => shieldTriangleParticlePrefab,
            ShieldType.Square => shieldSquareParticlePrefab,
            _ => null
        };

        if (prefab == null && !shieldVfxHintShown)
        {
            shieldVfxHintShown = true;
            Debug.Log($"敵人 {enemyID}: 未指定{GetShieldDisplayName(shield)}特效，改由護盾指示器（光環 + 圖示）呈現。" +
                      $"若要專屬粒子效果，請指定 shield{shield}ParticlePrefab");
        }

        return prefab;
    }

    /// <summary>
    /// ⭐ V0.2.1 改進：接收玩家的攻擊並判定是否被打斷
    ///
    /// playerSpell：經 BattleManager 反轉後的招式，用來比對 requiredSpellToBreak
    /// drawnSpell ：玩家「實際畫出來」的手勢，用來比對護盾屬性
    ///
    /// 兩者必須分開的原因：護盾規則（圓盾需三角形破）是以玩家實際畫的圖形描述的，
    /// 若拿反轉後的名稱比對等於被反轉兩次，破解條件會顛倒。
    /// </summary>
    public bool TakeSpellDamage(string playerSpell, string drawnSpell = null)
    {
        if (!isCharging || isStunned) return false;

        // 未提供原始手勢時退回舊行為，避免其他呼叫端編譯失敗
        if (string.IsNullOrEmpty(drawnSpell)) drawnSpell = playerSpell;

        // ⭐ V0.2.2 舉盾姿態：敵人的提示屬性就是護盾屬性，
        //    因此「破盾」本身即等於打斷，不再另外比對 requiredSpellToBreak
        if (hasActiveShield && shieldType != ShieldType.None)
        {
            if (!CanBreakShield(shieldType, drawnSpell))
            {
                Debug.Log($"❌ 敵人 {enemyID} 的{GetShieldDisplayName(shieldType)}擋下了 {drawnSpell}！{GetShieldHintText(shieldType)}");
                return false;
            }

            Debug.Log($"⚡ 敵人 {enemyID} 的{GetShieldDisplayName(shieldType)}被 {drawnSpell} 破壞！");

            hasActiveShield = false;
            shieldType = ShieldType.None;
            if (shieldIndicator != null) shieldIndicator.Hide();

            isCharging = false;
            EnterStunState();
            TakeDamage();
            return true;
        }

        // 一般出招：比對反轉後的招式
        if (playerSpell.Contains(requiredSpellToBreak))
        {
            isCharging = false;
            EnterStunState();
            TakeDamage();
            return true;
        }

        return false;
    }

    /// <summary>
    /// ⭐ V0.2.1 新增：三種護盾各自的破解條件（以玩家實際畫出的手勢判定）
    ///   圓形護盾   → 三角形破解
    ///   三角形護盾 → 圓形破解
    ///   正方形護盾 → 圓形或三角形皆可破解
    /// </summary>
    private bool CanBreakShield(ShieldType shield, string drawnSpell)
    {
        if (string.IsNullOrEmpty(drawnSpell)) return false;

        bool hasCircle = drawnSpell.Contains("Circle");
        bool hasTriangle = drawnSpell.Contains("Triangle");

        switch (shield)
        {
            case ShieldType.Circle:
                return hasTriangle;

            case ShieldType.Triangle:
                return hasCircle;

            case ShieldType.Square:
                return hasCircle || hasTriangle;

            case ShieldType.None:
            default:
                return true;
        }
    }

    private string GetShieldDisplayName(ShieldType shield)
    {
        return shield switch
        {
            ShieldType.Circle => "圓形護盾",
            ShieldType.Triangle => "三角形護盾",
            ShieldType.Square => "正方形護盾",
            _ => "無護盾"
        };
    }

    private string GetShieldHintText(ShieldType shield)
    {
        return shield switch
        {
            ShieldType.Circle => "需畫三角形破解",
            ShieldType.Triangle => "需畫圓形破解",
            ShieldType.Square => "需畫圓形或三角形破解",
            _ => ""
        };
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：進入僵直狀態
    /// </summary>
    private void EnterStunState()
    {
        isStunned = true;
        Debug.Log($"敵人 {enemyID} 進入僵直狀態，持續 {stunDuration}s");

        if (shieldIndicator != null) shieldIndicator.Hide();

        // 視覺效果：變暗
        if (enemyImage != null)
        {
            enemyImage.color = stunTintColor;
        }

        OnEnemyStunned?.Invoke(enemyID, stunDuration);

        if (stunCoroutine != null)
            StopCoroutine(stunCoroutine);
        stunCoroutine = StartCoroutine(StunDurationCoroutine());
    }

    /// <summary>
    /// ⭐ V0.2.0 新增：僵直狀態計時
    /// </summary>
    IEnumerator StunDurationCoroutine()
    {
        yield return new WaitForSeconds(stunDuration);

        isStunned = false;
        Debug.Log($"敵人 {enemyID} 恢復正常");

        // 恢復原色
        if (enemyImage != null)
        {
            enemyImage.color = originalColor;
        }
    }

    private void TakeDamage()
    {
        currentHealth--;
        Debug.Log($"敵人 {enemyID} 受傷！剩餘血量: {currentHealth}");

        if (enemyHurtJuice != null)
        {
            enemyHurtJuice.PlayShake();
        }

        if (animator != null)
        {
            animator.SetTrigger("Hurt");
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"敵人 {enemyID} 被擊敗！");
        isCharging = false;
        isStunned = false;
        hasActiveShield = false;
        shieldType = ShieldType.None;

        if (shieldIndicator != null) shieldIndicator.Hide();

        StopEnemyCoroutines();
        OnEnemyDefeated?.Invoke(enemyID);

        if (enemyDeathParticlePrefab != null)
        {
            GameObject deathFX = Instantiate(
                enemyDeathParticlePrefab,
                transform.position,
                Quaternion.identity,
                canvasParent
            );
            Destroy(deathFX, 3f);
        }

        StartCoroutine(FadeOutEnemy());
    }

    IEnumerator FadeOutEnemy()
    {
        float duration = 1.5f;
        float elapsed = 0f;

        if (canvasGroup != null)
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }

        gameObject.SetActive(false);
    }

    private void StopEnemyCoroutines()
    {
        if (chargeTimerCoroutine != null)
        {
            StopCoroutine(chargeTimerCoroutine);
            chargeTimerCoroutine = null;
        }
        if (glowControlCoroutine != null)
        {
            StopCoroutine(glowControlCoroutine);
            glowControlCoroutine = null;
        }
        if (stunCoroutine != null)
        {
            StopCoroutine(stunCoroutine);
            stunCoroutine = null;
        }
    }

    public int GetCurrentHealth() => currentHealth;
    public bool IsCharging() => isCharging;
    public bool IsStunned() => isStunned;
    public int GetEnemyID() => enemyID;
    public string GetShieldType() => hasActiveShield ? shieldType.ToString() : "None";
    public ShieldType GetShieldTypeEnum() => shieldType;
    public bool HasActiveShield() => hasActiveShield;
    public string GetRequiredSpell() => requiredSpellToBreak;
    public AdvancedAttackMode GetCurrentAttackMode() => currentAttackMode;

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    /// <summary>
    /// V0.2.0 敵人進階攻擊模式
    /// </summary>
    public enum AdvancedAttackMode
    {
        SingleAttack = 0,           // 單次攻擊
        DualElementAttack = 1,      // 雙屬性同時進行（未實作）
        ShieldedAttack = 2,         // 屬性護盾
        WheelSpinAttack = 3         // 轉盤攻擊（未實作）
    }

    /// <summary>
    /// V0.2.1 敵人護盾屬性
    ///   Circle   圓形護盾   → 玩家畫三角形破解
    ///   Triangle 三角形護盾 → 玩家畫圓形破解
    ///   Square   正方形護盾 → 玩家畫圓形或三角形皆可破解
    /// </summary>
    public enum ShieldType
    {
        None = 0,
        Circle = 1,
        Triangle = 2,
        Square = 3
    }
}