using UnityEngine;
using UnityEngine.UI;

/// <summary>短暂碰撞环与六根火花，无文字、贴图分配或输入。</summary>
public sealed class ElementAuraCollisionFlash : MaskableGraphic
{
    private float strength;
    public void Set(float value, Color tint) { strength = value; color = tint; raycastTarget = false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (strength <= 0) return;
        Vector2 center = rectTransform.rect.center;
        float size = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);
        float radius = size * (.55f + (1 - strength) * .25f), width = size * .035f;
        Color tint = color; tint.a *= strength;
        Vector2 Point(float angle, float r) => center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
        }
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2 / 40, b = (i + 1) * Mathf.PI * 2 / 40;
            Quad(Point(a, radius - width), Point(a, radius), Point(b, radius), Point(b, radius - width));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3;
            Vector2 offset = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * width * .6f;
            var start = Point(a, radius * .85f); var end = Point(a, radius * 1.2f);
            Quad(start - offset, start + offset, end + offset, end - offset);
        }
    }
}
