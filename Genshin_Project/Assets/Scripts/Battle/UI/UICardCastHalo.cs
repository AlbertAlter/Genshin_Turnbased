using UnityEngine;
using UnityEngine.UI;

/// <summary>One half of the shared three-height cast halo. Borrows its card and material; owns no clock.</summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class UICardCastHalo : MaskableGraphic
{
    private RectTransform card;
    private bool backHalf;
    private float progress = 1, strength;
    private readonly float[] lightPositions = new float[17], lightAlphas = new float[17];
    private float farY, lightSpan;

    internal void Bind(RectTransform target, bool rear, Material sharedMaterial)
    {
        card = target; backHalf = rear; material = sharedMaterial;
        raycastTarget = false; maskable = false; SetVerticesDirty();
    }
    public void Sample(float value, float amplitude)
    {
        if (progress == value && strength == amplitude) return;
        progress = value; strength = amplitude; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float alpha = CardAttackMotionMath.CastHaloOpacity(progress, strength);
        if (alpha <= 0 || card == null) return;
        var rect = card.rect; if (rect.width <= 0 || rect.height <= 0) return;
        float unit = rect.width / 197f;
        float t = Mathf.Clamp01(progress), radius = rect.width * 0.64f * (0.66f + 0.44f * Smooth(t));
        float spread = Smooth(t / 0.3f), lift = rect.height * (0.025f + 0.03f * Mathf.Sin(t * Mathf.PI));
        var projection = new Projection { Center = rect.center + Vector2.down * (rect.height * 0.46f), Focal = rect.width * 2.85f };
        Matrix4x4 toLocal = rectTransform.worldToLocalMatrix * card.localToWorldMatrix;
        for (int ring = 0; ring < 3; ring++)
        {
            float r = radius * (1 - ring * 0.1f), brightness = alpha * (0.9f + ring * 0.05f);
            projection.Elevation = lift + ring * rect.height * 0.11f * spread;
            SetLighting(projection, r);
            DrawBand(mesh, projection, toLocal, r, 18 * unit, brightness * 0.09f);
            DrawBand(mesh, projection, toLocal, r, 10 * unit, brightness * 0.2f);
            DrawBand(mesh, projection, toLocal, r, 5.5f * unit, brightness);
        }
    }
    private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    private struct Projection
    {
        private static readonly float SinTilt = Mathf.Sin(70 * Mathf.Deg2Rad), CosTilt = Mathf.Cos(70 * Mathf.Deg2Rad);
        public Vector2 Center;
        public float Focal, Elevation;
        public Vector2 Point(float x, float z)
        {
            float scale = Focal / (Focal + z * SinTilt - Elevation * CosTilt);
            return Center + new Vector2(x, z * CosTilt + Elevation * SinTilt) * scale;
        }
    }
    private void SetLighting(Projection projection, float radius)
    {
        farY = projection.Point(0, radius).y; lightSpan = projection.Point(0, -radius).y - farY;
        for (int i = 0; i <= 16; i++)
        {
            float near = i / 16f, z = radius * (1 - 2 * near);
            lightPositions[i] = (projection.Point(0, z).y - farY) / lightSpan;
            lightAlphas[i] = 0.34f + 0.66f * Mathf.Pow(near, 1.3f);
        }
    }
    private Color Tint(float y, float opacity)
    {
        float position = Mathf.Clamp01((y - farY) / lightSpan);
        int next = 1; while (next < 16 && position > lightPositions[next]) next++;
        float weight = Mathf.Lerp(lightAlphas[next - 1], lightAlphas[next],
            Mathf.InverseLerp(lightPositions[next - 1], lightPositions[next], position));
        return new Color(1, 231f / 255, 174f / 255, weight * opacity) * color;
    }
    private void DrawBand(VertexHelper mesh, Projection projection, Matrix4x4 toLocal, float radius, float thickness, float opacity)
    {
        const int segments = 64;
        int start = mesh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (backHalf ? 0 : Mathf.PI) + Mathf.PI * i / segments;
            float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
            var outer = projection.Point(x * (radius + thickness * 0.5f), z * (radius + thickness * 0.5f));
            var inner = projection.Point(x * (radius - thickness * 0.5f), z * (radius - thickness * 0.5f));
            mesh.AddVert(toLocal.MultiplyPoint3x4(outer), Tint(outer.y, opacity), Vector2.zero);
            mesh.AddVert(toLocal.MultiplyPoint3x4(inner), Tint(inner.y, opacity), Vector2.zero);
            if (i == 0) continue;
            int a = start + (i - 1) * 2, b = start + i * 2;
            mesh.AddTriangle(a, a + 1, b + 1); mesh.AddTriangle(a, b + 1, b);
        }
    }
}
