using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敵人護盾指示 V0.2.3
/// 在敵人身上顯示目前的屬性護盾：護盾罩填色 + 外框光環 + 形狀圖示 + 脈動動畫
/// 這是護盾的主要視覺 —— 護盾「不會」借用出招粒子特效，否則玩家會把防禦誤判成攻擊。
///   圓形護盾   橙色 → 需畫三角形破解
///   三角形護盾 紫色 → 需畫圓形破解
///   正方形護盾 青色 → 圓形或三角形皆可破解
///
/// 由 EnemyController 自動掛載與呼叫，不需要任何 Prefab；
/// Inspector 未指定 Sprite 時會以程式產生形狀貼圖。
/// ⚠ 只有敵人會有屬性護盾，玩家的正方形防禦不使用此指示。
/// </summary>
[DisallowMultipleComponent]
public class EnemyShieldIndicator : MonoBehaviour
{
    [Header("== 護盾顏色 ==")]
    [SerializeField] private Color circleShieldColor = new Color(1f, 0.6f, 0f, 1f);    // 橙
    [SerializeField] private Color triangleShieldColor = new Color(0.8f, 0f, 1f, 1f);  // 紫
    [SerializeField] private Color squareShieldColor = new Color(0f, 0.85f, 1f, 1f);   // 青

    [Header("== 外框光環 ==")]
    [SerializeField] private bool showRing = true;
    [SerializeField] private float ringSizeMultiplier = 1.25f;
    [SerializeField] private float ringAlpha = 0.75f;

    [Header("== 護盾罩填色 ==")]
    [Tooltip("光環內的半透明填色，讓護盾讀起來像一層屏障而非只是外框")]
    [SerializeField] private bool showFill = true;
    [SerializeField] private float fillAlpha = 0.22f;

    [Header("== 形狀圖示 ==")]
    [SerializeField] private bool showIcon = true;
    [SerializeField] private float iconSize = 48f;
    [SerializeField] private Vector2 iconOffset = new Vector2(0f, 90f);
    [Tooltip("留空則由程式產生形狀圖示")]
    [SerializeField] private Sprite circleIconSprite;
    [SerializeField] private Sprite triangleIconSprite;
    [SerializeField] private Sprite squareIconSprite;

    [Header("== 脈動動畫 ==")]
    [SerializeField] private bool enablePulse = true;
    [SerializeField] private float pulseSpeed = 2.5f;
    [SerializeField] private float pulseScale = 0.08f;
    [SerializeField] private float pulseAlpha = 0.25f;

    private RectTransform selfRect;
    private GameObject indicatorRoot;
    private RectTransform rootRect;
    private RectTransform ringRect;
    private Image ringImage;
    private Image fillImage;
    private Image iconImage;

    private bool isVisible;
    private float pulseTimer;
    private Vector3 ringBaseScale = Vector3.one;

    // 程式產生的 Sprite 快取（全專案共用，避免每次開盾都重建貼圖）
    private static Sprite generatedRing;
    private static Sprite generatedDisc;
    private static Sprite generatedCircleIcon;
    private static Sprite generatedTriangleIcon;
    private static Sprite generatedSquareIcon;

    void Awake()
    {
        selfRect = GetComponent<RectTransform>();
        BuildVisual();
        Hide();
    }

    void Update()
    {
        if (!isVisible || !enablePulse) return;

        pulseTimer += Time.deltaTime * pulseSpeed;
        float wave = Mathf.Sin(pulseTimer);

        if (ringRect != null)
        {
            ringRect.localScale = ringBaseScale * (1f + wave * pulseScale);
        }

        if (ringImage != null && ringImage.enabled)
        {
            Color c = ringImage.color;
            c.a = Mathf.Clamp01(ringAlpha + wave * pulseAlpha);
            ringImage.color = c;
        }
    }

