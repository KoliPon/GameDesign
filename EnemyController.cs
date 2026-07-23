using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敵人控制器 - 直接讀取 Live2D 組件
/// 無需 EnemyConfigSO，參數在此腳本中設定
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
    [SerializeField] private GameObject cooldownCirclePrefab;
    [SerializeField] private GameObject cooldownSquarePrefab;
    [SerializeField] private GameObject cooldownTrianglePrefab;
    [SerializeField] private GameObject enemyDeathParticlePrefab;

    [Header("== 敵人特殊行為 ==")]
    [SerializeField] private bool useRandomSpells = true;
    [SerializeField] private string[] preferredSpells = new string[] { "Circle", "Square", "Triangle" };

    // ⭐ 自動讀取的 Live2D 組件
    private Animator animator;
    private CanvasGroup canvasGroup;
    private ImageJuiceUi enemyHurtJuice;

    // 戰鬥狀態
    private int currentHealth;
    private bool isCharging = false;
    private string requiredSpellToBreak = "Circle";
    private GameObject currentVisualCircle;
    private Coroutine chargeTimerCoroutine;
    private Coroutine shrinkCoroutine;
    private Transform canvasParent;

    // 事件
    public delegate void EnemyEvent(int enemyID);
    public event EnemyEvent OnEnemyChargeStart;
    public event EnemyEvent OnEnemyCharged;
    public event EnemyEvent OnEnemyDefeated;

    void Start()
    {
        // ⭐ 自動讀取組件
        InitializeComponents();

        currentHealth = maxHealth;
        canvasParent = GameObject.Find("Canvas")?.transform;

        Debug.Log($"敵人 {enemyID} ({enemyName}) 初始化完成");
        Debug.Log($"  - Animator: {(animator != null ? "✓" : "✗")}");
        Debug.Log($"  - CanvasGroup: {(canvasGroup != null ? "✓" : "✗")}");
        Debug.Log($"  - ImageJuiceUi: {(enemyHurtJuice != null ? "✓" : "✗")}");
        Debug.Log($"  - 血量: {currentHealth}");

        StartCoroutine(EnemyAILoop());
    }

    /// <summary>
    /// ⭐ 自動讀取 Live2D 上的所有必要組件
    /// </summary>
    private void InitializeComponents()
    {
        // 1. 讀取 Animator（Live2D 的動畫控制器）
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"敵人 {enemyID}: 找不到 Animator 組件！");
        }

        // 2. 讀取或新增 CanvasGroup（用於淡出效果）
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            Debug.Log($"敵人 {enemyID}: 已自動新增 CanvasGroup");
        }

        // 3. 讀取 ImageJuiceUi（受傷特效）
        enemyHurtJuice = GetComponent<ImageJuiceUi>();
        if (enemyHurtJuice == null)
        {
            Debug.LogWarning($"敵人 {enemyID}: 找不到 ImageJuiceUi 組件（可選）");
        }
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
            ClearVisualCircle();

            if (currentHealth <= 0) break;

            float cooldownTime = Random.Range(minChargeInterval, maxChargeInterval);
            yield return new WaitForSeconds(cooldownTime);
        }
    }

    IEnumerator ChargeTimer()
    {
        Debug.Log($"敵人 {enemyID} 出招，請施展 {requiredSpellToBreak} 打斷");

        // ⭐ 敵人進入出招狀態，播放 standby 動畫
        if (animator != null)
        {
            animator.SetBool("IsCharging", true);  // 進入備戰狀態
        }

        GameObject prefabToSpawn = GetPrefabForSpell(requiredSpellToBreak);

        if (prefabToSpawn != null && canvasParent != null)
        {
            currentVisualCircle = Instantiate(prefabToSpawn, canvasParent);
            RectTransform rectTrans = currentVisualCircle.GetComponent<RectTransform>();
            if (rectTrans != null)
            {
                rectTrans.anchoredPosition = Vector2.zero;
            }
            shrinkCoroutine = StartCoroutine(AnimateCircleShrink(currentVisualCircle, attackWindow));
        }

        yield return new WaitForSeconds(attackWindow);

        if (isCharging)
        {
            isCharging = false;

            // ⭐ 攻擊動畫觸發
            if (animator != null)
            {
                animator.SetTrigger("Punch");
                animator.SetBool("IsCharging", false);  // 結束備戰狀態
            }

            OnEnemyCharged?.Invoke(enemyID);
        }
    }

    IEnumerator AnimateCircleShrink(GameObject circleObj, float duration)
    {
        Image fillImage = circleObj.GetComponent<Image>();
        RectTransform rectTransform = circleObj.GetComponent<RectTransform>();
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 2.5f;
        Vector3 targetScale = Vector3.one * 0.6f;
        Color startColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        Color endColor = new Color(1.0f, 0.0f, 0.0f, 1.0f);

        if (rectTransform != null) rectTransform.localScale = initialScale;
        if (fillImage != null) fillImage.color = startColor;

        while (elapsed < duration && fillImage != null && isCharging)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            if (rectTransform != null) rectTransform.localScale = Vector3.Lerp(initialScale, targetScale, t);
            if (fillImage != null) fillImage.color = Color.Lerp(startColor, endColor, t);
            yield return null;
        }
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

        // 播放受傷特效
        if (enemyHurtJuice != null)
        {
            enemyHurtJuice.PlayShake();
        }

        // ⭐ 觸發 Live2D 受傷動畫
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

        // 播放死亡特效
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

        // 淡出敵人
        StartCoroutine(FadeOutEnemy());
    }

    IEnumerator FadeOutEnemy()
    {
        float duration = 1.5f;
        float elapsed = 0f;

        // ⭐ 使用 CanvasGroup 控制透明度
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
            "Circle" => cooldownCirclePrefab,
            "Square" => cooldownSquarePrefab,
            "Triangle" => cooldownTrianglePrefab,
            _ => cooldownCirclePrefab
        };
    }

    private void StopEnemyCoroutines()
    {
        if (chargeTimerCoroutine != null)
        {
            StopCoroutine(chargeTimerCoroutine);
            chargeTimerCoroutine = null;
        }
        if (shrinkCoroutine != null)
        {
            StopCoroutine(shrinkCoroutine);
            shrinkCoroutine = null;
        }
    }

    private void ClearVisualCircle()
    {
        if (currentVisualCircle != null)
        {
            currentVisualCircle.SetActive(false);
            Destroy(currentVisualCircle);
            currentVisualCircle = null;
        }
    }

    public int GetCurrentHealth() => currentHealth;
    public bool IsCharging() => isCharging;
    public int GetEnemyID() => enemyID;

    void OnDestroy() { StopAllCoroutines(); }
}