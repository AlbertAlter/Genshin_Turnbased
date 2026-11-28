using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared card-face glyph transitions. Lisa uses four atlas digits; a talisman uses one whole glyph. Never edits statuses.</summary>
[DisallowMultipleComponent]
public sealed class UICardConductive : MonoBehaviour, IUICardFaceEffect
{
    public CardFaceConductiveStyle style = new CardFaceConductiveStyle();
    public bool automaticClock = true;
    public bool Paused { get; set; }
    public bool Ready => isActiveAndEnabled && assets != null && assets.Ready && root != null && area != null;
    public bool Visible => requested || opacity > 0;
    public int DisplayedCount => count;
    private UICardConductiveAssets assets;
    private RectTransform area, root;
    private RawImage image;
    private Material material;
    private bool requested;
    private int count;
    private float opacity, pulseTime;

    public bool Bind(RectTransform cardArea, RectTransform mount, UICardConductiveAssets resources)
    {
        Clear();
        if (!isActiveAndEnabled || cardArea == null || mount == null || resources == null || !resources.Ready) return false;
        area = cardArea; assets = resources;
        root = new GameObject("CardFaceGlyph", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        root.SetParent(mount, false); root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        image = root.GetComponent<RawImage>(); image.texture = assets.Atlas.texture; image.raycastTarget = false;
        material = new Material(assets.Shader) { name = "ConductiveGlyph", hideFlags = HideFlags.HideAndDontSave };
        image.material = material;
        assets.Invalidated += Clear; assets.ParametersChanged += ApplyConfiguration;
        ApplyConfiguration(); SetCount(1); Hide(true); return true;
    }
    private void ApplyConfiguration() => ApplyStyle(assets.Parameters.ApplyTo(style));
    public void ApplyStyle(CardFaceConductiveStyle next)
    {
        style = next.Copy();
        if (material != null)
        {
            material.SetColor("_CoreColor", assets.CoreColor);
            material.SetFloat("_UseAlpha", assets.IsTalisman ? 1 : 0);
            material.SetFloat("_CoreOpacity", style.coreOpacity); material.SetFloat("_GlowOpacity", style.glowOpacity);
        }
        Sample();
    }
    // Atlas origin is bottom-left in Unity. Original image has 1/2 on the top row, 3/4 below.
    public static Rect AtlasUV(int digit)
    {
        if (digit < 1 || digit > 4) throw new ArgumentOutOfRangeException(nameof(digit));
        const float inset = .5f / 256;
        return new Rect((digit % 2 == 0 ? .5f : 0) + inset, (digit < 3 ? .5f : 0) + inset, .5f - inset * 2, .5f - inset * 2);
    }
    public void SetCount(int value)
    {
        value = Mathf.Clamp(value, 0, assets != null && assets.IsTalisman ? 1 : 4);
        if (value == count) return;
        pulseTime = count > 0 && value > 0 ? 0 : style.changeSeconds;
        count = value;
        if (image != null && count > 0) image.uvRect = assets.IsTalisman ? new Rect(0, 0, 1, 1) : AtlasUV(count);
        if (requested && count > 0 && style.enterSeconds <= 0) opacity = 1;
        if (count == 0 && style.exitSeconds <= 0) opacity = 0;
        Sample();
    }
    public bool Show() { if (!Ready) return false; requested = true; if (style.enterSeconds <= 0 && count > 0) opacity = 1; Sample(); return true; }
    public void Hide(bool immediate = false)
    { requested = false; if (immediate || style.exitSeconds <= 0) opacity = 0; Sample(); }
    public void Advance(float seconds)
    {
        if (!Ready || Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        float target = requested && count > 0 ? 1 : 0;
        float duration = target > opacity ? style.enterSeconds : style.exitSeconds;
        opacity = duration <= 0 ? target : Mathf.MoveTowards(opacity, target, seconds / duration);
        pulseTime += seconds; Sample();
    }
    private void Update() { if (automaticClock) Advance(BattlePause.DeltaTime); }
    private void Sample()
    {
        if (root == null || area == null) return;
        float scale = area.rect.width / Mathf.Max(.01f, style.referenceCardWidth);
        root.sizeDelta = style.referenceSize * (scale * style.sizeMultiplier);
        root.anchoredPosition = style.centerOffset * scale;
        float pop = style.changeSeconds > 0 && pulseTime < style.changeSeconds
            ? Mathf.Sin(Mathf.PI * pulseTime / style.changeSeconds) * style.changeScaleAmount : 0;
        root.localScale = Vector3.one * (Mathf.Lerp(style.startScale, 1, Mathf.SmoothStep(0, 1, opacity)) + pop);
        Color color = assets != null ? assets.Tint : Color.white; color.a = opacity; image.color = color;
        root.gameObject.SetActive(opacity > 0 && count > 0);
    }
    public void Clear()
    {
        if (assets != null) { assets.Invalidated -= Clear; assets.ParametersChanged -= ApplyConfiguration; }
        if (image != null) { image.texture = null; image.material = null; }
        if (root != null) { root.gameObject.SetActive(false); Dispose(root.gameObject); }
        Dispose(material); material = null; root = area = null; image = null; assets = null;
        requested = false; opacity = pulseTime = 0; count = 0;
    }
    private static void Dispose(UnityEngine.Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    private void OnDisable() => Clear();
}