    /// <summary>
    /// ⭐ 顯示指定屬性的護盾指示
    /// </summary>
    public void Show(EnemyController.ShieldType shield)
    {
        if (shield == EnemyController.ShieldType.None)
        {
            Hide();
            return;
        }

        if (indicatorRoot == null) BuildVisual();
        if (indicatorRoot == null) return;

        Color color = GetShieldColor(shield);

        if (fillImage != null)
        {
            fillImage.enabled = showFill;
            fillImage.color = new Color(color.r, color.g, color.b, fillAlpha);
        }

        if (ringImage != null)
        {
            ringImage.enabled = showRing;
            ringImage.color = new Color(color.r, color.g, color.b, ringAlpha);
        }

        if (iconImage != null)
        {
            Sprite icon = GetIconSprite(shield);
            iconImage.sprite = icon;
            iconImage.enabled = showIcon && icon != null;
            iconImage.color = color;
        }

        pulseTimer = 0f;
        isVisible = true;
        indicatorRoot.SetActive(true);
    }

    /// <summary>
    /// ⭐ 隱藏護盾指示（破盾、僵直、出招結束、死亡時呼叫）
    /// </summary>
    public void Hide()
    {
        isVisible = false;

        if (ringRect != null) ringRect.localScale = ringBaseScale;
        if (indicatorRoot != null) indicatorRoot.SetActive(false);
    }

    public bool IsVisible() => isVisible;

    // ==================== 視覺建構 ====================

    private void BuildVisual()
    {
        if (indicatorRoot != null) return;

        if (selfRect == null) selfRect = GetComponent<RectTransform>();
        if (selfRect == null)
        {
            Debug.LogWarning($"[護盾指示] {name} 沒有 RectTransform，護盾指示僅支援 Canvas 上的 UI 敵人");
            return;
        }

        Vector2 enemySize = selfRect.rect.size;
        if (enemySize.x <= 1f || enemySize.y <= 1f) enemySize = new Vector2(200f, 200f);

        // 根節點
        indicatorRoot = new GameObject("ShieldIndicator");
        rootRect = indicatorRoot.AddComponent<RectTransform>();
        rootRect.SetParent(selfRect, false);
        SetCenterAnchors(rootRect);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = enemySize;

        float diameter = Mathf.Max(enemySize.x, enemySize.y) * ringSizeMultiplier;

        // 護盾罩填色（先建立 → 排在光環與圖示後方）
        GameObject fillObj = new GameObject("ShieldFill");
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.SetParent(rootRect, false);
        SetCenterAnchors(fillRect);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(diameter, diameter);

        fillImage = fillObj.AddComponent<Image>();
        fillImage.sprite = GetDiscSprite();
        fillImage.raycastTarget = false;

        // 外框光環
        GameObject ringObj = new GameObject("ShieldRing");
        ringRect = ringObj.AddComponent<RectTransform>();
        ringRect.SetParent(rootRect, false);
        SetCenterAnchors(ringRect);
        ringRect.anchoredPosition = Vector2.zero;
        ringRect.sizeDelta = new Vector2(diameter, diameter);
        ringBaseScale = ringRect.localScale;

        ringImage = ringObj.AddComponent<Image>();
        ringImage.sprite = GetRingSprite();
        ringImage.raycastTarget = false;

        // 形狀圖示
        GameObject iconObj = new GameObject("ShieldIcon");
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.SetParent(rootRect, false);
        SetCenterAnchors(iconRect);
        iconRect.anchoredPosition = iconOffset;
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);

