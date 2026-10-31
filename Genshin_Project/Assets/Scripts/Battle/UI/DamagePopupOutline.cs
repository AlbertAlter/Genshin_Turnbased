using TMPro;
using UnityEngine;

/// <summary>PSD outer stroke on live text; owned SDF material follows vertex-alpha fading.</summary>
[DisallowMultipleComponent]
public sealed class DamagePopupOutline : MonoBehaviour
{
    private TMP_Text label;
    private Material original, owned;

    public static void Apply(TMP_Text text, BattlefieldDamagePopupStyle style)
    {
        var effect = text.GetComponent<DamagePopupOutline>();
        if (style == null || style.OutlineWidthPixels <= 0 || style.OutlineColor.a <= 0)
        {
            // Damage/status labels share a pool; a reused status label must not inherit a damage outline.
            if (effect != null) effect.Release();
            return;
        }
        if (effect == null) effect = text.gameObject.AddComponent<DamagePopupOutline>();
        effect.Configure(text, style);
    }

    private void Configure(TMP_Text text, BattlefieldDamagePopupStyle style)
    {
        Release(); label = text;
        original = text.fontSharedMaterial;
        if (text.font == null || original == null) return;
        owned = new Material(original) { name = "Damage PSD outer stroke", hideFlags = HideFlags.HideAndDontSave };
        float gradient = Mathf.Max(.01f, owned.GetFloat(ShaderUtilities.ID_GradientScale));
        float width = Mathf.Clamp01(style.OutlineWidthPixels * text.font.faceInfo.pointSize /
            Mathf.Max(.01f, 2 * gradient * text.fontSize));
        // Equal dilation and outline move only the outside contour; the face retains its original size.
        owned.EnableKeyword("RATIOS_OFF"); owned.EnableKeyword("OUTLINE_ON");
        owned.SetFloat(ShaderUtilities.ID_ScaleRatio_A, 1);
        owned.SetFloat(ShaderUtilities.ID_FaceDilate, width);
        owned.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        owned.SetColor(ShaderUtilities.ID_OutlineColor, style.OutlineColor);
        text.fontSharedMaterial = owned;
        text.UpdateMeshPadding();
    }

    private void Release()
    {
        if (owned == null) return;
        if (label != null && label.fontSharedMaterial == owned)
        { label.fontSharedMaterial = original; label.UpdateMeshPadding(); }
        if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
        owned = null; original = null;
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
