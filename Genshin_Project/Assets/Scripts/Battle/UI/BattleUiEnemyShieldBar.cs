using UnityEngine;

/// <summary>Optional enemy shield meter; owns no battle rules, card layout, or art imports.</summary>
[AddComponentMenu("Battle UI/Enemy Shield Bar")]
public sealed class BattleUiEnemyShieldBar : MonoBehaviour
{
    public int slot = 1;
    public MonoBehaviour dataSource;
    // Host this component outside the visibility root so it can show the bar again.
    public GameObject visibilityRoot;
    public RoundedHealthFill fill;
    public PsdUiLayerStack layerStack;
    public TMPro.TMP_Text valueText;
    public bool useElementColor = true;
    [Min(0)] public float transitionSeconds = .2f;
    private IBattleUiEnemyShieldSource source;
    private ElementColorPalette palette;
    private object previous;
    private float shown, from, target, elapsed;
    private Color authoredColor;

    private void Start()
    {
        if (source == null) source = dataSource as IBattleUiEnemyShieldSource;
        if (visibilityRoot == gameObject || fill == null || layerStack == null)
        { Debug.LogError("Shield bar needs a separate visibility root, fill model and PSD layer stack.", this); enabled = false; return; }
        authoredColor = fill.color;
        if (useElementColor) palette = ElementColorPalette.Load();
        ReloadAnimationParameters();
    }

    public void Bind(IBattleUiEnemyShieldSource value) { source = value; previous = null; }
    public void ReloadAnimationParameters() => transitionSeconds = BattlefieldAnimationParameters.Load().Get("Health.TransitionDuration", .2f);
    private void LateUpdate()
    {
        if (source != null) Present(source.GetEnemyShield(slot), Time.unscaledDeltaTime);
    }

    public void Present(BattleUiMeterState state, float deltaTime)
    {
        bool visible = state.Visible && state.Current > 0 && state.Maximum > 0;
        if (visibilityRoot != null && visibilityRoot != gameObject) visibilityRoot.SetActive(visible);
        if (!visible) { previous = null; if (valueText != null) valueText.text = ""; return; }
        if (fill == null) return;
        float next = state.Fraction;
        if (float.IsNaN(next) || float.IsInfinity(next)) next = 0;
        if (!ReferenceEquals(previous, state.Identity))
        { previous = state.Identity; shown = from = target = next; elapsed = transitionSeconds; }
        else if (!Mathf.Approximately(target, next)) { from = shown; target = next; elapsed = 0; }
        elapsed += Mathf.Max(0, deltaTime);
        float t = transitionSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / transitionSeconds);
        shown = Mathf.Lerp(from, target, 1 - Mathf.Pow(1 - t, 3));
        // Every PSD layer, including the shadow, follows this fraction. No fixed empty track/border.
        fill.Fraction = shown;
        string element = string.IsNullOrEmpty(state.Element) || state.Element == "None" ? "Physical" : state.Element;
        if (useElementColor && palette != null && palette.TryGetColor(element, out var color))
        { color.a = authoredColor.a; layerStack.SetBaseColor(color); }
        if (valueText != null) valueText.text = Mathf.CeilToInt(Mathf.Max(0, state.Current)).ToString();
    }

    private void OnDisable()
    {
        if (visibilityRoot != null && visibilityRoot != gameObject) visibilityRoot.SetActive(false);
        previous = null;
    }
}
