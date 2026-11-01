using UnityEngine;

/// <summary>Shared art crossfade. Enemy video freezes on its current frame, then resumes the target form after commit.</summary>
[DisallowMultipleComponent]
public sealed class UICardVariantSwitcher : MonoBehaviour
{
    public bool Pending { get; private set; }
    public int CurrentVariant { get; private set; }
    public float LastSwitchSeconds { get; private set; }
    public string Error { get; private set; }
    public bool AutomaticClock = true;
    public float Progress => fadeMaterial == null ? 1 : Mathf.Clamp01(fadeAge / fadeDuration);
    private UICardFactory factory;
    private UICardView card;
    private Material fadeMaterial, previousMaterial;
    private UnityEngine.UI.RawImage surface;
    private Texture fadeTarget, fadeSource;
    private int targetVariant;
    private float fadeAge, fadeDuration;
    private bool smoothFade;

    public void Bind(UICardFactory service, UICardView view, int currentVariant)
    {
        Cancel(); factory = service; card = view;
        CurrentVariant = currentVariant; Error = null;
    }
    public bool SetImmediate(int variant)
    {
        if (!isActiveAndEnabled || factory == null || !factory.HasVariant(card, variant))
        { Error = "差分未就绪：请先用卡牌服务预加载目标差分。"; return false; }
        Cancel(); Error = null;
        LastSwitchSeconds = 0;
        if (!factory.TrySetVariant(card, variant)) return false;
        CurrentVariant = variant; return true;
    }
    /// <summary>Both textures are mixed in a single masked draw, so the card never fades through the background.</summary>
    public bool FadeTo(int variant, UICardVariantStyle style)
    {
        if (!isActiveAndEnabled || factory == null || !factory.HasVariant(card, variant) || style == null ||
            style.Duration <= 0 || float.IsNaN(style.Duration) || float.IsInfinity(style.Duration))
        { Error = "渐变需要已预加载的差分与有效参数。"; return false; }
        if (Pending) { Error = "正在切换，请等待完成或先取消。"; return false; }
        if (!card.MediaResolved || (card.Video != null && card.Video.enabled && !card.Video.Ready && card.Video.Error == null))
        { Error = "卡面媒体仍在准备，请等待首帧后切换差分。"; return false; }
        var shader = Resources.Load<Shader>("UI/CardVariantCrossfade");
        if (shader == null) { Error = "缺少 UI/CardVariantCrossfade Shader。"; return false; }
        Cancel(); Error = null;
        card.SetAnimationFrozen(this, true);
        surface = card.Appearance.Surface; previousMaterial = surface.material;
        fadeSource = surface.texture;
        fadeTarget = factory.GetVariantTexture(card, variant);
        targetVariant = variant; fadeAge = 0; fadeDuration = style.Duration; smoothFade = style.Smooth;
        fadeMaterial = new Material(shader) { name = "Card variant crossfade", hideFlags = HideFlags.HideAndDontSave };
        surface.material = fadeMaterial;
        Pending = true; LastSwitchSeconds = fadeDuration; ApplyFade(0);
        return true;
    }
    public void Advance(float delta)
    {
        if (!isActiveAndEnabled || fadeMaterial == null || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        if (factory == null || !factory.HasVariant(card, targetVariant) || surface == null ||
            surface.material != fadeMaterial || surface.texture != fadeSource)
        { Cancel(); Error = "渐变已取消：卡牌、素材或材质所有者发生变化。"; return; }
        fadeAge += Mathf.Max(0, delta);
        float t = Progress;
        ApplyFade(smoothFade ? t * t * (3 - 2 * t) : t);
        if (t >= 1)
        {
            if (factory.TrySetVariant(card, targetVariant)) CurrentVariant = targetVariant;
            else Error = "差分提交失败，保留原卡面。";
            EndFade(); Pending = false;
        }
    }
    private void ApplyFade(float blend)
    {
        Rect from = surface.uvRect;
        Rect to = UICardFrameView.CoverUV(surface.rectTransform.rect.size, new Vector2(fadeTarget.width, fadeTarget.height));
        void Apply(Material material)
        {
            material.SetTexture("_NextTex", fadeTarget);
            material.SetVector("_FromUV", new Vector4(from.x, from.y, from.width, from.height));
            material.SetVector("_ToUV", new Vector4(to.x, to.y, to.width, to.height));
            material.SetFloat("_Blend", blend);
        }
        Apply(fadeMaterial);
        // UGUI caches a stencil material for this card mask. Update that owned clone too.
        var rendered = surface.materialForRendering;
        if (rendered != null && rendered != fadeMaterial) Apply(rendered);
    }
    private void EndFade()
    {
        if (surface != null && surface.material == fadeMaterial) surface.material = previousMaterial;
        if (fadeMaterial != null) Destroy(fadeMaterial);
        fadeMaterial = previousMaterial = null; surface = null; fadeTarget = fadeSource = null;
        if (card != null) card.SetAnimationFrozen(this, false);
    }
    public void Cancel()
    {
        Pending = false;
        EndFade();
    }
    private void Update()
    {
        if (Pending && (factory == null || !factory.isActiveAndEnabled || card == null || card.Released ||
            !card.isActiveAndEnabled)) Cancel();
    }
    private void LateUpdate() { if (AutomaticClock) Advance(Time.unscaledDeltaTime); }
    private void OnDisable() => Cancel();
}
