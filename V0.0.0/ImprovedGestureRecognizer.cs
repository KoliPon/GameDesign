using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ⭐ 改進的手勢識別器 - 支持潦草/變形圖形
/// 結合多種幾何特徵分析，提高識別準確率
/// </summary>
public static class ImprovedGestureRecognizer
{
    // ========== 識別參數配置 ==========
    [System.Serializable]
    public class RecognitionConfig
    {
        [Range(0f, 1f)] public float circleRoundnessThreshold = 0.55f;    // 圓形圓度閾值
        [Range(0f, 1f)] public float squareAngularityThreshold = 0.50f;   // 正方形角度閾值
        [Range(0f, 1f)] public float triangleSharpnessThreshold = 0.48f;  // 三角形尖銳度閾值
        [Range(0f, 1f)] public float oneJollarFallbackThreshold = 0.45f;  // One Dollar 備選閾值
        [Range(0f, 1f)] public float minAspectRatioForSquare = 0.65f;     // 正方形最小寬高比

        public bool enableDebugLog = true;
    }

    private static RecognitionConfig config = new RecognitionConfig();

    // ========== 核心識別方法 ==========
    /// <summary>
    /// 改進的分類方法 - 支持潦草圖形
    /// 返回識別的圖形名稱與信心度
    /// </summary>
    public static string ClassifyImproved(
        List<Vector2> candidatePoints,
        List<OneDollarRecognizer.GestureTemplate> templates,
        out float confidence,
        RecognitionConfig cfg = null)
    {
        if (cfg != null) config = cfg;
        confidence = 0f;

        if (candidatePoints.Count < 10 || templates.Count == 0)
            return "None";

        // ========== 第一階段：幾何特徵提取 ==========
        GeometricFeatures features = AnalyzeGeometricFeatures(candidatePoints);

        if (config.enableDebugLog)
        {
            Debug.Log($"[識別] 幾何特徵:");
            Debug.Log($"  - 圓度: {features.roundness:F3}");
            Debug.Log($"  - 角度: {features.angularity:F3}");
            Debug.Log($"  - 尖銳: {features.sharpness:F3}");
            Debug.Log($"  - 寬高比: {features.aspectRatio:F3}");
            Debug.Log($"  - 周長比: {features.perimeterRatio:F3}");
        }

        // ========== 第二階段：基於特徵的初步判定 ==========
        string preliminaryResult = ClassifyByGeometricFeatures(features);

        // ========== 第三階段：One Dollar 模板匹配（作為備選） ==========
        List<Vector2> processedPoints = OneDollarRecognizer.ProcessPoints(candidatePoints);
        string oneJollarResult = ClassifyByTemplateMatching(processedPoints, templates, out float oneJollarScore);

        // ========== 第四階段：融合決策 ==========
        string finalResult = FusionDecision(preliminaryResult, features, oneJollarResult, oneJollarScore, out confidence);

        if (config.enableDebugLog)
        {
            Debug.Log($"[識別] 最終結果: {finalResult} (信心: {confidence:F3})");
        }

        return finalResult;
    }

    // ========== 幾何特徵結構 ==========
    private struct GeometricFeatures
    {
        public float roundness;        // 圓度 (0-1)
        public float angularity;       // 角度 (0-1)
        public float sharpness;        // 尖銳度 (0-1)
        public float aspectRatio;      // 寬高比 (0-1)
        public float perimeterRatio;   // 周長比 (接近1為圓形)
        public float pathStraightness; // 路徑直度
        public int estimatedCorners;   // 估計角點數
    }

    // ========== 幾何特徵分析 ==========
    private static GeometricFeatures AnalyzeGeometricFeatures(List<Vector2> points)
    {
        GeometricFeatures features = new GeometricFeatures();

        if (points.Count < 5) return features;

        // 1. 計算邊界框和寬高比
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width == 0 || height == 0) return features;

        features.aspectRatio = Mathf.Min(width, height) / Mathf.Max(width, height);

        // 2. 計算圓度
        Vector2 center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        float avgDistance = 0f;
        float distanceVariance = 0f;

