using UnityEngine;
using UnityEngine.UI;

/// <summary>The HTML SVG's flat body, gold rules and six-stop end fade, drawn without a texture.</summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class UIStatusNameBackdrop : MaskableGraphic
{
    private UIStatusNamePopupStyle style;
    public void Configure(UIStatusNamePopupStyle value)
    { style = value; raycastTarget = false; maskable = false; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if (style == null) return;
        Rect rect = rectTransform.rect;
        float border = Mathf.Min(style.GoldBorderWidth, rect.height * .5f);
        var body = style.BackgroundColor; body.a = style.BackgroundOpacity;
        var edge = style.GoldColor;
        float alpha = style.GoldBorderOpacity + body.a * (1 - style.GoldBorderOpacity);
        edge = alpha > 0 ? (edge * style.GoldBorderOpacity + body * (body.a * (1 - style.GoldBorderOpacity))) / alpha : Color.clear;
        edge.a = alpha;
        // Composite the gold over the body before masking; overlapping translucent quads would fade twice.
        Band(rect.yMin + border, rect.yMax - border, body);
        if (border > 0) { Band(rect.yMin, rect.yMin + border, edge); Band(rect.yMax - border, rect.yMax, edge); }
        void Band(float bottom, float top, Color tint)
        {
            float fade = style.EndFadeRatio;
            if (fade <= 0) { Quad(0, 1, 1, 1); return; }
            Quad(0, fade * .4f, 0, .35f); Quad(fade * .4f, fade, .35f, 1);
            Quad(fade, 1 - fade, 1, 1); Quad(1 - fade, 1 - fade * .4f, 1, .35f); Quad(1 - fade * .4f, 1, .35f, 0);
            void Quad(float left, float right, float leftAlpha, float rightAlpha)
            {
                Color a = tint * color, b = a; a.a *= leftAlpha; b.a *= rightAlpha;
                int start = mesh.currentVertCount;
                mesh.AddVert(new Vector3(rect.xMin + rect.width * left, bottom), a, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin + rect.width * left, top), a, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin + rect.width * right, top), b, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin + rect.width * right, bottom), b, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
