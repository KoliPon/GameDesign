using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⭐ UI 線條渲染器 - 在 Canvas 2D UI 上繪製線條
/// </summary>
public class CanvasLineRenderer : Graphic
{
    private List<Vector2> linePoints = new List<Vector2>();
    public float lineWidth = 5f;
    public Color lineColor = Color.cyan;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (linePoints.Count < 2)
            return;

        for (int i = 0; i < linePoints.Count - 1; i++)
        {
            DrawLineSegment(vh, linePoints[i], linePoints[i + 1]);
        }
    }

    private void DrawLineSegment(VertexHelper vh, Vector2 start, Vector2 end)
    {
        Vector2 dir = (end - start).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x) * (lineWidth / 2f);

        UIVertex[] vertices = new UIVertex[4];
        vertices[0].position = start - perp;
        vertices[1].position = start + perp;
        vertices[2].position = end + perp;
        vertices[3].position = end - perp;

        for (int i = 0; i < 4; i++)
        {
            vertices[i].color = lineColor;
        }

        vh.AddUIVertexQuad(vertices);
    }

    public void UpdateLine(List<Vector2> points)
    {
        linePoints = new List<Vector2>(points);
        SetVerticesDirty();
    }

    public void ClearLine()
    {
        linePoints.Clear();
        SetVerticesDirty();
    }
}
