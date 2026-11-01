using TMPro;
using UnityEngine;

/// <summary>Per-label SDF material; never modifies the shared project font.</summary>
[ExecuteAlways]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class PsdApTextOutline : MonoBehaviour
{
    private TMP_Text label;
    private Material original, owned;
    [SerializeField] private Color outlineColor = new Color32(113,103,88,255);
    [SerializeField, Min(0)] private float outlinePixels = 3f;

    public void Configure(Color color, float pixels)
    {
        if (owned != null) OnDisable();
        outlineColor = color;
        outlinePixels = pixels;
        if (isActiveAndEnabled) OnEnable();
    }

    private void OnEnable()
    {
        label = GetComponent<TMP_Text>();
        if (label.font == null) return;
        original = label.fontSharedMaterial;
        if (original == null) original = label.font.material;
        if (original == null) return;
        owned = new Material(original) { name = "PSD text outer stroke", hideFlags = HideFlags.HideAndDontSave };
        float gradient = owned.GetFloat(ShaderUtilities.ID_GradientScale);
        float width = Mathf.Clamp01(outlinePixels * label.font.faceInfo.pointSize /
                                     Mathf.Max(.01f, 2f * gradient * label.fontSize));
        // With ratios off, equal dilation and outline preserve the original face
        // edge and extend only outwards. Width is in UI design pixels, not a guessed shader value.
        owned.EnableKeyword("RATIOS_OFF");
        owned.EnableKeyword("OUTLINE_ON");
        owned.SetFloat(ShaderUtilities.ID_ScaleRatio_A, 1f);
        owned.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        owned.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        owned.SetFloat(ShaderUtilities.ID_FaceDilate, width);
        label.fontSharedMaterial = owned;
        label.UpdateMeshPadding();
    }

    private void OnDisable()
    {
        if (owned == null) return;
        if (label != null && label.fontSharedMaterial == owned) label.fontSharedMaterial = original;
        if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
        owned = null;
    }
}