        iconImage = iconObj.AddComponent<Image>();
        iconImage.raycastTarget = false;
    }

    private static void SetCenterAnchors(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private Color GetShieldColor(EnemyController.ShieldType shield)
    {
        return shield switch
        {
            EnemyController.ShieldType.Circle => circleShieldColor,
            EnemyController.ShieldType.Triangle => triangleShieldColor,
            EnemyController.ShieldType.Square => squareShieldColor,
            _ => Color.white
        };
    }

    private Sprite GetIconSprite(EnemyController.ShieldType shield)
    {
        switch (shield)
        {
            case EnemyController.ShieldType.Circle:
                if (circleIconSprite != null) return circleIconSprite;
                if (generatedCircleIcon == null) generatedCircleIcon = BuildRingSprite(128, 10f, 10f);
                return generatedCircleIcon;

            case EnemyController.ShieldType.Triangle:
                if (triangleIconSprite != null) return triangleIconSprite;
                if (generatedTriangleIcon == null) generatedTriangleIcon = BuildTriangleSprite(128, 12f, 9f);
                return generatedTriangleIcon;

            case EnemyController.ShieldType.Square:
                if (squareIconSprite != null) return squareIconSprite;
                if (generatedSquareIcon == null) generatedSquareIcon = BuildSquareSprite(128, 14f, 9f);
                return generatedSquareIcon;

            default:
                return null;
        }
    }

    private static Sprite GetRingSprite()
    {
        if (generatedRing == null) generatedRing = BuildRingSprite(160, 2f, 7f);
        return generatedRing;
    }

    private static Sprite GetDiscSprite()
    {
        if (generatedDisc == null) generatedDisc = BuildDiscSprite(160, 3f);
        return generatedDisc;
    }

    // ==================== 程式產生形狀貼圖 ====================
    // 未指定 Sprite 時使用，讓護盾指示不需要任何美術資源即可運作

    /// <summary>環形（空心圓）</summary>
    private static Sprite BuildRingSprite(int size, float margin, float thickness)
    {
        Texture2D tex = NewTexture(size);
        Color[] pixels = new Color[size * size];

        float c = (size - 1) * 0.5f;
        float outer = c - margin;
        float inner = outer - thickness;
        Vector2 center = new Vector2(c, c);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float a = Mathf.Min(Mathf.Clamp01((outer - d) * 0.5f),
                                    Mathf.Clamp01((d - inner) * 0.5f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        return Finish(tex, pixels, size);
    }

    /// <summary>實心圓（護盾罩填色用；邊緣向外淡出，讓屏障有柔邊）</summary>
    private static Sprite BuildDiscSprite(int size, float margin)
    {
        Texture2D tex = NewTexture(size);
        Color[] pixels = new Color[size * size];

        float c = (size - 1) * 0.5f;
        float radius = c - margin;
        Vector2 center = new Vector2(c, c);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);

                // 外緣較實、中央較透，敵人本體才不會被蓋住
                float a = Mathf.Clamp01((radius - d) * 0.5f);
                a *= Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(d / Mathf.Max(radius, 0.001f)));

                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        return Finish(tex, pixels, size);
    }

    /// <summary>空心正方形</summary>
    private static Sprite BuildSquareSprite(int size, float margin, float thickness)
    {
        Texture2D tex = NewTexture(size);
        Color[] pixels = new Color[size * size];

        float max = size - 1 - margin;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float minEdge = Mathf.Min(
                    Mathf.Min(x - margin, max - x),
                    Mathf.Min(y - margin, max - y));

                float a = Mathf.Min(Mathf.Clamp01((minEdge + 1f) * 0.5f),
                                    Mathf.Clamp01((thickness - minEdge) * 0.5f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        return Finish(tex, pixels, size);
    }

    /// <summary>空心正三角形（頂點朝上）</summary>
    private static Sprite BuildTriangleSprite(int size, float margin, float thickness)
    {
        Texture2D tex = NewTexture(size);
        Color[] pixels = new Color[size * size];

        // 逆時針排列，使邊界函數在三角形內為正值
        Vector2 top = new Vector2((size - 1) * 0.5f, size - 1 - margin);
        Vector2 bottomLeft = new Vector2(margin, margin);
        Vector2 bottomRight = new Vector2(size - 1 - margin, margin);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float dist = Mathf.Min(
                    EdgeDistance(top, bottomLeft, p),
                    Mathf.Min(EdgeDistance(bottomLeft, bottomRight, p),
                              EdgeDistance(bottomRight, top, p)));

                float a = Mathf.Min(Mathf.Clamp01((dist + 1f) * 0.5f),
                                    Mathf.Clamp01((thickness - dist) * 0.5f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        return Finish(tex, pixels, size);
    }

    /// <summary>點 p 到有向邊 a→b 的帶號距離（逆時針時，三角形內部為正）</summary>
    private static float EdgeDistance(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len = ab.magnitude;
        if (len < 0.0001f) return 0f;

        return (ab.x * (p.y - a.y) - ab.y * (p.x - a.x)) / len;
    }

    private static Texture2D NewTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    private static Sprite Finish(Texture2D tex, Color[] pixels, int size)
    {
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }
}
