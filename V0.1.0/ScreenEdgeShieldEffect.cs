using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 螢幕邊緣護盾特效 V0.2.0
/// - 在螢幕邊緣繪製藍色邊框
/// - 當護盾被激活時顯示
/// - 當護盾擋下傷害時閃光
/// - 支援顏色和透明度動態調整
/// </summary>
public class ScreenEdgeShieldEffect : MonoBehaviour
{
    [Header("== 邊框設定 ==")]
    [SerializeField] private float borderThickness = 15f;      // 邊框厚度
    [SerializeField] private float cornerRadius = 20f;         // 四角圓角半徑
    [SerializeField] private Color defaultColor = Color.cyan;

    [Header("== 動畫設定 ==")]
    [SerializeField] private bool enablePulsing = true;        // 是否啟用脈衝動畫
    [SerializeField] private float pulseSpeed = 2f;            // 脈衝速度
    [SerializeField] private float pulseIntensity = 0.3f;      // 脈衝強度

    [Header("== 邊框圖像 ==")]
    [SerializeField] private Image borderImage;
    [SerializeField] private Sprite borderSprite;

    private RectTransform rectTransform;
    private Graphic borderGraphic;
    private Color currentColor;
    private bool isActive = false;
    private float pulseTimer = 0f;

    void Start()
    {
        InitializeComponents();
        currentColor = defaultColor;
        isActive = false;

        Debug.Log($"[螢幕邊緣護盾特效] 初始化完成");
    }

    void Update()
    {
        if (isActive && enablePulsing)
        {
            UpdatePulseAnimation();
        }
    }

