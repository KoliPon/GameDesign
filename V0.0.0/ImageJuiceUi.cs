using System.Collections;
using UnityEngine;

public class ImageJuiceUi : MonoBehaviour
{
    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private Coroutine shakeCoroutine;

    void Awake()
    {
        // 抓取 UI 的位置組件
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            originalPosition = rectTransform.anchoredPosition;
        }
    }

    /// <summary>
    /// 觸發受擊震動效果（維持 1 秒）
    /// </summary>
    public void PlayShake()
    {
        if (rectTransform == null) return;

        // 如果前一次還沒震動完，先強制停止並回歸原位
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            rectTransform.anchoredPosition = originalPosition;
        }

        shakeCoroutine = StartCoroutine(ShakeRoutine(1.0f, 15f)); // 1秒長度，15點震動幅度
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0.0f;

        while (elapsed < duration)
        {
            // 隨著時間推移，震動幅度逐漸衰減（流暢感關鍵！）
            float currentMagnitude = Mathf.Lerp(magnitude, 0f, elapsed / duration);

            // 產生隨機的 X, Y 偏移量
            float randomX = Random.Range(-1f, 1f) * currentMagnitude;
            float randomY = Random.Range(-1f, 1f) * currentMagnitude;

            rectTransform.anchoredPosition = originalPosition + new Vector2(randomX, randomY);

            elapsed += Time.deltaTime;
            yield return null; // 等待下一幀
        }

        // 結束後務必精準回歸原點
        rectTransform.anchoredPosition = originalPosition;
        shakeCoroutine = null;
    }
}