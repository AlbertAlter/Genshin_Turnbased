using UnityEngine;
using UnityEngine.UI;

/// <summary>Display-only membrane/mask/tint/sweep. One owned material per card; no combat state.</summary>
[RequireComponent(typeof(RawImage))]
public sealed class UICardShield : MonoBehaviour
{
    private RawImage image;
    private Material ownedMaterial;
    public void Initialize(Texture membrane, Texture mask, Texture clip)
    {
        image = GetComponent<RawImage>(); image.raycastTarget = false;
        var shader = Resources.Load<Shader>("UI/CardShield");
        if (shader == null) throw new System.InvalidOperationException("缺少 UI/CardShield Shader。");
        if (ownedMaterial != null) Destroy(ownedMaterial);
        ownedMaterial = new Material(shader) { name = "CardShield (owned)" };
        image.texture = membrane; image.material = ownedMaterial;
        ownedMaterial.SetTexture("_MaskTex", mask); ownedMaterial.SetTexture("_CardMask", clip);
    }
    public void Apply(UIShieldStyle style, Color tint, float membraneOpacity, float tintOpacity,
        Vector2 membraneOffset, Vector2 maskOffset, float sweepProgress, float sweepOpacity,
        float sweepWidth, float sweepAngle, float alpha)
    {
        var membrane = style.Rect("Membrane"); membrane.x += membraneOffset.x; membrane.y += membraneOffset.y;
        var mask = style.Rect("Mask"); mask.x += maskOffset.x; mask.y += maskOffset.y;
        ownedMaterial.SetVector("_ReferenceSize", new Vector4(style.Number("ReferenceWidth"), style.Number("ReferenceHeight"), 0, 0));
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
