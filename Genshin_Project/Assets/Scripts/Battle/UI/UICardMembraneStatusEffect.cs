using UnityEngine;

/// <summary>State-driven adapter for the existing shared shield membrane. No shield gameplay or bar ownership.</summary>
public sealed class UICardMembraneStatusEffect : MonoBehaviour, IUICardFaceEffect
{
    public static readonly string[] Elements = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private BattleUiShields assets;
    private UICardShield membrane;
    private UIShieldStyle appliedStyle;
    private readonly ShieldSweepClock sweep = new ShieldSweepClock();
    private string element;
    private bool requested;
    private double elapsed, revealStart;
    private float alpha, reveal, sweepProgress, sweepOpacity;
    public bool Ready => isActiveAndEnabled && assets != null && assets.isActiveAndEnabled && assets.Ready && membrane != null;
    public bool Visible => requested || alpha > 0;

    public bool Bind(RectTransform mount, BattleUiShields sharedAssets, string shieldElement)
    {
        Clear();
        if (mount == null || sharedAssets == null || !sharedAssets.isActiveAndEnabled || !sharedAssets.Ready) return false;
        if (shieldElement != "None" && !sharedAssets.Palette.TryGetColor(shieldElement, out _)) return false;
        assets = sharedAssets; element = shieldElement;
        if (membrane == null)
        {
            var node = new GameObject("StatusShieldMembrane", typeof(RectTransform));
            node.transform.SetParent(mount, false);
            membrane = node.AddComponent<UICardShield>();
        }
        assets.InitializeMembrane(membrane); appliedStyle = assets.Settings;
        membrane.SetAlpha(0); membrane.gameObject.SetActive(false);
        return true;
    }
    public bool Show()
    {
        if (!Ready) return false;
        if (!requested && alpha <= 0)
        {
            revealStart = elapsed;
            reveal = UIShieldStyle.SampleReveal(0, assets.Settings.Number("RevealDuration"));
            sweep.Restart(elapsed + assets.Settings.Number("RevealDuration"));
        }
        requested = true; membrane.gameObject.SetActive(true);
        return true;
    }
    public void Hide(bool immediate = false)
    {
        requested = false;
        if (immediate) Clear();
    }
    public void Advance(float seconds)
    {
        if (!Ready) { Clear(); return; }
        if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        elapsed += seconds;
        var style = assets.Settings;
        if (appliedStyle != style)
        { assets.InitializeMembrane(membrane); appliedStyle = style; }
        alpha = UIShieldStyle.StepAlpha(alpha, requested, seconds,
            style.Number(requested ? "FadeInSeconds" : "FadeOutSeconds"));
        if (!requested && alpha <= 0) { membrane.gameObject.SetActive(false); return; }
        if (requested)
        {
            reveal = UIShieldStyle.SampleReveal(elapsed - revealStart, style.Number("RevealDuration"));
            sweepOpacity = sweep.Sample(elapsed, style.Number("SweepDuration"), style.Number("SweepInterval"),
                style.Number("SweepIntervalJitter"), style.Number("SweepFadeFraction"), true, out sweepProgress);
        }
        membrane.Apply(style, assets.Palette.GetColor(element), style.Number("MembraneOpacity"),
            style.Number(element == "None" ? "NeutralTintOpacity" : "TintOpacity"), Vector2.zero, Vector2.zero,
            sweepProgress, style.Number("SweepOpacity") * sweepOpacity, style.Number("SweepWidth"),
            style.Number("SweepAngle"), alpha, reveal);
    }
    public void Clear()
    {
        requested = false; alpha = reveal = sweepProgress = sweepOpacity = 0; elapsed = revealStart = 0;
        if (membrane != null)
        {
            var owned = membrane.gameObject; membrane = null;
            owned.SetActive(false);
            if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
        }
        assets = null; appliedStyle = null; element = null;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}
