using UnityEngine;
using UnityEngine.UI;

/// <summary>Display-only membrane/mask/tint/sweep. One owned material per card; no combat state.</summary>
[RequireComponent(typeof(RawImage))]
public sealed class UICardShield : MonoBehaviour
{
    private RawImage image;
    private Material ownedMaterial;
    public void Initialize(Texture membrane, Texture mask, Texture border, Color projectWhite)
    {
        image = GetComponent<RawImage>(); image.raycastTarget = false;
        var shader = Resources.Load<Shader>("UI/CardShield");
        if (shader == null) throw new System.InvalidOperationException("缺少 UI/CardShield Shader。");
        if (ownedMaterial != null) Destroy(ownedMaterial);
        ownedMaterial = new Material(shader) { name = "CardShield (owned)" };
        image.texture = membrane; image.material = ownedMaterial;
        ownedMaterial.SetTexture("_MaskTex", mask); ownedMaterial.SetTexture("_BorderTex", border);
        ownedMaterial.SetColor("_ProjectWhite", projectWhite);
    }
    public void Apply(UIShieldStyle style, Color tint, float membraneOpacity, float tintOpacity,
        Vector2 membraneOffset, Vector2 maskOffset, float sweepProgress, float sweepOpacity,
        float sweepWidth, float sweepAngle, float alpha)
    {
        var membrane = style.Rect("Membrane"); membrane.x += membraneOffset.x; membrane.y += membraneOffset.y;
        var mask = style.Rect("Mask"); mask.x += maskOffset.x; mask.y += maskOffset.y;
        Vector2 size = new Vector2(membrane.z + 2 * Mathf.Abs(membrane.x), membrane.w + 2 * Mathf.Abs(membrane.y));
        Vector2 half = new Vector2(size.x / style.Number("ReferenceWidth"), size.y / style.Number("ReferenceHeight")) * .5f;
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.one * .5f - half; rect.anchorMax = Vector2.one * .5f + half;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        ownedMaterial.SetVector("_ReferenceSize", new Vector4(size.x, size.y, 0, 0));
        ownedMaterial.SetVector("_MembraneRect", membrane); ownedMaterial.SetVector("_MaskRect", mask);
        ownedMaterial.SetVector("_TintRect", style.Rect("Tint"));
        ownedMaterial.SetColor("_TintColor", tint);
        ownedMaterial.SetFloat("_MembraneOpacity", membraneOpacity * style.Number("MembraneFillOpacity"));
        ownedMaterial.SetFloat("_TintOpacity", tintOpacity * style.Number("TintFillOpacity"));
        ownedMaterial.SetFloat("_SweepProgress", sweepProgress);
        ownedMaterial.SetFloat("_SweepOpacity", sweepOpacity);
        ownedMaterial.SetFloat("_SweepWidth", sweepWidth); ownedMaterial.SetFloat("_SweepAngle", sweepAngle);
        ownedMaterial.SetFloat("_SweepSoftness", style.Number("SweepSoftness"));
        ownedMaterial.SetColor("_SweepColor", Color.Lerp(Color.white, tint, style.Number("SweepElementTint")));
        image.color = new Color(1, 1, 1, alpha);
    }
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