        foreach (var p in points)
            avgDistance += Vector2.Distance(p, center);
        avgDistance /= points.Count;

        foreach (var p in points)
        {
            float dist = Vector2.Distance(p, center);
            distanceVariance += (dist - avgDistance) * (dist - avgDistance);
        }
        distanceVariance = Mathf.Sqrt(distanceVariance / points.Count);

        float circularity = features.aspectRatio * (1f - Mathf.Clamp01(distanceVariance / (avgDistance + 0.001f)));
        features.roundness = Mathf.Clamp01(circularity);

        // 3. 計算周長比（用於區分圓形）
        float pathLength = CalculatePathLength(points);
        float bBoxPerimeter = (width + height) * 2f;
        features.perimeterRatio = bBoxPerimeter > 0 ? (pathLength / bBoxPerimeter) : 0f;

        // 4. 計算角度和尖銳度
        (features.angularity, features.estimatedCorners) = AnalyzeCorners(points);
        features.sharpness = AnalyzeSharpness(points);

        // 5. 計算路徑直度
        features.pathStraightness = AnalyzePathStraightness(points);

        return features;
    }

    // ========== 圓角分析 ==========
    private static (float angularity, int corners) AnalyzeCorners(List<Vector2> points)
    {
        if (points.Count < 5) return (0f, 0);

        // 採樣均勻的點，避免噪聲
        List<Vector2> sampledPoints = new List<Vector2>();
        int sampleStep = Mathf.Max(1, points.Count / 16);
        for (int i = 0; i < points.Count; i += sampleStep)
            sampledPoints.Add(points[i]);

        if (sampledPoints.Count < 3) return (0f, 0);

        int cornerCount = 0;
        float totalAngleChange = 0f;
        int angleCount = 0;

        // 使用 Ramer-Douglas-Peucker 簡化
        List<Vector2> simplified = SimplifyPath(sampledPoints, 5f);

        for (int i = 1; i < simplified.Count - 1; i++)
        {
            Vector2 v1 = (simplified[i] - simplified[i - 1]).normalized;
            Vector2 v2 = (simplified[i + 1] - simplified[i]).normalized;

            float angle = Vector2.Angle(v1, v2);
            totalAngleChange += angle;
            angleCount++;

            // 檢測角點（60-130 度）
            if (angle > 60f && angle < 130f)
                cornerCount++;
        }

        float avgAngle = angleCount > 0 ? (totalAngleChange / angleCount) : 0f;
        float cornerRatio = Mathf.Clamp01(cornerCount / 4f);  // 正方形有 4 個角
        float angleConsistency = 1f - Mathf.Abs((avgAngle - 90f) / 90f);

        float angularity = Mathf.Lerp(cornerRatio, angleConsistency, 0.5f);
        return (Mathf.Clamp01(angularity), cornerCount);
    }

    // ========== 尖銳度分析（三角形） ==========
    private static float AnalyzeSharpness(List<Vector2> points)
    {
        if (points.Count < 5) return 0f;

        List<Vector2> sampledPoints = new List<Vector2>();
        int sampleStep = Mathf.Max(1, points.Count / 20);
        for (int i = 0; i < points.Count; i += sampleStep)
            sampledPoints.Add(points[i]);

        if (sampledPoints.Count < 3) return 0f;

        List<Vector2> simplified = SimplifyPath(sampledPoints, 3f);

        int sharpCorners = 0;
        float totalAngle = 0f;
        int angleCount = 0;

        for (int i = 1; i < simplified.Count - 1; i++)
        {
            Vector2 v1 = (simplified[i] - simplified[i - 1]).normalized;
            Vector2 v2 = (simplified[i + 1] - simplified[i]).normalized;

            float angle = Vector2.Angle(v1, v2);
            angleCount++;

            // 尖角（> 70 度）
            if (angle > 70f)
            {
                sharpCorners++;
                totalAngle += angle;
            }
        }

        if (angleCount == 0) return 0f;

        // 三角形應該有 3 個尖角
        float cornerRatio = Mathf.Clamp01(sharpCorners / 3f);
        float avgAngle = totalAngle / Mathf.Max(1, sharpCorners);
        float angleSharpness = Mathf.Clamp01((avgAngle - 70f) / 90f);

        float sharpness = Mathf.Lerp(cornerRatio, angleSharpness, 0.6f);
        return Mathf.Clamp01(sharpness);
    }

    // ========== 路徑直度分析 ==========
    private static float AnalyzePathStraightness(List<Vector2> points)
    {
        if (points.Count < 3) return 0f;

        float totalLength = 0f;
        float cumulativeDeviation = 0f;

        Vector2 start = points[0];
        Vector2 end = points[points.Count - 1];
        float directDistance = Vector2.Distance(start, end);

        if (directDistance < 0.001f) return 0f;

        Vector2 direction = (end - start).normalized;

        for (int i = 1; i < points.Count; i++)
        {
            totalLength += Vector2.Distance(points[i - 1], points[i]);

            // 計算點到直線的偏離度
            Vector2 toPoint = points[i] - start;
            float projection = Vector2.Dot(toPoint, direction);
            Vector2 closest = start + direction * projection;
            cumulativeDeviation += Vector2.Distance(points[i], closest);
        }

        float avgDeviation = cumulativeDeviation / points.Count;
        float straightness = 1f / (1f + avgDeviation / (directDistance + 0.001f));

        return Mathf.Clamp01(straightness);
    }

    // ========== 基於幾何特徵的初步判定 ==========
    private static string ClassifyByGeometricFeatures(GeometricFeatures features)
    {
        // 優先級：圓形 > 三角形 > 正方形

        // 1. 圓形判定
        if (features.roundness > config.circleRoundnessThreshold &&
            features.estimatedCorners <= 1 &&
            features.perimeterRatio < 0.9f)
        {
            return "Circle";
        }

        // 2. 三角形判定
        if (features.sharpness > config.triangleSharpnessThreshold &&
            features.estimatedCorners >= 2 && features.estimatedCorners <= 4)
        {
            return "Triangle";
        }

        // 3. 正方形判定
        if (features.angularity > config.squareAngularityThreshold &&
            features.aspectRatio > config.minAspectRatioForSquare &&
            features.estimatedCorners >= 3)
        {
            return "Square";
        }

        // 4. 備選判定（基於最高分數）
        if (features.roundness > features.angularity &&
            features.roundness > features.sharpness)
        {
            return "Circle";
        }

        if (features.sharpness > features.angularity)
        {
            return "Triangle";
        }

        return "Square";
    }

    // ========== One Dollar 模板匹配 ==========
    private static string ClassifyByTemplateMatching(
        List<Vector2> processedPoints,
        List<OneDollarRecognizer.GestureTemplate> templates,
        out float bestScore)
    {
        bestScore = 0f;
        if (templates.Count == 0) return "None";

        float bestDistance = float.MaxValue;
        string bestName = "None";

        foreach (var template in templates)
        {
            float dist = OneDollarRecognizerExtensions.DistanceAtBestAngle(processedPoints, template.Points, -45f, 45f, 2f);

            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestName = template.Name;
            }
        }

        float halfDiagonal = Mathf.Sqrt(100f * 100f + 100f * 100f) * 0.5f;
        bestScore = Mathf.Max((halfDiagonal - bestDistance) / halfDiagonal, 0f);

        return bestName;
    }

    // ========== 融合決策 ==========
    private static string FusionDecision(
        string geometricResult,
        GeometricFeatures features,
        string oneJollarResult,
        float oneJollarScore,
        out float finalConfidence)
    {
        finalConfidence = 0f;

        // 如果幾何特徵很明確，使用幾何結果
        float maxGeometricScore = Mathf.Max(features.roundness, features.angularity, features.sharpness);

        if (maxGeometricScore > 0.65f)  // 高信心度
        {
            finalConfidence = maxGeometricScore;
            return geometricResult;
        }

        // 如果 One Dollar 結果很好，使用 One Dollar
        if (oneJollarScore > config.oneJollarFallbackThreshold)
        {
            finalConfidence = oneJollarScore;
            return oneJollarResult;
        }

        // 混合判定：綜合幾何特徵和 One Dollar
        if (geometricResult == oneJollarResult)
        {
            // 一致：高信心
            finalConfidence = Mathf.Lerp(maxGeometricScore, oneJollarScore, 0.5f);
            return geometricResult;
        }

        // 不一致：選擇分數更高的
        if (maxGeometricScore > oneJollarScore)
        {
            finalConfidence = maxGeometricScore;
            return geometricResult;
        }
        else
        {
            finalConfidence = oneJollarScore;
            return oneJollarResult;
        }
    }

    // ========== 輔助方法 ==========

    private static float CalculatePathLength(List<Vector2> points)
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    /// <summary>
    /// Ramer-Douglas-Peucker 路徑簡化算法
    /// 用於減少噪聲點
    /// </summary>
    private static List<Vector2> SimplifyPath(List<Vector2> points, float epsilon)
    {
        if (points.Count < 3) return new List<Vector2>(points);

        float maxDist = 0f;
        int maxIndex = 0;

        Vector2 start = points[0];
        Vector2 end = points[points.Count - 1];
        Vector2 direction = (end - start).normalized;
        float segmentLength = Vector2.Distance(start, end);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 toPoint = points[i] - start;
            float projection = Vector2.Dot(toPoint, direction);
            Vector2 closest = start + direction * Mathf.Clamp01(projection / segmentLength);
            float dist = Vector2.Distance(points[i], closest);

            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist > epsilon)
        {
            List<Vector2> left = SimplifyPath(new List<Vector2>(points.GetRange(0, maxIndex + 1)), epsilon);
            List<Vector2> right = SimplifyPath(new List<Vector2>(points.GetRange(maxIndex, points.Count - maxIndex)), epsilon);

            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return new List<Vector2> { start, end };
    }

    // ========== 公開方法：調整配置 ==========
    public static void SetConfiguration(RecognitionConfig cfg)
    {
        config = cfg;
    }

    public static RecognitionConfig GetConfiguration()
    {
        return config;
    }
}

