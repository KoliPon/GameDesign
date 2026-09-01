using UnityEngine;
using System.Collections.Generic;

public class InfiniteRunner : MonoBehaviour
{
    [SerializeField] private GameObject segmentPrefab;
    [SerializeField] private int poolSize = 5;
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float segmentLength = 10f;
    [SerializeField] private Transform cameraTransform;

    [Header("回收設置")]
    [SerializeField] private float recycleDistance = 30f; // 調整這個值來控制何時回收

    [Header("位置偏移（修正擋住攝影機的問題）")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero; // 可在Inspector調整

    private Queue<Transform> segmentPool = new Queue<Transform>();
    private List<Transform> activeSegments = new List<Transform>();

    private void Start()
    {
        // 創建初始池
        for (int i = 0; i < poolSize; i++)
        {
            GameObject seg = Instantiate(segmentPrefab, transform);
            seg.SetActive(false);
            segmentPool.Enqueue(seg.transform);
        }

        // 激活初始段落
        for (int i = 0; i < poolSize; i++)
        {
            SpawnSegment(i * segmentLength);
        }
    }

    private void SpawnSegment(float zPosition)
    {
        if (segmentPool.Count > 0)
        {
            Transform segment = segmentPool.Dequeue();
            // 應用位置偏移以避免擋住攝影機
            segment.position = new Vector3(positionOffset.x, positionOffset.y, zPosition + positionOffset.z);
            segment.gameObject.SetActive(true);
            activeSegments.Add(segment);
        }
    }

    private void Update()
    {
        // 移動所有活躍段落
        for (int i = 0; i < activeSegments.Count; i++)
        {
            activeSegments[i].position += Vector3.back * moveSpeed * Time.deltaTime;
        }

        // 回收移出視野的段落（改進版本）
        for (int i = activeSegments.Count - 1; i >= 0; i--)
        {
            // 只有當段落完全通過攝影機時才回收
            if (activeSegments[i].position.z < cameraTransform.position.z - recycleDistance)
            {
                Transform recycled = activeSegments[i];
                activeSegments.RemoveAt(i);
                recycled.gameObject.SetActive(false);
                segmentPool.Enqueue(recycled);

                // 生成新段落
                float maxZ = activeSegments.Count > 0
                    ? activeSegments[activeSegments.Count - 1].position.z
                    : 0;
                SpawnSegment(maxZ + segmentLength);
            }
        }
    }

    // Debug用：在Scene view繪制回收線
    private void OnDrawGizmos()
    {
        if (cameraTransform != null)
        {
            Gizmos.color = Color.red;
            Vector3 recyclePos = cameraTransform.position + Vector3.back * recycleDistance;
            Gizmos.DrawLine(recyclePos + Vector3.left * 10, recyclePos + Vector3.right * 10);
            Gizmos.DrawLine(recyclePos + Vector3.down * 10, recyclePos + Vector3.up * 10);
        }
    }
}