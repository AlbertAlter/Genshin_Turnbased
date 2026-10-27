using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable procedural impact. Caller supplies the already resolved element and hit index, and owns its clock.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIHitEffectGraphic : MaskableGraphic
{
    private UIHitEffectStyle.Profile profile;
    private UIHitEffectStyle.Particles particles;
    private Color tint;
    private Vector2 cardSize;
    private uint seed;
    private float elapsed, scale, angle, opacity;
    public float Duration => profile?.Duration ?? 0;
    public bool Completed => elapsed >= Duration;

    public void Play(UIHitEffectStyle.Profile settings, UIHitEffectStyle.Particles elementParticles, Color elementColor,
        Vector2 targetCardSize, uint visualSeed, int hitIndex, int hitCount)
    {
        profile = settings; particles = elementParticles; tint = elementColor; cardSize = targetCardSize; seed = visualSeed;
        elapsed = 0; raycastTarget = false;
        scale = HitEffectMath.Scale(profile, seed, hitIndex, hitCount);
        angle = N("AngleBaseDegrees") + (hitIndex % 2) * N("AlternateAngleDegrees") + hitIndex * N("HitAngleProgressDegrees") +
            HitEffectMath.Range(seed, 4, -N("AngleJitterDegrees"), N("AngleJitterDegrees"));
        opacity = HitEffectMath.Range(seed, 5, N("OpacityMin"), N("OpacityMax"));
        rectTransform.sizeDelta = targetCardSize;
        Vector2 center = HitEffectMath.Center(profile, seed);
        rectTransform.anchoredPosition = Vector2.Scale(center - Vector2.one * .5f, targetCardSize);
        SetVerticesDirty();
    }
    public void SetElapsed(float age) { elapsed = Mathf.Max(0, age); SetVerticesDirty(); }
    private float N(string key) => profile.Number(key);
    private static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
    private static Vector2 Rotate(Vector2 point, float degrees)
    { Vector2 d = Direction(degrees); return new Vector2(point.x * d.x - point.y * d.y, point.x * d.y + point.y * d.x); }
    private float Envelope(float lifetime) => lifetime <= 0 || elapsed >= lifetime ? 0 : Mathf.Clamp01(1 - elapsed / lifetime);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (profile == null || !profile.Enabled || Completed) return;
        float fadeIn = N("FadeInSeconds"), hold = N("HoldSeconds"), fadeOut = N("FadeOutSeconds");
        float alpha = (fadeIn > 0 ? Mathf.Clamp01(elapsed / fadeIn) : 1) *
            (elapsed <= fadeIn + hold ? 1 : fadeOut > 0 ? Mathf.Clamp01(1 - (elapsed - fadeIn - hold) / fadeOut) : 0);
        float length = N("LengthCardWidthRatio") * cardSize.x * scale;
        float width = N("WidthCardWidthRatio") * cardSize.x * scale;
        string pattern = profile.Text("Pattern");
        if (alpha > 0 && length > 0 && width > 0)
        {
            switch (pattern)
            {
                case "CrossSlash": Slash(mesh, length, width, angle, .17f, alpha); Slash(mesh, length * .85f, width, angle + 85, .17f, alpha); break;
                case "HeavySlash": Slash(mesh, length, width, angle, .28f, alpha); break;
                case "SweepingSlash": case "SweepingThrust": Slash(mesh, length, width, angle + elapsed * 70, .4f, alpha); break;
                case "Thrust": Slash(mesh, length, width, angle, 0, alpha); break;
                case "ArrowImpact":
                    Slash(mesh, length, width, angle, 0, alpha);
                    Slash(mesh, length * .65f, width * .6f, angle + 90, 0, alpha); break;
                case "ChargedImpact":
                    Ring(mesh, Vector2.zero, length * (.5f + elapsed), width, angle, 360, alpha);
                    Slash(mesh, length, width, angle, 0, alpha); break;
                case "ElementImpact": case "ElementBurst":
                    Ring(mesh, Vector2.zero, length * (.4f + elapsed * 2), width, angle + elapsed * 130,
                        pattern == "ElementBurst" ? 320 : 240, alpha);
                    Ring(mesh, Vector2.zero, length * .22f, width * .5f, -angle, 240, alpha * .7f); break;
                default: Slash(mesh, length, width, angle, .18f, alpha); break;
            }
        }
        float flash = Envelope(N("FlashLifetimeSeconds"));
        if (flash > 0)
        {
            float radius = N("FlashCardWidthRatio") * cardSize.x * scale * (.25f + flash * .3f);
            Diamond(mesh, Vector2.zero, radius, radius * .45f, angle, Tint(flash, true));
        }
        if (N("ElementParticlesEnabled") == 0 || particles == null || Envelope(N("ParticleLifetimeSeconds")) <= 0) return;
        int count = Mathf.Min(512, Mathf.RoundToInt(N("ParticleCount") * particles.CountMultiplier));
        // One uGUI graphic must stay below the 16-bit mesh index limit even when a table is tuned upward.
        int verticesPerParticle = particles.Shape == "Curl" ? 384 : particles.Shape == "Arc" ? 48 : particles.Shape == "Spark" ? 16 : 4;
        count = Mathf.Min(count, (64000 - mesh.currentVertCount) / verticesPerParticle);
        for (int i = 0; i < count; i++) Particle(mesh, i);
    }

    private Color Tint(float alpha, bool core = false)
    {
        Color value = core ? Color.Lerp(tint, Color.white, .65f) : tint;
        value.a = alpha * opacity * tint.a; return value;
    }
    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int n = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
    }
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a, normal = new Vector2(-d.y, d.x).normalized * width * .5f;
        Quad(mesh, a - normal, a + normal, b + normal, b - normal, color);
    }
    private void GlowLine(VertexHelper mesh, Vector2 a, Vector2 b, float width, float alpha)
    {
        Line(mesh, a, b, width * 3.5f, Tint(alpha * .08f));
        Line(mesh, a, b, width * 1.8f, Tint(alpha * .22f));
        Line(mesh, a, b, width, Tint(alpha));
        Line(mesh, a, b, width * .28f, Tint(alpha, true));
    }
    private void Slash(VertexHelper mesh, float length, float width, float degrees, float curve, float alpha)
    {
        const int segments = 18;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (float)i / segments, t1 = (float)(i + 1) / segments;
            Vector2 Point(float t) => Rotate(new Vector2((t - .5f) * length, Mathf.Sin(t * Mathf.PI) * length * curve), degrees);
            float taper = Mathf.Sin((t0 + t1) * .5f * Mathf.PI);
            GlowLine(mesh, Point(t0), Point(t1), width * Mathf.Max(.02f, taper), alpha);
        }
    }
    private void Ring(VertexHelper mesh, Vector2 center, float radius, float width, float degrees, float arc, float alpha)
    {
        const int segments = 24;
        for (int i = 0; i < segments; i++)
            GlowLine(mesh, center + Direction(degrees + arc * i / segments) * radius,
                center + Direction(degrees + arc * (i + 1) / segments) * radius, width, alpha);
    }
    private static void Diamond(VertexHelper mesh, Vector2 center, float x, float y, float angle, Color color)
        => Quad(mesh, center + Rotate(new Vector2(-x, 0), angle), center + Rotate(new Vector2(0, y), angle),
            center + Rotate(new Vector2(x, 0), angle), center + Rotate(new Vector2(0, -y), angle), color);

    private void Particle(VertexHelper mesh, int index)
    {
        uint channel = (uint)(20 + index * 5);
        float lifetime = N("ParticleLifetimeSeconds"), t = elapsed / lifetime;
        float degrees = angle + HitEffectMath.Range(seed, channel, -N("ParticleSpreadDegrees"), N("ParticleSpreadDegrees"));
        float speed = cardSize.y * N("ParticleSpeedCardHeightPerSecond") * HitEffectMath.Range(seed, channel + 1, .6f, 1.2f) * scale;
        Vector2 point = Direction(degrees) * speed * elapsed;
        float rotation = HitEffectMath.Range(seed, channel + 2, 0, 360);
        switch (particles.Motion)
        {
            case "BurstRise": point.y += speed * elapsed * t; break;
            case "BurstFall": point.y -= speed * elapsed * t; break;
            case "SwirlOut": point = Rotate(point, t * 160); break;
            case "DriftOut": point.y += Mathf.Sin(t * 5 + rotation) * speed * .12f; rotation += t * 80; break;
            case "BurstSpin": rotation += t * 240; break;
            case "BranchFlicker": point += Direction(rotation) * speed * .08f * Mathf.Sin(elapsed * 80); break;
        }
        float size = cardSize.x * N("ParticleSizeCardWidthRatio") * particles.SizeMultiplier * scale * (1 - t * .6f);
        float alpha = (1 - t) * (1 - t);
        switch (particles.Shape)
        {
            case "Arc":
                Vector2 end = point + Direction(degrees) * size * 4, bend = (point + end) * .5f + Direction(degrees + 90) * size;
                GlowLine(mesh, point, bend, size * .3f, alpha); GlowLine(mesh, bend, end, size * .3f, alpha);
                GlowLine(mesh, bend, bend + Direction(degrees - 65) * size * 1.5f, size * .2f, alpha * .6f); break;
            case "Curl": Ring(mesh, point, size * 1.5f, size * .25f, rotation + t * 180, 160, alpha); break;
            case "Spark": GlowLine(mesh, point, point - Direction(degrees) * size * 3, size * .3f, alpha); break;
            case "Shard":
                int n = mesh.currentVertCount;
                mesh.AddVert(point + Rotate(new Vector2(0, size * 1.5f), rotation), Tint(alpha, true), Vector2.zero);
                mesh.AddVert(point + Rotate(new Vector2(-size, -size), rotation), Tint(alpha), Vector2.zero);
                mesh.AddVert(point + Rotate(new Vector2(size * .6f, -size * .4f), rotation), Tint(alpha), Vector2.zero);
                mesh.AddTriangle(n, n + 1, n + 2); break;
            case "Droplet": Diamond(mesh, point, size * .55f, size * 1.4f, degrees - 90, Tint(alpha)); break;
            case "Leaf": Diamond(mesh, point, size * 1.5f, size * .4f, rotation, Tint(alpha)); break;
            case "Ember": Diamond(mesh, point, size * .6f, size, rotation, Tint(alpha, true)); break;
            default: Diamond(mesh, point, size * .65f, size * 1.4f, rotation, Tint(alpha)); break;
        }
    }
}
