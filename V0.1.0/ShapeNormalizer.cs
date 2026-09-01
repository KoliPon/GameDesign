using System.Collections.Generic;
using UnityEngine;

public static class ShapeNormalizer
{
    public static List<Vector2> FillGaps(IList<Vector2> points, float maximumSegmentLength)
    {
        List<Vector2> filled = new List<Vector2>();
        if (points == null || points.Count == 0)
            return filled;

        maximumSegmentLength = Mathf.Max(0.01f, maximumSegmentLength);
        filled.Add(points[0]);

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 previous = points[i - 1];
            Vector2 current = points[i];
            int segments = Mathf.CeilToInt(Vector2.Distance(previous, current) / maximumSegmentLength);

            for (int segment = 1; segment <= segments; segment++)
                filled.Add(Vector2.Lerp(previous, current, segment / (float)segments));
        }

        return filled;
    }

    public static List<Vector2> Normalize(IList<Vector2> points, float smoothingStrength)
    {
        if (points == null || points.Count < 3)
            return points == null ? new List<Vector2>() : new List<Vector2>(points);

        Vector2 centroid = Vector2.zero;
        foreach (Vector2 point in points)
            centroid += point;
        centroid /= points.Count;

        float xx = 0f;
        float xy = 0f;
        float yy = 0f;
        foreach (Vector2 point in points)
        {
            Vector2 centered = point - centroid;
            xx += centered.x * centered.x;
            xy += centered.x * centered.y;
            yy += centered.y * centered.y;
        }

        float angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
        float cosine = Mathf.Cos(-angle);
        float sine = Mathf.Sin(-angle);
        List<Vector2> rotated = new List<Vector2>(points.Count);
        foreach (Vector2 point in points)
        {
            Vector2 centered = point - centroid;
            rotated.Add(new Vector2(
                centered.x * cosine - centered.y * sine,
                centered.x * sine + centered.y * cosine));
        }

        return SmoothPreservingCorners(rotated, smoothingStrength);
    }

    public static int CountCorners(IList<Vector2> points, float minimumTurnAngle)
    {
        if (points == null || points.Count < 5)
            return 0;

        int corners = 0;
        const int stride = 2;
        for (int i = stride; i < points.Count - stride; i++)
        {
            Vector2 before = points[i] - points[i - stride];
            Vector2 after = points[i + stride] - points[i];
            if (before.sqrMagnitude > 0.0001f && after.sqrMagnitude > 0.0001f &&
                Vector2.Angle(before, after) >= minimumTurnAngle)
                corners++;
        }

        return corners;
    }

    private static List<Vector2> SmoothPreservingCorners(IList<Vector2> points, float smoothingStrength)
    {
        smoothingStrength = Mathf.Clamp01(smoothingStrength);
        List<Vector2> smoothed = new List<Vector2>(points);
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 before = points[i] - points[i - 1];
            Vector2 after = points[i + 1] - points[i];
            if (before.sqrMagnitude < 0.0001f || after.sqrMagnitude < 0.0001f ||
                Vector2.Angle(before, after) >= 45f)
                continue;

            Vector2 average = (points[i - 1] + points[i] * 2f + points[i + 1]) * 0.25f;
            smoothed[i] = Vector2.Lerp(points[i], average, smoothingStrength);
        }

        return smoothed;
    }
}
