using UnityEngine;
using UnityEngine.UI;

/// <summary>Straight chevrons with simple vertical tapered motion lines. Owns no textures or materials.</summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class UICardOrbitArrowGraphic : MaskableGraphic
{
    internal bool Front;
    private UICardOrbitArrowStyle style;
    private UICardStatusFlow flow;
    private Rect bounds;
    private Matrix4x4 toLocal;
    private Color tint;
    private float seconds, opacity;
    private readonly Vector2[] polygon = new Vector2[6];
    internal void Present(RectTransform card, UICardOrbitArrowStyle value, UICardStatusFlow direction, Color color, float age, float alpha)
    {
        style = value; flow = direction; bounds = card.rect;
        toLocal = rectTransform.worldToLocalMatrix * card.localToWorldMatrix;
        tint = color; seconds = age; opacity = alpha * value.Opacity; SetVerticesDirty();
    }
    private Color Tint(float alpha, float white = 0)
    { Color c = Color.Lerp(tint, Color.white, white); c.a = tint.a * alpha * opacity; return c * color; }
    private float Weight(float depth) => Front ? UICardOrbitArrowMath.FrontWeight(depth) : 1 - UICardOrbitArrowMath.FrontWeight(depth);
    private void Vertex(VertexHelper mesh, Vector2 point, Color c) => mesh.AddVert(toLocal.MultiplyPoint3x4(point), c, Vector2.zero);
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if (style == null || opacity <= 0 || bounds.width <= 0 || bounds.height <= 0) return;
        float sign = flow == UICardStatusFlow.Buff ? 1 : -1;
        for (int i = 0; i < style.ArrowCount; i++)
        {
            var p = UICardOrbitArrowMath.Sample(style, bounds, i, seconds, flow);
            DrawTrail(mesh, i, p);
            float alpha = p.Opacity * Weight(p.Depth) * Mathf.Lerp(.65f, 1, (p.Depth + 1) * .5f);
            if (alpha < .001f) continue;
            for (int n = 0; n < style.Chevrons; n++)
            {
                var center = p.Position - Vector2.up * sign * n * bounds.height * style.Gap;
                float width = bounds.width * style.Width * p.Scale, height = bounds.height * style.Height;
                Chevron(mesh, center, width, height * sign, alpha * (1 - n * .22f));
            }
        }
    }
    private void Chevron(VertexHelper mesh, Vector2 center, float width, float height, float alpha)
    {
        polygon[0] = new Vector2(-.5f * width, -.5f * height); polygon[1] = new Vector2(0, .5f * height);
        polygon[2] = new Vector2(.5f * width, -.5f * height); polygon[3] = new Vector2(.28f * width, -.5f * height);
        polygon[4] = new Vector2(0, .08f * height); polygon[5] = new Vector2(-.28f * width, -.5f * height);
        int start = mesh.currentVertCount;
        for (int i = 0; i < 6; i++)
        {
            Vertex(mesh, center + polygon[i] * style.GlowScale, Tint(0));
            Vertex(mesh, center + polygon[i], Tint(alpha * style.GlowOpacity));
        }
        for (int i = 0; i < 6; i++)
        { int a = start + i * 2, b = start + (i + 1) % 6 * 2; mesh.AddTriangle(a, a + 1, b); mesh.AddTriangle(a + 1, b + 1, b); }
        start = mesh.currentVertCount;
        for (int i = 0; i < 6; i++) Vertex(mesh, center + polygon[i], Tint(alpha, i == 1 || i == 4 ? style.CoreWhiteMix : .15f));
        mesh.AddTriangle(start, start + 1, start + 5); mesh.AddTriangle(start + 5, start + 1, start + 4);
        mesh.AddTriangle(start + 4, start + 1, start + 2); mesh.AddTriangle(start + 4, start + 2, start + 3);
    }
    private void DrawTrail(VertexHelper mesh, int index, UICardOrbitArrowSample head)
    {
        if (style.Trail <= 0) return;
        const int count = 12;
        int start = mesh.currentVertCount;
        for (int j = 0; j <= count; j++)
        {
            float t = (float)j / count;
            var p = UICardOrbitArrowMath.Sample(style, bounds, index, seconds - (1 - t) * style.Trail * style.TravelSeconds / style.TravelHeight, flow);
            float alpha = p.Opacity * head.Opacity * Weight(p.Depth) * t * t * .45f;
            Vector2 position = p.Position;
            float half = bounds.width * .005f * head.Scale * Mathf.Lerp(.1f, 1, t);
            Vertex(mesh, position - Vector2.right * half * 3, Tint(0));
            Vertex(mesh, position - Vector2.right * half, Tint(alpha));
            Vertex(mesh, position + Vector2.right * half, Tint(alpha));
            Vertex(mesh, position + Vector2.right * half * 3, Tint(0));
            if (j == 0) continue;
            int previous = start + (j - 1) * 4;
            for (int strip = 0; strip < 3; strip++)
            { mesh.AddTriangle(previous + strip, previous + strip + 4, previous + strip + 1); mesh.AddTriangle(previous + strip + 1, previous + strip + 4, previous + strip + 5); }
        }
    }
}
