using System;
using System.Collections.Generic;
using UnityEngine;

public static class OneDollarRecognizer
{
    private const int NumPoints = 32;
    private static readonly Vector2 SquareSize = new Vector2(100f, 100f);
    private static readonly float GoldenRatio = 0.5f * (Mathf.Sqrt(5f) - 1f);

    public class GestureTemplate
    {
        public string Name;
        public List<Vector2> Points;

        public GestureTemplate(string name, List<Vector2> rawPoints)
        {
            Name = name;
            Points = OneDollarRecognizer.ProcessPoints(rawPoints);
        }
    }

    public static List<Vector2> ProcessPoints(List<Vector2> points)
    {
        List<Vector2> resampled = Resample(points, NumPoints);
        RotateToZero(resampled);
        List<Vector2> scaled = ScaleTo(resampled, SquareSize);
        List<Vector2> translated = TranslateTo(scaled, Vector2.zero);
        return translated;
    }

    /// <summary>
    /// 改進的分類方法，支援更好的形狀辨識
    /// 返回識別出的圖形名稱與信心度
    /// </summary>
    public static string Classify(List<Vector2> candidatePoints, List<GestureTemplate> templates, out float outScore)
    {
        outScore = 0f;
        if (candidatePoints.Count < 10 || templates.Count == 0) return "None";

        // 📊 收集幾何特徵
        float actualPathLength = PathLength(candidatePoints);
        Rect bBox = BoundingBox(candidatePoints);
        float bBoxPerimeter = (bBox.width + bBox.height) * 2f;
        float perimeterRatio = bBoxPerimeter > 0 ? (actualPathLength / bBoxPerimeter) : 0f;

        float aspect = bBox.height > 0 ? (bBox.width / bBox.height) : 1f;
        float aspectDeviation = Mathf.Abs(aspect - 1f);
        bool isRoughlySquareBox = (aspect > 0.7f && aspect < 1.4f);

        List<Vector2> candidate = ProcessPoints(candidatePoints);
        int cornerCount = CountSharpCorners(candidate, 35f);
        float curvedness = CalculateCurvedness(candidate);  // 新增：曲線度指標

        Debug.Log($"幾何特徵 - 周長比: {perimeterRatio:F2}, 角點: {cornerCount}, 曲線度: {curvedness:F2}, 寬高比: {aspect:F2}");

        // 🎯 範本匹配
        float bestDistance = float.MaxValue;
        string bestName = "None";

        foreach (var template in templates)
        {
            float dist = DistanceAtBestAngle(candidate, template.Points, -45f, 45f, 2f);
            Debug.Log($"範本 {template.Name}: 距離 = {dist:F2}");

            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestName = template.Name;
            }
        }

        // 🔍 後置修正：利用幾何特徵改進辨識
        if (bestName == "Circle")
        {
            // 高周長比 + 低角點數 = 圓形，否則可能是其他形狀
            if (perimeterRatio < 0.85f || cornerCount >= 2)
            {
                // 根據角點數和寬高比判定是正方形還是三角形
                bestName = (cornerCount >= 3 || (cornerCount >= 2 && isRoughlySquareBox)) ? "Square" : "Triangle";
                Debug.Log($"圓形後置修正 → {bestName}");
            }
        }
        else if (bestName == "Square")
        {
            // 如果寬高比偏離太大或角點太少，可能是三角形
            if (aspectDeviation > 0.5f && cornerCount < 2)
            {
                bestName = "Triangle";
                Debug.Log($"正方形後置修正 → 三角形");
            }
        }
        else if (bestName == "Triangle")
        {
            // 如果角點太多，可能是正方形或圓形
            if (cornerCount >= 4)
            {
                bestName = isRoughlySquareBox ? "Square" : "Circle";
                Debug.Log($"三角形後置修正 → {bestName}");
            }
        }

        // 計算信心度 (0-1，1 為最高)
        float halfDiagonal = Mathf.Sqrt(SquareSize.x * SquareSize.x + SquareSize.y * SquareSize.y) * 0.5f;
        outScore = Mathf.Max((halfDiagonal - bestDistance) / halfDiagonal, 0f);

        // ⚡ 信心度過低時降低分數
        if (outScore < 0.5f)
        {
            Debug.LogWarning($"低信心度辨識: {bestName} ({outScore:F2})");
        }

