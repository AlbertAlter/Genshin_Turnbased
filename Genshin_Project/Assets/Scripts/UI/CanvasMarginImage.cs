using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Draws a screen-aligned image around a centered content frame.</summary>
public sealed class CanvasMarginImage : Image
{
    [SerializeField] private RectTransform contentFrame;

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        if (contentFrame == null) return;

        Rect screen = rectTransform.rect;
        if (screen.width <= 0f || screen.height <= 0f) return;

        Vector2 innerSize = Vector2.Min(screen.size, contentFrame.rect.size);
        Rect inner = new Rect(screen.center - innerSize * .5f, innerSize);
        Sprite active = overrideSprite;
        Vector4 uv = active != null ? DataUtility.GetOuterUV(active) : new Vector4(0, 0, 1, 1);
        AddStrip(vertices, screen, uv, screen.xMin, screen.yMin, inner.xMin, screen.yMax);
        AddStrip(vertices, screen, uv, inner.xMax, screen.yMin, screen.xMax, screen.yMax);
        AddStrip(vertices, screen, uv, inner.xMin, screen.yMin, inner.xMax, inner.yMin);
        AddStrip(vertices, screen, uv, inner.xMin, inner.yMax, inner.xMax, screen.yMax);
    }

    private void AddStrip(VertexHelper vertices, Rect screen, Vector4 uv, float left, float bottom, float right, float top)
    {
        // Ignore subpixel rounding when the reference and display aspect ratios match.
        if (right - left < .01f || top - bottom < .01f) return;

        float u0 = Mathf.LerpUnclamped(uv.x, uv.z, (left - screen.xMin) / screen.width);
        float u1 = Mathf.LerpUnclamped(uv.x, uv.z, (right - screen.xMin) / screen.width);
        float v0 = Mathf.LerpUnclamped(uv.y, uv.w, (bottom - screen.yMin) / screen.height);
        float v1 = Mathf.LerpUnclamped(uv.y, uv.w, (top - screen.yMin) / screen.height);
        int index = vertices.currentVertCount;
        vertices.AddVert(new Vector3(left, bottom), color, new Vector2(u0, v0));
        vertices.AddVert(new Vector3(left, top), color, new Vector2(u0, v1));
        vertices.AddVert(new Vector3(right, top), color, new Vector2(u1, v1));
        vertices.AddVert(new Vector3(right, bottom), color, new Vector2(u1, v0));
        vertices.AddTriangle(index, index + 1, index + 2);
        vertices.AddTriangle(index + 2, index + 3, index);
    }
}
