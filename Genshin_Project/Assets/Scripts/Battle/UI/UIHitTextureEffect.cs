using System.Collections.Generic;
using UnityEngine;

/// <summary>One resolved hit: separately animated blade/splash, additive glow, glint and flying fragments.</summary>
public sealed class UIHitTextureEffect : MonoBehaviour
{
    private sealed class Stroke { public UIHitTextureGraphic Body, Glow; public float Delay, Angle; }
    private sealed class Fragment { public UIHitTextureGraphic Image; public Vector2 Velocity; public float Rotation, Size; }
    private readonly List<Stroke> strokes = new List<Stroke>();
    private readonly List<Fragment> fragments = new List<Fragment>();
    private UIHitTextureGraphic flash;
    private UIHitEffectStyle.Profile impact;
    private UIHitEffectStyle.Particles particles;
    private HitEffectTextureStyle.Profile style;
    private Color tint;
    private float opacity, width, height, flashSize, age;
    private bool mirror;
    public float Duration => style.Duration(impact);
    public bool Completed => age >= Duration;

    public static UIHitTextureEffect Create(Transform parent, HitTextureLibrary library, UIHitEffectStyle.Profile impact,
        UIHitEffectStyle.Particles particles, HitEffectTextureStyle.Profile style, Color color, Vector2 cardSize, uint seed, int hit, int hitCount, float directionDegrees = 0,
        Vector2? groupCenter = null, Vector2? groupSpan = null)
    {
        var root = new GameObject("TexturedHit_" + hit, typeof(RectTransform)); root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = cardSize;
        rect.anchoredPosition = groupCenter ?? Vector2.Scale(HitEffectMath.Center(impact, seed) - Vector2.one * .5f, cardSize);
        var effect = root.AddComponent<UIHitTextureEffect>();
        effect.Initialize(library, impact, particles, style, color, cardSize, seed, hit, hitCount, directionDegrees, groupSpan); return effect;
    }
    private float N(string key) => style.Number(key);
    private void Initialize(HitTextureLibrary library, UIHitEffectStyle.Profile p, UIHitEffectStyle.Particles elementParticles,
        HitEffectTextureStyle.Profile settings, Color color, Vector2 size, uint seed, int hit, int count, float directionDegrees, Vector2? groupSpan)
    {
        impact = p; particles = elementParticles; style = settings; tint = color;
        float scale = HitEffectMath.Scale(p, seed, hit, count);
        opacity = HitEffectMath.Range(seed, 5, p.Number("OpacityMin"), p.Number("OpacityMax"));
        float angle = directionDegrees + p.Number("AngleBaseDegrees") + (hit % 2) * p.Number("AlternateAngleDegrees") + hit * p.Number("HitAngleProgressDegrees")
            + HitEffectMath.Range(seed, 4, -p.Number("AngleJitterDegrees"), p.Number("AngleJitterDegrees"));
        mirror = HitEffectMath.Unit(seed, 701) < N("MirrorChance");
        width = size.x * p.Number("LengthCardWidthRatio") * N("BodyWidthMultiplier") * scale;
        height = size.x * N("BodyHeightRatio") * scale * HitEffectMath.Range(seed, 702, N("StretchMin"), N("StretchMax"));
        if (groupSpan.HasValue)
        {
            // Only the slash body spans the group. Glint, fragment size and speed retain a single-card scale.
            angle = N("GroupAngleDegrees");
            float radians = angle * Mathf.Deg2Rad;
            Vector2 span = groupSpan.Value;
            float along = Mathf.Abs(Mathf.Cos(radians)) * span.x + Mathf.Abs(Mathf.Sin(radians)) * span.y;
            float across = Mathf.Abs(Mathf.Sin(radians)) * span.x + Mathf.Abs(Mathf.Cos(radians)) * span.y;
            width = Mathf.Max(width, along + 2 * size.x * N("GroupPaddingCardWidthRatio"));
            height = Mathf.Min(Mathf.Max(height, across * N("GroupHeightSpanRatio")), size.y * N("GroupMaxHeightCardHeightRatio"));
        }
        var body = library.Get(style.Bodies[HitTextureMath.Variant(seed, style.Bodies.Length)]);
        var glow = library.Get(style.Text("GlowTexture")) ?? body;
        Stroke MakeStroke(float degrees, float delay)
        {
            var stroke = new Stroke { Angle = degrees, Delay = delay };
            stroke.Glow = Image("BladeGlow", glow, library.LightMaterial, new Vector2(width, height) * N("GlowScale"));
            stroke.Body = Image("BladeOrImpact", body, library.BodyMaterial, new Vector2(width, height));
            stroke.Glow.rectTransform.localScale = stroke.Body.rectTransform.localScale = new Vector3(mirror ? -1 : 1, 1, 1);
            strokes.Add(stroke); return stroke;
        }
        MakeStroke(angle, 0);
        if (!groupSpan.HasValue && N("SecondSlashAngleDegrees") != 0) MakeStroke(angle + N("SecondSlashAngleDegrees"), N("SecondSlashDelaySeconds"));
        flashSize = size.x * p.Number("FlashCardWidthRatio") * N("FlashScale") * scale;
        var flashSprite = library.Get(style.Text("FlashTexture"));
        if (flashSprite != null) { flash = Image("ContactGlint", flashSprite, library.LightMaterial, Vector2.one * flashSize); flash.rectTransform.localEulerAngles = new Vector3(0, 0, angle); }
        var fragmentSprite = library.Get(style.Text("ParticleTexture"));
        int number = fragmentSprite == null || p.Number("ElementParticlesEnabled") == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(p.Number("ParticleCount") * particles.CountMultiplier), 0, 64);
        for (int i = 0; i < number; i++)
        {
            uint channel = (uint)(900 + i * 4);
            float degrees = angle + HitEffectMath.Range(seed, channel, -p.Number("ParticleSpreadDegrees"), p.Number("ParticleSpreadDegrees"));
            float speed = size.y * p.Number("ParticleSpeedCardHeightPerSecond") * N("ParticleSpeedMultiplier") * scale * HitEffectMath.Range(seed, channel + 1, .65f, 1.3f);
            float length = size.x * p.Number("ParticleSizeCardWidthRatio") * N("ParticleSizeMultiplier") * particles.SizeMultiplier * scale * HitEffectMath.Range(seed, channel + 2, .65f, 1.3f);
            var graphic = Image("LightFragment_" + i, fragmentSprite, library.LightMaterial, new Vector2(length, length * fragmentSprite.rect.height / fragmentSprite.rect.width));
            fragments.Add(new Fragment { Image = graphic, Velocity = Direction(degrees) * speed, Size = length, Rotation = HitEffectMath.Range(seed, channel + 3, -35, 35) });
        }
        grouped = groupSpan.HasValue;
        SetElapsed(0);
    }
    private bool grouped;
    private UIHitTextureGraphic Image(string name, Sprite sprite, Material material, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIHitTextureGraphic)); go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size;
        var graphic = go.GetComponent<UIHitTextureGraphic>(); graphic.Initialize(sprite, material); return graphic;
    }
    private static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
    private Color Color(float alpha) { var c = tint; c.a *= Mathf.Clamp01(alpha * opacity); return c; }
    public void SetElapsed(float seconds)
    {
        age = Mathf.Max(0, seconds);
        foreach (var stroke in strokes)
        {
            float local = age - stroke.Delay;
            float erase = HitTextureMath.Erase(local, N("EraseDelaySeconds"), N("EraseSeconds"));
            float reveal = style.Text("RevealMode") == "Burst" ? 1 : HitTextureMath.Reveal(local, N("RevealSeconds"));
            float alpha = local < 0 || local >= Duration - stroke.Delay ? 0 : style.Text("RevealMode") == "Burst" ? 1 - erase : 1 - erase * .35f;
            float angle = stroke.Angle + (!grouped && (impact.Text("Pattern") == "SweepingSlash" || impact.Text("Pattern") == "SweepingThrust") ? Mathf.Max(0, local) * 65 : 0);
            foreach (var graphic in new[] { stroke.Body, stroke.Glow }) graphic.rectTransform.localEulerAngles = new Vector3(0, 0, angle);
            stroke.Body.color = Color(alpha);
            stroke.Body.Appearance(reveal, erase, N("EdgeSoftness"), N("CoreWhiteMix"));
            stroke.Glow.color = Color(alpha * N("GlowOpacity"));
            stroke.Glow.Appearance(reveal, erase, N("EdgeSoftness"), .15f, 36);
            if (style.Text("RevealMode") == "Burst")
            {
                float expand = .6f + .55f * HitTextureMath.Reveal(Mathf.Max(0, local), N("EraseDelaySeconds") + N("EraseSeconds"));
                stroke.Body.rectTransform.localScale = stroke.Glow.rectTransform.localScale = new Vector3((mirror ? -1 : 1) * expand, expand, 1);
            }
        }
        if (flash != null)
        {
            float t = HitTextureMath.Reveal(age, impact.Number("FlashLifetimeSeconds"));
            flash.color = Color((1 - t) * (1 - t) * N("FlashOpacity"));
            flash.rectTransform.sizeDelta = Vector2.one * flashSize * (.7f + .6f * t);
            flash.Appearance(1, 0, .04f, .8f, 3);
        }
        foreach (var fragment in fragments)
        {
            float t = HitTextureMath.Reveal(age, impact.Number("ParticleLifetimeSeconds"));
            Vector2 point = fragment.Velocity * age * (1 - .4f * t);
            switch (particles.Motion)
            {
                case "BurstRise": point.y += fragment.Velocity.magnitude * age * t * .5f; break;
                case "BurstFall": point.y -= fragment.Velocity.magnitude * age * t * .5f; break;
                case "SwirlOut": point = Quaternion.Euler(0, 0, t * 100) * point; break;
                case "DriftOut": point.y += Mathf.Sin(t * 5 + fragment.Rotation) * fragment.Size; break;
                case "BranchFlicker": point.y += Mathf.Sin(age * 80 + fragment.Rotation) * fragment.Size * .2f; break;
            }
            fragment.Image.rectTransform.anchoredPosition = point;
            fragment.Image.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(fragment.Velocity.y, fragment.Velocity.x) * Mathf.Rad2Deg + fragment.Rotation * t);
            fragment.Image.rectTransform.localScale = Vector3.one * (1 - .65f * t);
            fragment.Image.color = Color((1 - t) * (1 - t) * N("ParticleOpacity"));
            fragment.Image.Appearance(1, 0, .04f, .5f);
        }
    }
}
