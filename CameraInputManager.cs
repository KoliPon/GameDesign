using System.Collections.Generic;
using UnityEngine;

public class CameraInputManager : MonoBehaviour
{
    private static CameraInputManager instance;
    public static CameraInputManager Instance => instance;

    [Header("== 攝像頭設定 ==")]
    [SerializeField] private int cameraIndex = 0;
    [SerializeField] private int targetWidth = 640;   
    [SerializeField] private int targetHeight = 360;
    [SerializeField] private int targetFPS = 30;

    private WebCamTexture webCamTexture;
    public Texture CurrentFrame => webCamTexture;
    public bool IsInitialized => webCamTexture != null && webCamTexture.isPlaying;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;

        if (transform.parent == null)
        {
            DontDestroyOnLoad(this.gameObject);
        }
    }

    void Start()
    {
        InitializeCamera();
    }

    private void InitializeCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("❌ 找不到任何攝像頭");
            return;
        }

        Debug.Log($"✓ 找到 {devices.Length} 個攝像頭");
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"  [{i}] {devices[i].name}");
        }

        if (cameraIndex >= devices.Length)
        {
            Debug.LogWarning($"攝像頭索引 {cameraIndex} 超出範圍，使用第一個");
            cameraIndex = 0;
        }

        WebCamDevice selectedDevice = devices[cameraIndex];
        Debug.Log($"✓ 選用攝像頭: {selectedDevice.name}");

        webCamTexture = new WebCamTexture(selectedDevice.name, targetWidth, targetHeight, targetFPS);
        webCamTexture.Play();

        Debug.Log($"攝像頭已啟動 ({targetWidth}x{targetHeight}@{targetFPS}fps)");
    }

    void OnDisable()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
        }
    }
}