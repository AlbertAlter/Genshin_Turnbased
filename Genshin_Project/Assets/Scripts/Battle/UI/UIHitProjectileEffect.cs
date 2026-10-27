using UnityEngine;

/// <summary>Cosmetic flight between sampled anchors; arrival never executes damage or creates a second hit.</summary>
public sealed class UIHitProjectileEffect : MonoBehaviour
{
    private UIHitTextureGraphic image, glow;
    private Vector2 start, end;
    private Color tint;
    private float duration, age;
    public bool Completed => age >= duration;
    public float HeadingDegrees => Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
    public static UIHitProjectileEffect Create(RectTransform layer, RectTransform source, RectTransform target,
        Vector2 hitPoint, HitEffectTextureStyle.Profile style, HitTextureLibrary library, Color color)
    {
        if (source == null || target == null || source == target || style.Number("FlightSeconds") <= 0) return null;
        var sprite = library.Get(style.Text("ProjectileTexture")); if (sprite == null) return null;
        var go = new GameObject("CosmeticProjectile", typeof(RectTransform)); go.transform.SetParent(layer, false);
        var effect = go.AddComponent<UIHitProjectileEffect>();
        Vector2 sourceCenter = layer.InverseTransformPoint(source.TransformPoint(source.rect.center));
        Vector2 targetCenter = layer.InverseTransformPoint(target.TransformPoint(target.rect.center));
        float facing = targetCenter.x >= sourceCenter.x ? 1 : -1;
        Vector2 sourceOffset = source.rect.center + new Vector2(source.rect.width * style.Number("SourceForwardRatio") * facing,
            source.rect.height * style.Number("SourceHeightRatio"));
        effect.start = layer.InverseTransformPoint(source.TransformPoint(sourceOffset));
        effect.end = layer.InverseTransformPoint(target.TransformPoint(target.rect.center + Vector2.Scale(hitPoint - Vector2.one * .5f, target.rect.size)));
        effect.duration = style.Number("FlightSeconds"); effect.tint = color;
        float width = source.rect.width * style.Number("ProjectileWidthRatio");
        Vector2 size = new Vector2(width, width * sprite.rect.height / sprite.rect.width);
        effect.glow = Make("ProjectileGlow", go.transform, sprite, library.LightMaterial, size * 1.3f);
        effect.image = Make("Projectile", go.transform, sprite, library.LightMaterial, size);
        effect.SetElapsed(0); return effect;
    }
    private static UIHitTextureGraphic Make(string name, Transform root, Sprite sprite, Material material, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIHitTextureGraphic)); go.transform.SetParent(root, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size;
        var image = go.GetComponent<UIHitTextureGraphic>(); image.Initialize(sprite, material); return image;
    }
    public void SetElapsed(float seconds)
    {
        age = Mathf.Max(0, seconds);
        var rect = (RectTransform)transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = HitTextureMath.Flight(start, end, age, duration);
        Vector2 direction = end - start; rect.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        Color core = tint; core.a *= Completed ? 0 : Mathf.Clamp01(age / .025f);
        image.color = core; image.Appearance(1, 0, .04f, .55f);
        core.a *= .28f; glow.color = core; glow.Appearance(1, 0, .04f, .2f, 28);
    }
}