// ========== OneDollarRecognizer 擴展方法 ==========
public static class OneDollarRecognizerExtensions
{
    public static float DistanceAtBestAngle(
        List<Vector2> points,
        List<Vector2> T,
        float a, float b,
        float threshold)
    {
        float goldenRatio = 0.5f * (Mathf.Sqrt(5f) - 1f);

        float x1 = goldenRatio * a + (1f - goldenRatio) * b;
        float f1 = DistanceAtAngle(points, T, x1);
        float x2 = (1f - goldenRatio) * a + goldenRatio * b;
        float f2 = DistanceAtAngle(points, T, x2);

        while (Mathf.Abs(b - a) > threshold)
        {
            if (f1 < f2)
            {
                b = x2;
                x2 = x1;
                f2 = f1;
                x1 = goldenRatio * a + (1f - goldenRatio) * b;
                f1 = DistanceAtAngle(points, T, x1);
            }
            else
            {
                a = x1;
                x1 = x2;
                f1 = f2;
                x2 = (1f - goldenRatio) * a + goldenRatio * b;
                f2 = DistanceAtAngle(points, T, x2);
            }
        }

        return Mathf.Min(f1, f2);
    }

    private static float DistanceAtAngle(List<Vector2> points, List<Vector2> T, float radians)
    {
        List<Vector2> newPoints = new List<Vector2>(points);
        RotateBy(newPoints, radians);
        return PathDistance(newPoints, T);
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

    private static Vector2 Centroid(List<Vector2> points)
    {
        Vector2 c = Vector2.zero;
        foreach (var p in points) c += p;
        return c / points.Count;
    }

    private static float PathDistance(List<Vector2> pts1, List<Vector2> pts2)
    {
        float d = 0f;
        for (int i = 0; i < pts1.Count; i++)
            d += Vector2.Distance(pts1[i], pts2[i]);
        return d / pts1.Count;
    }
}