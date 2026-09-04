using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 螢幕邊緣護盾特效生成器 V0.2.0 - 修正版
/// - 自動創建藍色邊框特效
/// - 支援脈衝動畫
/// - 支援擋下傷害時的閃光效果
/// </summary>
public class ScreenEdgeShieldEffectGenerator : MonoBehaviour
{
    [Header("== 邊框設定 ==")]
    [SerializeField] private float borderThickness = 15f;
    [SerializeField] private Color borderColor = new Color(0, 1, 1, 0.8f);  // 藍色

    [Header("== 動畫設定 ==")]
    [SerializeField] private bool enablePulsing = true;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseIntensity = 0.3f;

    /// <summary>
    /// ⭐ 靜態方法：快速創建護盾特效
    /// </summary>
    public static GameObject CreateShieldEffect(Transform canvasTransform = null)
    {
        Canvas canvas = null;

        // ⭐ 修正：正確地查找 Canvas
        if (canvasTransform != null)
        {
            // 如果傳入的是 Canvas 的 Transform，直接獲取 Canvas 組件
            canvas = canvasTransform.GetComponent<Canvas>();

            // 如果不是 Canvas 的 Transform，就往上查找
            if (canvas == null)
            {
                canvas = canvasTransform.GetComponentInParent<Canvas>();
            }
        }

        // 如果還是找不到，全域查找
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogError("[護盾特效生成器] 找不到 Canvas");
            return null;
        }

        // ⭐ 使用 Canvas 的 transform
        GameObject shieldContainer = new GameObject("ScreenEdgeShield");
        shieldContainer.transform.SetParent(canvas.transform, false);

        RectTransform containerRect = shieldContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;

        // 創建四個邊框
        CreateBorderEdge(shieldContainer, "Top", new Vector2(0.5f, 1f), new Vector2(1f, 0f), 15f);
        CreateBorderEdge(shieldContainer, "Bottom", new Vector2(0.5f, 0f), new Vector2(1f, 0f), 15f);
        CreateBorderEdge(shieldContainer, "Left", new Vector2(0f, 0.5f), new Vector2(0f, 1f), 15f);
        CreateBorderEdge(shieldContainer, "Right", new Vector2(1f, 0.5f), new Vector2(0f, 1f), 15f);

        // 添加特效腳本
        ScreenEdgeShieldEffect effectScript = shieldContainer.AddComponent<ScreenEdgeShieldEffect>();

        Debug.Log("[護盾特效生成器] ✓ 護盾特效已創建");
        return shieldContainer;
    }

    /// <summary>
    /// ⭐ 創建單個邊框邊緣
    /// </summary>
    private static void CreateBorderEdge(GameObject parent, string name,
                                        Vector2 anchor, Vector2 sizeDelta, float thickness)
    {
        GameObject edgeObj = new GameObject($"Edge_{name}");
        edgeObj.transform.SetParent(parent.transform, false);

        Image edgeImage = edgeObj.AddComponent<Image>();
        edgeImage.color = new Color(0, 1, 1, 0.8f);  // 藍色

        RectTransform edgeRect = edgeObj.GetComponent<RectTransform>();
        edgeRect.anchorMin = anchor;
        edgeRect.anchorMax = anchor;
        edgeRect.pivot = new Vector2(0.5f, 0.5f);

        // 設定尺寸
        if (name == "Top" || name == "Bottom")
        {
            edgeRect.sizeDelta = new Vector2(Screen.width, thickness);
            if (name == "Top")
                edgeRect.anchoredPosition = new Vector2(0, -thickness / 2f);
            else
                edgeRect.anchoredPosition = new Vector2(0, thickness / 2f);
        }
        else
        {
            edgeRect.sizeDelta = new Vector2(thickness, Screen.height);
            if (name == "Left")
                edgeRect.anchoredPosition = new Vector2(thickness / 2f, 0);
            else
                edgeRect.anchoredPosition = new Vector2(-thickness / 2f, 0);
        }
    }
}