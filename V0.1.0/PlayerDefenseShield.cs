using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家防禦護盾系統 V0.2.0
/// - 玩家施展正方形時激活護盾
/// - 護盾可以預防敵人攻擊造成的傷害（有數量限制）
/// - 視覺特效：藍色邊緣特效
/// - 與 BattleManager 協調
/// </summary>
public class PlayerDefenseShield : MonoBehaviour
{
    [Header("== 護盾基本設定 ==")]
    [SerializeField] private int maxShieldStacks = 3;          // 護盾堆疊數上限
    [SerializeField] private float shieldActiveDuration = 2.0f; // 護盾持續時間
    [SerializeField] private bool autoDeactivate = true;       // 護盾是否自動失效

    [Header("== 視覺特效 ==")]
    [SerializeField] private GameObject screenEdgeEffectPrefab;
    [SerializeField] private Color shieldActiveColor = Color.cyan;
    [SerializeField] private Color shieldBlockColor = new Color(0, 1, 1, 0.8f);
    [SerializeField] private float blockFlashDuration = 0.3f;

    [Header("== 音效設定 ==")]
    [SerializeField] private AudioClip shieldActivateSound;
    [SerializeField] private AudioClip shieldBlockSound;
    [SerializeField] private float soundVolume = 0.8f;

    private int currentShieldStacks = 0;
    private bool isShieldActive = false;
    private Coroutine shieldTimerCoroutine;
    private Coroutine shieldBlockCoroutine;
    private GameObject activeScreenEffect;
    private ScreenEdgeShieldEffect screenEffectScript;
    private AudioSource audioSource;

    void Start()
    {
        InitializeComponents();
        currentShieldStacks = 0;
        isShieldActive = false;

        Debug.Log($"[玩家護盾] 系統初始化完成");
        Debug.Log($"  - 最大堆疊數: {maxShieldStacks}");
        Debug.Log($"  - 護盾持續時間: {shieldActiveDuration}s");
    }

