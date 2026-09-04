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

    [Header("== 敵人特殊行為 ==")]
    [SerializeField] private bool useRandomSpells = true;
    [SerializeField] private string[] preferredSpells = new string[] { "Circle", "Square", "Triangle" };

    [Header("== V0.2.0 進階攻擊系統 ==")]
    [SerializeField] private bool enableAdvancedAttacks = true;
    [SerializeField]
    private AdvancedAttackMode[] availableAttackModes = new AdvancedAttackMode[]
    {
        AdvancedAttackMode.SingleAttack,
        AdvancedAttackMode.ShieldedAttack
    };

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
    private string shieldType = "None";  // 護盾屬性：Circle、Triangle、None
    private Coroutine chargeTimerCoroutine;
    private Coroutine glowControlCoroutine;
    private Coroutine stunCoroutine;
    private Transform canvasParent;
    private Color originalColor;

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

            isCharging = true;
            OnEnemyChargeStart?.Invoke(enemyID);

            // ⭐ 決定是否使用護盾
            hasActiveShield = false;
            shieldType = "None";
            if (enableAdvancedAttacks && Random.value > 0.5f)
            {
                hasActiveShield = true;
                shieldType = Random.value > 0.5f ? "Circle" : "Triangle";
                Debug.Log($"敵人 {enemyID} 啟動 {shieldType} 護盾");
            }

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

    IEnumerator ChargeTimer()
    {
        Debug.Log($"敵人 {enemyID} 出招，請施展 {requiredSpellToBreak} 打斷");

        if (animator != null)
        {
            animator.SetBool("IsCharging", true);
        }

        // ⭐ 啟動粒子系統
        if (glowControlCoroutine != null)
            StopCoroutine(glowControlCoroutine);
        glowControlCoroutine = StartCoroutine(ControlCustomGlowParticles(requiredSpellToBreak, attackWindow));

        yield return new WaitForSeconds(attackWindow);

        if (isCharging)
        {
            isCharging = false;

            if (animator != null)
            {
                animator.SetTrigger("Punch");
                animator.SetBool("IsCharging", false);
            }

            if (currentParticleSystem != null)
            {
                currentParticleSystem.Stop();
            }

            OnEnemyCharged?.Invoke(enemyID);
        }
    }

    /// <summary>
    /// 控制粒子系統
    /// </summary>
    IEnumerator ControlCustomGlowParticles(string spellName, float duration)
    {
        GameObject particlePrefab = GetGlowParticlePrefab(spellName);

        if (particlePrefab == null)
        {
            Debug.LogWarning($"❌ 敵人 {enemyID}: 找不到 {spellName} 的粒子 Prefab");
            yield break;
        }

        currentGlowParticles = Instantiate(particlePrefab, transform.position + glowOffset, Quaternion.identity, transform);
        currentGlowParticles.transform.localScale = Vector3.one * glowScale;

        currentParticleSystem = currentGlowParticles.GetComponent<ParticleSystem>();

        if (currentParticleSystem == null)
        {
            Debug.LogError($"❌ 敵人 {enemyID}: 粒子 Prefab 沒有 ParticleSystem 組件");
            Destroy(currentGlowParticles);
            yield break;
        }

        currentParticleSystem.Play();
        Debug.Log($"✓ 敵人 {enemyID}: 播放 {spellName} 粒子效果 (縮放: {glowScale}x)");

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
    /// ⭐ V0.2.0 改進：接收玩家的攻擊並判定是否被打斷
    /// </summary>
    public bool TakeSpellDamage(string playerSpell)
    {
        if (!isCharging || isStunned) return false;

        // ⭐ 如果敵人有護盾
        if (hasActiveShield && shieldType != "None")
        {
            // 檢查玩家的攻擊是否能破壞護盾
            bool canBreakShield = false;

            if (shieldType == "Circle" && playerSpell.Contains("Triangle"))
            {
                canBreakShield = true;
            }
            else if (shieldType == "Triangle" && playerSpell.Contains("Circle"))
            {
                canBreakShield = true;
            }

            if (!canBreakShield)
            {
                Debug.Log($"❌ 敵人 {enemyID} 的 {shieldType} 護盾擋下了攻擊！需要用 {(shieldType == "Circle" ? "Triangle" : "Circle")} 來破壞");
                return false;
            }

            // 護盾被破壞
            hasActiveShield = false;
            shieldType = "None";
            Debug.Log($"⚡ 敵人 {enemyID} 的護盾被破壞！");
        }

        // 檢查是否匹配所需咒語
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
    /// ⭐ V0.2.0 新增：進入僵直狀態
    /// </summary>
    private void EnterStunState()
    {
        isStunned = true;
        Debug.Log($"敵人 {enemyID} 進入僵直狀態，持續 {stunDuration}s");

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
    public string GetShieldType() => shieldType;

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
        DualElementAttack = 1,      // 雙屬性同時進行
        ShieldedAttack = 2,         // 屬性護盾
        WheelSpinAttack = 3         // 轉盤攻擊
    }
}