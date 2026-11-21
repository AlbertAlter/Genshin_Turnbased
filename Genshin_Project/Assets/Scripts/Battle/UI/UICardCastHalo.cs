using UnityEngine;
using UnityEngine.UI;

/// <summary>Owned visual of SelfAnimation 5. Three concentric ellipses; no timer or layout writes.</summary>
public sealed class UICardCastHalo : MaskableGraphic
{
    private float progress = 1, strength;
    public void Sample(float value, float amplitude)
    {
        if (progress == value && strength == amplitude) return;
        progress = value; strength = amplitude; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float alpha = CardAttackMotionMath.CastHaloOpacity(progress, strength);
        if (alpha <= 0) return;
        var rect = rectTransform.rect; if (rect.width <= 0 || rect.height <= 0) return;
        float unit = rect.width / 197f;
        Vector2 center = rect.center + new Vector2(0, -rect.height * (70f / 338f));
        float scale = 0.55f + Mathf.Clamp01(progress) * 1.1f;
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = rect.width * (125f / 197f) * scale * (1 - ring * 0.23f);
            DrawRing(mesh, center, radius, radius * 0.43837115f, 6 * unit, alpha * 0.13f);
            DrawRing(mesh, center, radius, radius * 0.43837115f, 1.8f * unit, alpha * (0.75f + ring * 0.1f));
        }
    }
    private static void DrawRing(VertexHelper mesh, Vector2 center, float xRadius, float yRadius, float thickness, float alpha)
    {
        const int segments = 72;
        var tint = new Color(0.93f, 0.85f, 0.66f, alpha);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
            var pa = center + new Vector2(Mathf.Cos(a) * xRadius, Mathf.Sin(a) * yRadius);
            var pb = center + new Vector2(Mathf.Cos(b) * xRadius, Mathf.Sin(b) * yRadius);
            var na = new Vector2(Mathf.Cos(a) / xRadius, Mathf.Sin(a) / yRadius).normalized * (thickness * 0.5f);
            var nb = new Vector2(Mathf.Cos(b) / xRadius, Mathf.Sin(b) / yRadius).normalized * (thickness * 0.5f);
            int start = mesh.currentVertCount;
            mesh.AddVert(pa + na, tint, Vector2.zero); mesh.AddVert(pa - na, tint, Vector2.zero);
            mesh.AddVert(pb - nb, tint, Vector2.zero); mesh.AddVert(pb + nb, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
