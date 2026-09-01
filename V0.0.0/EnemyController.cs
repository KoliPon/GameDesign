using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    // ⭐ 自動讀取的 Live2D 組件
    private Animator animator;
    private CanvasGroup canvasGroup;
    private ImageJuiceUi enemyHurtJuice;

    // ⭐ 粒子系統實例
    private GameObject currentGlowParticles;
    private ParticleSystem currentParticleSystem;

    // 戰鬥狀態
    private int currentHealth;
    private bool isCharging = false;
    private string requiredSpellToBreak = "Circle";
    private Coroutine chargeTimerCoroutine;
    private Coroutine glowControlCoroutine;
    private Transform canvasParent;

    // 事件
    public delegate void EnemyEvent(int enemyID);
    public event EnemyEvent OnEnemyChargeStart;
    public event EnemyEvent OnEnemyCharged;
    public event EnemyEvent OnEnemyDefeated;

    void Start()
    {
        InitializeComponents();

        currentHealth = maxHealth;
        canvasParent = GameObject.Find("Canvas")?.transform;

        Debug.Log($"敵人 {enemyID} ({enemyName}) 初始化完成");
        Debug.Log($"  - Animator: {(animator != null ? "✓" : "✗")}");
        Debug.Log($"  - CanvasGroup: {(canvasGroup != null ? "✓" : "✗")}");
        Debug.Log($"  - ImageJuiceUi: {(enemyHurtJuice != null ? "✓" : "✗")}");
        Debug.Log($"  - 粒子縮放: {glowScale}x");
        Debug.Log($"  - 血量: {currentHealth}");

        StartCoroutine(EnemyAILoop());
    }

    private void InitializeComponents()
    {
        // 1. 讀取 Animator
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"敵人 {enemyID}: 找不到 Animator 組件！");
        }

        // 2. 讀取或新增 CanvasGroup
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // 3. 讀取 ImageJuiceUi
        enemyHurtJuice = GetComponent<ImageJuiceUi>();
    }

    IEnumerator EnemyAILoop()
    {
        float initialDelay = Random.Range(minChargeInterval, maxChargeInterval);
        yield return new WaitForSeconds(initialDelay);

        while (currentHealth > 0)
        {
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

            // 停止粒子系統
            if (currentParticleSystem != null)
            {
                currentParticleSystem.Stop();
            }

            OnEnemyCharged?.Invoke(enemyID);
        }
    }

    /// <summary>
    /// ⭐ 控制粒子系統
    /// </summary>
    IEnumerator ControlCustomGlowParticles(string spellName, float duration)
    {
        GameObject particlePrefab = GetGlowParticlePrefab(spellName);

        if (particlePrefab == null)
        {
            Debug.LogWarning($"❌ 敵人 {enemyID}: 找不到 {spellName} 的粒子 Prefab");
            yield break;
        }

        // 實例化粒子
        currentGlowParticles = Instantiate(particlePrefab, transform.position + glowOffset, Quaternion.identity, transform);
        currentGlowParticles.transform.localScale = Vector3.one * glowScale;

        currentParticleSystem = currentGlowParticles.GetComponent<ParticleSystem>();

        if (currentParticleSystem == null)
        {
            Debug.LogError($"❌ 敵人 {enemyID}: 粒子 Prefab 沒有 ParticleSystem 組件");
            Destroy(currentGlowParticles);
            yield break;
        }

        // 啟動粒子系統
        currentParticleSystem.Play();
        Debug.Log($"✓ 敵人 {enemyID}: 播放 {spellName} 粒子效果 (縮放: {glowScale}x)");

        float elapsed = 0f;
        while (elapsed < duration && isCharging)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 停止粒子系統
        if (currentParticleSystem != null)
        {
            currentParticleSystem.Stop();
        }

        // 清除粒子物體
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

    public bool TakeSpellDamage(string playerSpell)
    {
        if (!isCharging) return false;

        if (playerSpell.Contains(requiredSpellToBreak))
        {
            isCharging = false;
            TakeDamage();
            return true;
        }
        return false;
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

    private GameObject GetPrefabForSpell(string spellName)
    {
        return spellName switch
        {
            "Circle" => glowCircleParticlePrefab,
            "Square" => glowSquareParticlePrefab,
            "Triangle" => glowTriangleParticlePrefab,
            _ => null
        };
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
    }

    public int GetCurrentHealth() => currentHealth;
    public bool IsCharging() => isCharging;
    public int GetEnemyID() => enemyID;

    void OnDestroy()
    {
        StopAllCoroutines();
    }
}