    private void InitializeComponents()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Debug.LogError("[螢幕邊緣護盾特效] 未找到 RectTransform 組件");
            enabled = false;
            return;
        }

        // ⭐ 如果沒有設定 borderImage，則自動創建邊框佈局
        if (borderImage == null)
        {
            CreateBorderLayout();
        }
        else
        {
            borderGraphic = borderImage.GetComponent<Graphic>();
        }
    }

    /// <summary>
    /// ⭐ 如果沒有現成的邊框 Image，則自動創建
    /// </summary>
    private void CreateBorderLayout()
    {
        // 頂部邊框
        CreateBorderPanel("Top", new Vector2(0.5f, 1f), new Vector2(1f, 0f),
                         new Vector2(0, -borderThickness / 2));

        // 底部邊框
        CreateBorderPanel("Bottom", new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                         new Vector2(0, borderThickness / 2));

        // 左側邊框
        CreateBorderPanel("Left", new Vector2(0f, 0.5f), new Vector2(0f, 1f),
                         new Vector2(borderThickness / 2, 0));

        // 右側邊框
        CreateBorderPanel("Right", new Vector2(1f, 0.5f), new Vector2(0f, 1f),
                         new Vector2(-borderThickness / 2, 0));

        Debug.Log($"[螢幕邊緣護盾特效] ✓ 邊框佈局已自動創建");
    }

    /// <summary>
    /// ⭐ 創建單個邊框面板
    /// </summary>
    private void CreateBorderPanel(string name, Vector2 anchorPos, Vector2 sizeDelta, Vector2 offsetPos)
    {
        GameObject borderPanel = new GameObject($"Border_{name}");
        borderPanel.transform.SetParent(transform, false);

        Image panelImage = borderPanel.AddComponent<Image>();
        panelImage.color = defaultColor;

        RectTransform panelRect = borderPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = anchorPos;
        panelRect.anchorMax = anchorPos;

        // 設定尺寸
        if (name == "Top" || name == "Bottom")
        {
            panelRect.sizeDelta = new Vector2(Screen.width, borderThickness);
        }
        else
        {
            panelRect.sizeDelta = new Vector2(borderThickness, Screen.height);
        }

        panelRect.anchoredPosition = offsetPos;

        // 設定圖像顏色
        if (borderSprite != null)
        {
            panelImage.sprite = borderSprite;
        }
        else
        {
            panelImage.color = defaultColor;
        }
    }

    /// <summary>
    /// ⭐ 激活或停用特效
    /// </summary>
    public void SetActive(bool active)
    {
        isActive = active;
        gameObject.SetActive(active);

        if (active)
        {
            pulseTimer = 0f;
            Debug.Log($"[螢幕邊緣護盾特效] ✓ 已激活");
        }
        else
        {
            Debug.Log($"[螢幕邊緣護盾特效] ✗ 已停用");
        }
    }

    /// <summary>
    /// ⭐ 動態設定邊框顏色
    /// </summary>
    public void SetColor(Color newColor)
    {
        currentColor = newColor;

        // 更新所有子物體的顏色
        foreach (Image img in GetComponentsInChildren<Image>())
        {
            img.color = newColor;
        }
    }

    /// <summary>
    /// ⭐ 脈衝動畫更新
    /// </summary>
    private void UpdatePulseAnimation()
    {
        pulseTimer += Time.deltaTime * pulseSpeed;

        // 正弦波脈衝效果
        float pulseValue = Mathf.Sin(pulseTimer * Mathf.PI) * pulseIntensity;
        float newAlpha = Mathf.Clamp01(currentColor.a + pulseValue);

        Color pulsedColor = currentColor;
        pulsedColor.a = newAlpha;

        foreach (Image img in GetComponentsInChildren<Image>())
        {
            img.color = pulsedColor;
        }

        // 重置計時器
        if (pulseTimer > 1f)
        {
            pulseTimer = 0f;
        }
    }

    /// <summary>
    /// ⭐ 快速閃光效果（護盾擋下傷害時）
    /// </summary>
    public void PlayFlashEffect(float duration = 0.3f)
    {
        StartCoroutine(FlashEffectCoroutine(duration));
    }

    IEnumerator FlashEffectCoroutine(float duration)
    {
        Color originalColor = currentColor;
        Color flashColor = new Color(1f, 1f, 1f, 1f);  // 白色

        float elapsed = 0f;
        while (elapsed < duration / 2)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2);
            Color lerpedColor = Color.Lerp(originalColor, flashColor, t);

            foreach (Image img in GetComponentsInChildren<Image>())
            {
                img.color = lerpedColor;
            }

            yield return null;
        }

        elapsed = 0f;
        while (elapsed < duration / 2)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2);
            Color lerpedColor = Color.Lerp(flashColor, originalColor, t);

            foreach (Image img in GetComponentsInChildren<Image>())
            {
                img.color = lerpedColor;
            }

            yield return null;
        }

        SetColor(originalColor);
    }

    /// <summary>
    /// ⭐ 設定邊框厚度
    /// </summary>
    public void SetBorderThickness(float thickness)
    {
        borderThickness = thickness;

        foreach (RectTransform rect in GetComponentsInChildren<RectTransform>())
        {
            if (rect == rectTransform) continue;

            if (rect.name.Contains("Top") || rect.name.Contains("Bottom"))
            {
                rect.sizeDelta = new Vector2(Screen.width, borderThickness);
            }
            else if (rect.name.Contains("Left") || rect.name.Contains("Right"))
            {
                rect.sizeDelta = new Vector2(borderThickness, Screen.height);
            }
        }
    }

    /// <summary>
    /// ⭐ 逐漸消退效果
    /// </summary>
    public void FadeOut(float duration = 0.5f)
    {
        StartCoroutine(FadeOutCoroutine(duration));
    }

    IEnumerator FadeOutCoroutine(float duration)
    {
        Color startColor = currentColor;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            Color fadedColor = startColor;
            fadedColor.a = Mathf.Lerp(startColor.a, 0f, t);

            foreach (Image img in GetComponentsInChildren<Image>())
            {
                img.color = fadedColor;
            }

            yield return null;
        }

        SetActive(false);
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }
}