    private void InitializeComponents()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0;
        }
    }

    /// <summary>
    /// ⭐ 玩家施展正方形時調用 - 激活護盾
    /// 由 BattleManager 或 GestureChain 調用
    /// </summary>
    public void ActivateShield()
    {
        if (isShieldActive)
        {
            Debug.Log("[玩家護盾] 護盾已激活，無法重複激活");
            return;
        }

        isShieldActive = true;
        currentShieldStacks = maxShieldStacks;

        Debug.Log($"[玩家護盾] ✓ 護盾已激活！堆疊數: {currentShieldStacks}/{maxShieldStacks}");

        // 播放激活音效
        if (shieldActivateSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(shieldActivateSound, soundVolume);
        }

        // 創建螢幕邊緣特效
        CreateScreenEdgeEffect();

        // 啟動計時器
        if (shieldTimerCoroutine != null)
            StopCoroutine(shieldTimerCoroutine);
        shieldTimerCoroutine = StartCoroutine(ShieldDurationCoroutine());
    }

    /// <summary>
    /// ⭐ 護盾被敵人攻擊時調用
    /// 返回 true 表示護盾成功擋下傷害
    /// </summary>
    public bool TryBlockDamage()
    {
        if (!isShieldActive || currentShieldStacks <= 0)
        {
            Debug.Log("[玩家護盾] ✗ 護盾未激活或已耗盡");
            return false;
        }

        currentShieldStacks--;
        Debug.Log($"[玩家護盾] ✓ 成功擋下傷害！剩餘堆疊: {currentShieldStacks}/{maxShieldStacks}");

        // 播放擋下傷害的視覺和音效
        PlayBlockEffect();

        // 如果堆疊用盡，護盾失效
        if (currentShieldStacks <= 0)
        {
            DeactivateShield();
        }

        return true;
    }

    /// <summary>
    /// ⭐ 護盾被打斷或時間耗盡時調用 - 停用護盾
    /// </summary>
    public void DeactivateShield()
    {
        if (!isShieldActive) return;

        isShieldActive = false;
        Debug.Log($"[玩家護盾] ✗ 護盾已失效");

        // 銷毀螢幕邊緣特效
        if (activeScreenEffect != null)
        {
            Destroy(activeScreenEffect);
            activeScreenEffect = null;
            screenEffectScript = null;
        }

        // 停止計時器
        if (shieldTimerCoroutine != null)
        {
            StopCoroutine(shieldTimerCoroutine);
            shieldTimerCoroutine = null;
        }
    }

    /// <summary>
    /// ⭐ 創建螢幕邊緣藍色特效
    /// </summary>
    private void CreateScreenEdgeEffect()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[玩家護盾] 找不到 Canvas");
            return;
        }

        // ⭐ 修正版：如果有 Prefab 就使用，沒有就自動生成
        if (screenEdgeEffectPrefab != null)
        {
            activeScreenEffect = Instantiate(screenEdgeEffectPrefab, canvas.transform);
            activeScreenEffect.name = "PlayerShieldScreenEdge";
        }
        else
        {
            // ⭐ 自動生成護盾特效（使用修正後的生成器）
            activeScreenEffect = ScreenEdgeShieldEffectGenerator.CreateShieldEffect(canvas.transform);

            if (activeScreenEffect == null)
            {
                Debug.LogError("[玩家護盾] 無法創建護盾特效");
                return;
            }
        }

        // 設定為全螢幕
        RectTransform rectTransform = activeScreenEffect.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        screenEffectScript = activeScreenEffect.GetComponent<ScreenEdgeShieldEffect>();
        if (screenEffectScript != null)
        {
            screenEffectScript.SetActive(true);
            screenEffectScript.SetColor(shieldActiveColor);
        }

        Debug.Log("[玩家護盾] ✓ 螢幕邊緣特效已創建");
    }

    /// <summary>
    /// ⭐ 護盾擋下傷害時的視覺和音效反饋
    /// </summary>
    private void PlayBlockEffect()
    {
        if (shieldBlockCoroutine != null)
            StopCoroutine(shieldBlockCoroutine);
        shieldBlockCoroutine = StartCoroutine(BlockEffectCoroutine());

        // 播放擋下傷害的音效
        if (shieldBlockSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(shieldBlockSound, soundVolume);
        }
    }

    /// <summary>
    /// ⭐ 擋下傷害時的閃光效果
    /// </summary>
    IEnumerator BlockEffectCoroutine()
    {
        if (screenEffectScript == null) yield break;

        // 快速閃白
        Color originalColor = shieldActiveColor;
        screenEffectScript.SetColor(shieldBlockColor);

        yield return new WaitForSeconds(blockFlashDuration);

        // 恢復原色
        screenEffectScript.SetColor(originalColor);
    }

    /// <summary>
    /// ⭐ 護盾時間計時
    /// </summary>
    IEnumerator ShieldDurationCoroutine()
    {
        float elapsed = 0f;

        while (elapsed < shieldActiveDuration && isShieldActive)
        {
            elapsed += Time.deltaTime;

            // 可選：隨著時間推移，邊緣特效漸淡
            if (screenEffectScript != null)
            {
                float alpha = Mathf.Lerp(shieldActiveColor.a, 0.3f, elapsed / shieldActiveDuration);
                Color fadingColor = shieldActiveColor;
                fadingColor.a = alpha;
                screenEffectScript.SetColor(fadingColor);
            }

            yield return null;
        }

        if (autoDeactivate && isShieldActive)
        {
            Debug.Log("[玩家護盾] ⏰ 時間耗盡，護盾失效");
            DeactivateShield();
        }
    }

    public bool IsShieldActive() => isShieldActive;
    public int GetCurrentShieldStacks() => currentShieldStacks;
    public int GetMaxShieldStacks() => maxShieldStacks;

    void OnDestroy()
    {
        if (shieldTimerCoroutine != null)
            StopCoroutine(shieldTimerCoroutine);
        if (shieldBlockCoroutine != null)
            StopCoroutine(shieldBlockCoroutine);
        if (activeScreenEffect != null)
            Destroy(activeScreenEffect);
    }
}   