        return bestName;
    }

    /// <summary>
    /// 計算曲線度指標（用於區分圓形和多邊形）
    /// 值越高表示越彎曲（圓形），值越低表示越棱角分明（多邊形）
    /// </summary>
    private static float CalculateCurvedness(List<Vector2> points)
    {
        if (points.Count < 3) return 0f;

        float totalAngleChange = 0f;
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 v1 = (points[i] - points[i - 1]).normalized;
            Vector2 v2 = (points[i + 1] - points[i]).normalized;
            float angle = Vector2.Angle(v1, v2);
            totalAngleChange += angle;
        }

        // 歸一化到 0-1 之間
        float avgAngleChange = totalAngleChange / (points.Count - 2);
        return Mathf.Clamp01(avgAngleChange / 180f);
    }

    private static int CountSharpCorners(List<Vector2> points, float angleThreshold)
    {
        if (points.Count < 5) return 0;
        int count = 0;
        for (int i = 2; i < points.Count - 2; i++)
        {
            Vector2 v1 = (points[i] - points[i - 2]).normalized;
            Vector2 v2 = (points[i + 2] - points[i]).normalized;
            float angle = Vector2.Angle(v1, v2);

            if (angle > angleThreshold)
            {
                count++;
                i += 2;
            }
        }
        return count;
    }

    /// <summary>
    /// 改進的重新採樣方法，保留更多細節
    /// </summary>
    private static List<Vector2> Resample(List<Vector2> points, int n)
    {
        float I = PathLength(points) / (n - 1);
        float D = 0f;
        List<Vector2> newPoints = new List<Vector2> { points[0] };
        List<Vector2> srcPoints = new List<Vector2>(points);

        for (int i = 1; i < srcPoints.Count; i++)
        {
            float d = Vector2.Distance(srcPoints[i - 1], srcPoints[i]);
            if ((D + d) >= I)
            {
                float t = (I - D) / d;
                Vector2 q = Vector2.Lerp(srcPoints[i - 1], srcPoints[i], t);
                newPoints.Add(q);
                srcPoints.Insert(i, q);
                D = 0f;
            }
            else D += d;
        }
        if (newPoints.Count == n - 1) newPoints.Add(srcPoints[srcPoints.Count - 1]);
        return newPoints;
    }

    private static float PathLength(List<Vector2> points)
    {
        float d = 0f;
        for (int i = 1; i < points.Count; i++) d += Vector2.Distance(points[i - 1], points[i]);
        return d;
    }

    private static void RotateToZero(List<Vector2> points)
    {
        Vector2 c = Centroid(points);
        float radians = Mathf.Atan2(c.y - points[0].y, c.x - points[0].x);
        RotateBy(points, -radians);
    }

    private static void RotateBy(List<Vector2> points, float radians)
    {
        Vector2 c = Centroid(points);
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        for (int i = 0; i < points.Count; i++)
        {
            float qx = (points[i].x - c.x) * cos - (points[i].y - c.y) * sin + c.x;
            float qy = (points[i].x - c.x) * sin + (points[i].y - c.y) * cos + c.y;
            points[i] = new Vector2(qx, qy);
        }
    }

    private static List<Vector2> ScaleTo(List<Vector2> points, Vector2 size)
    {
        Rect B = BoundingBox(points);
        List<Vector2> newPoints = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            float qx = points[i].x * (B.width == 0 ? 1 : (size.x / B.width));
            float qy = points[i].y * (B.height == 0 ? 1 : (size.y / B.height));
            newPoints.Add(new Vector2(qx, qy));
        }
        return newPoints;
    }

    private static List<Vector2> TranslateTo(List<Vector2> points, Vector2 pt)
    {
        Vector2 c = Centroid(points);
        List<Vector2> newPoints = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            float qx = points[i].x + pt.x - c.x;
            float qy = points[i].y + pt.y - c.y;
            newPoints.Add(new Vector2(qx, qy));
        }
        return newPoints;
    }

    private static Vector2 Centroid(List<Vector2> points)
    {
        Vector2 c = Vector2.zero;
        foreach (var p in points) c += p;
        return c / points.Count;
    }

    private static Rect BoundingBox(List<Vector2> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static float DistanceAtBestAngle(List<Vector2> points, List<Vector2> T, float a, float b, float threshold)
    {
        float x1 = GoldenRatio * a + (1f - GoldenRatio) * b;
        float f1 = DistanceAtAngle(points, T, x1);
        float x2 = (1f - GoldenRatio) * a + GoldenRatio * b;
        float f2 = DistanceAtAngle(points, T, x2);

        while (Mathf.Abs(b - a) > threshold)
        {
            if (f1 < f2) { b = x2; x2 = x1; f2 = f1; x1 = GoldenRatio * a + (1f - GoldenRatio) * b; f1 = DistanceAtAngle(points, T, x1); }
            else { a = x1; x1 = x2; f1 = f2; x2 = (1f - GoldenRatio) * a + GoldenRatio * b; f2 = DistanceAtAngle(points, T, x2); }
        }
        return Mathf.Min(f1, f2);
    }

    private static float DistanceAtAngle(List<Vector2> points, List<Vector2> T, float radians)
    {
        List<Vector2> newPoints = new List<Vector2>(points);
        RotateBy(newPoints, radians);
        return PathDistance(newPoints, T);
    }

    private static float PathDistance(List<Vector2> pts1, List<Vector2> pts2)
    {
        float d = 0f;
        for (int i = 0; i < pts1.Count; i++) d += Vector2.Distance(pts1[i], pts2[i]);
        return d / pts1.Count;
    }
}
