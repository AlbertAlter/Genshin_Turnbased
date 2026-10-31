using UnityEngine;
using UnityEngine.UI;

/// <summary>Single-batch ice fragments. Original UVs retain the frost and card silhouette as fragments move.</summary>
public sealed class UIFrozenShardGraphic : MaskableGraphic
{
    private Texture source;
    private UICardFreezeStyle style;
    private Vector2[] perimeter;
    private float progress;
    public override Texture mainTexture => source != null ? source : Texture2D.whiteTexture;

    public void Configure(Texture texture, UICardFreezeStyle settings)
    {
        source = texture; style = settings; progress = 0;
        perimeter = new Vector2[settings.FragmentCount];
        int steps = settings.FragmentCount / 4;
        for (int i = 0; i < perimeter.Length; i++)
        {
            int edge = i / steps; float t = (i % steps) / (float)steps;
            perimeter[i] = edge == 0 ? new Vector2(t, 0) : edge == 1 ? new Vector2(1, t)
                : edge == 2 ? new Vector2(1 - t, 1) : new Vector2(0, 1 - t);
        }
        SetAllDirty();
    }
    public void SetProgress(float value) { progress = Mathf.Clamp01(value); SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (source == null || style == null || perimeter == null) return;
        Rect rect = rectTransform.rect;
        Vector2 origin = new Vector2(.5f, style.OriginY);
        float travel = 1 - Mathf.Pow(1 - progress, 2);
        // The shared material controls body/detail opacity. Vertex alpha is only the animation fade.
        Color tint = color; tint.a *= (1 - progress) * (1 - progress);
        for (int i = 0; i < perimeter.Length; i++)
        {
            Vector2 a = perimeter[i], b = perimeter[(i + 1) % perimeter.Length];
            Vector2 center = (origin + a + b) / 3;
            Vector2 direction = (center - origin).normalized;
            float variation = .65f + HitEffectMath.Unit(197u, (uint)i) * .7f;
            Vector2 movement = direction * (rect.width * style.TravelWidthRatio * travel * variation)
                + Vector2.down * (rect.height * style.FallHeightRatio * progress * progress);
            float angle = (HitEffectMath.Unit(198u, (uint)i) * 2 - 1) * style.RotationDegrees * travel * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            Vector2 pivot = Point(center);
            int start = vh.currentVertCount;
            Add(origin); Add(a); Add(b); vh.AddTriangle(start, start + 1, start + 2);
            void Add(Vector2 uv)
            {
                Vector2 local = Point(uv) - pivot;
                Vector2 position = pivot + new Vector2(local.x * c - local.y * s, local.x * s + local.y * c) + movement;
                var vertex = UIVertex.simpleVert; vertex.position = position; vertex.color = tint; vertex.uv0 = uv; vh.AddVert(vertex);
            }
        }
        Vector2 Point(Vector2 uv) => new Vector2(rect.xMin + uv.x * rect.width, rect.yMin + uv.y * rect.height);
    }
}
