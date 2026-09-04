using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ShieldFlashUi : MonoBehaviour
{
    private Image flashImage;
    private Coroutine flashCoroutine;

    [Header("=== 護盾視覺調整 ===")]
    [Range(0f, 1f)]
    public float maxAlpha = 0.4f; // 藍色遮罩最高亮到多少透明度

    void Awake()
    {
        flashImage = GetComponent<Image>();
        if (flashImage != null)
        {
            // 我們直接把 Alpha 設為 0，讓它在場景上完全透明隱形
            Color c = flashImage.color;
            c.a = 0f;
            flashImage.color = c;

            // 確保它是開啟的，這樣才能收得到協程指令
            gameObject.SetActive(true);
        }
    }

    /// 觸發玩家護盾受擊的藍色遮罩閃爍（維持 1 秒）
    public void PlayShieldFlash()
    {
        if (flashImage == null) return;

        if (!gameObject.activeSelf) gameObject.SetActive(true);

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(FlashRoutine(1.0f));
    }

    IEnumerator FlashRoutine(float duration)
    {
        float elapsed = 0.0f;
        Color baseColor = flashImage.color;

        Debug.Log("玩家受擊！");

        while (elapsed < duration)
        {
            //  1 秒倒數，透明度從 maxAlpha 線性淡出到 0
            float currentAlpha = Mathf.Lerp(maxAlpha, 0f, elapsed / duration);

            baseColor.a = currentAlpha;
            flashImage.color = baseColor;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 結束後徹底回歸完全透明
        baseColor.a = 0f;
        flashImage.color = baseColor;

        flashCoroutine = null;
    }
}