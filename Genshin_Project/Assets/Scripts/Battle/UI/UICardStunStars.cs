using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared stars above the card: fixed for actual stun on either side, remaining clicks for ally airborne.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UICardStunStars : MaskableGraphic
{
    [SerializeField, Min(.1f)] private float secondsPerOrbit = 1.4f;
    [SerializeField, Range(20f, 120f)] private float trailDegrees = 85f;
    [SerializeField, Range(0f, 1f)] private float trailOpacity = .8f;
    private const float FullTurn = 2f * Mathf.PI;
    private const int TrailSegments = 24;
    private int visibleStars;
    private float phase;
    private readonly int[] drawOrder = new int[AllyControlRelease.RequiredClicks];
    private readonly float[] depths = new float[AllyControlRelease.RequiredClicks];

    public void SetRemaining(int remaining)
    {
        remaining = Mathf.Clamp(remaining, 0, AllyControlRelease.RequiredClicks);
        if (visibleStars == remaining) return;
        if (visibleStars == 0) phase = .35f;
        visibleStars = remaining;
        gameObject.SetActive(remaining > 0);
        if (remaining > 0) SetVerticesDirty();
    }

    private void Update()
    {
        phase = Mathf.Repeat(phase + BattlePause.DeltaTime / Mathf.Max(.1f, secondsPerOrbit), 1f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect bounds = rectTransform.rect;
        if (visibleStars == 0 || bounds.width <= 0 || bounds.height <= 0) return;
        // Stable orbit seats: removing one star never redistributes the survivors.
        for (int i = 0; i < visibleStars; i++)
        {
            depths[i] = Mathf.Sin(Angle(i));
            int j = i;
            while (j > 0 && depths[drawOrder[j - 1]] > depths[i])
            { drawOrder[j] = drawOrder[j - 1]; j--; }
            drawOrder[j] = i;
        }
        for (int seat = 0; seat < visibleStars; seat++)
        {
            float angle = Angle(drawOrder[seat]);
            float brightness = Mathf.Lerp(.45f, 1f, (Mathf.Sin(angle) + 1f) * .5f);
            float length = Mathf.Clamp(trailDegrees, 20f, 120f) * Mathf.Deg2Rad;
            float width = bounds.height * .045f;
            // A broad fading ribbon follows the star; a separate fine arc adds a second motion line.
            DrawTrail(mesh, bounds, angle - length, length - .08f, 1f, width,
                Tint(new Color(1f, .86f, .48f, trailOpacity * brightness)));
            DrawTrail(mesh, bounds, angle - length * .9f, length * .6f, 1.12f, width * .42f,
                Tint(new Color(1f, .96f, .76f, trailOpacity * brightness * .6f)));
        }
        for (int seat = 0; seat < visibleStars; seat++)
        {
            float angle = Angle(drawOrder[seat]);
            float depth = Mathf.Sin(angle);
            Vector2 center = OrbitPoint(bounds, angle, 1f);
            float radius = Mathf.Min(bounds.width * .10f, bounds.height * .22f) * (1f + .18f * depth);
            // One star-point of spin per orbit closes seamlessly across the phase wrap.
            float tilt = phase * FullTurn / 5f + .18f * Mathf.Cos(angle);
            float alpha = Mathf.Lerp(.70f, 1f, (depth + 1f) * .5f);
            DrawStar(mesh, center, radius * 1.30f, tilt, Tint(new Color(1f, .79f, .32f, .12f * alpha)));
            DrawStar(mesh, center, radius * 1.12f, tilt, Tint(new Color(.44f, .30f, .18f, .9f * alpha)));
            DrawStar(mesh, center, radius, tilt, Tint(new Color(1f, .84f, .36f, alpha)), true);
        }
    }

    private float Angle(int index) => (phase + (float)index / AllyControlRelease.RequiredClicks) * FullTurn;

    private Color Tint(Color value) => value * color;

    private static Vector2 OrbitPoint(Rect bounds, float angle, float scale)
        => bounds.center + new Vector2(Mathf.Cos(angle) * bounds.width * .35f * scale,
            (-Mathf.Sin(angle) * .18f + Mathf.Cos(angle) * .06f) * bounds.height * scale);

    private static void DrawTrail(VertexHelper mesh, Rect bounds, float startAngle, float length,
        float orbitScale, float width, Color tint)
    {
        if (tint.a <= 0f) return;
        int start = mesh.currentVertCount;
        Color clear = tint; clear.a = 0f;
        for (int i = 0; i <= TrailSegments; i++)
        {
            float t = (float)i / TrailSegments;
            float angle = startAngle + t * length;
            Vector2 center = OrbitPoint(bounds, angle, orbitScale);
            Vector2 tangent = new Vector2(-Mathf.Sin(angle) * bounds.width * .35f,
                (-Mathf.Cos(angle) * .18f - Mathf.Sin(angle) * .06f) * bounds.height);
            Vector2 normal = new Vector2(-tangent.y, tangent.x).normalized;
            float halfWidth = width * Mathf.Lerp(.08f, 1f, t) * .5f;
            float feather = width * .55f;
            Color core = tint; core.a *= t * t;
            mesh.AddVert(center - normal * (halfWidth + feather), clear, Vector2.zero);
            mesh.AddVert(center - normal * halfWidth, core, Vector2.zero);
            mesh.AddVert(center + normal * halfWidth, core, Vector2.zero);
            mesh.AddVert(center + normal * (halfWidth + feather), clear, Vector2.zero);
            if (i == 0) continue;
            int previous = start + (i - 1) * 4;
            for (int strip = 0; strip < 3; strip++)
            {
                mesh.AddTriangle(previous + strip, previous + strip + 4, previous + strip + 1);
                mesh.AddTriangle(previous + strip + 1, previous + strip + 4, previous + strip + 5);
            }
        }
    }

    private void DrawStar(VertexHelper mesh, Vector2 center, float radius, float tilt, Color tint, bool shaded = false)
    {
        int start = mesh.currentVertCount;
        Color light = shaded ? Tint(new Color(1f, .98f, .81f, 1f)) : tint;
        light.a = tint.a;
        mesh.AddVert(center, light, Vector2.zero);
        for (int i = 0; i < 10; i++)
        {
            float a = tilt + i * Mathf.PI / 5f;
            float r = i % 2 == 0 ? radius : radius * .47f;
            Vector2 point = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            Color edge = shaded ? Color.Lerp(tint, light, Mathf.Clamp01(point.y) * .75f) : tint;
            mesh.AddVert(center + point * r, edge, Vector2.zero);
        }
        for (int i = 0; i < 10; i++)
            mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 10);
    }
}
