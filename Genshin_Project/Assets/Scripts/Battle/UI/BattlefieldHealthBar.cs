using TMPro;
using UnityEngine;

/// <summary>Presentation only. Entity identity is side + slot, never the enemy ID.</summary>
public sealed class BattlefieldHealthBar : MonoBehaviour
{
    public BattleSide side;
    public int slot;
    public RoundedHealthFill fill;
    public TMP_Text currentHPText;
    public MonoBehaviour dataSource;
    private IBattleUiHealthSource boundSource;
    [Min(0)] public float transitionSeconds = .2f;
    private object previous;
    private float from, target, elapsed;

    private void Start()
    {
        if (boundSource == null) boundSource = dataSource as IBattleUiHealthSource;
        if (dataSource != null && boundSource == null) Debug.LogError("Health bar Data Source must implement IBattleUiHealthSource.", this);
        ReloadAnimationParameters();
    }

    public void Bind(IBattleUiHealthSource source) { boundSource = source; previous = null; }

    public void ReloadAnimationParameters()
    {
        transitionSeconds=BattlefieldAnimationParameters.Load().Get("Health.TransitionDuration",.2f);
    }

    // Initial PSD construction only. Never called by the parameter update tool or at runtime.
    public void ApplyStyle(BattlefieldHealthStyle style)
    {
        transitionSeconds = style.Duration;
        if (fill == null) return;
        fill.color = side == BattleSide.Ally ? style.AllyColor : style.EnemyColor;
        var rect = fill.rectTransform;
        rect.offsetMin = new Vector2(style.Left, style.Bottom);
        rect.offsetMax = new Vector2(-style.Right, -style.Top);
    }

    private void LateUpdate()
    {
        if (boundSource != null) Present(boundSource.GetHealth(side, slot), Time.unscaledDeltaTime);
    }

    public void Present(BattleUiMeterState state, float deltaTime)
    {
        if (fill == null) return;
        if (!state.Visible)
        {
            fill.Fraction = 0;
            if (currentHPText != null) currentHPText.text = "";
            previous = null;
            return;
        }
        float next = state.Fraction;
        if (float.IsNaN(next) || float.IsInfinity(next)) next = 0;
        if (!ReferenceEquals(previous, state.Identity))
        {
            previous = state.Identity;
            from = target = next;
            fill.Fraction = next;
            elapsed = transitionSeconds;
        }
        else if (!Mathf.Approximately(next, target))
        {
            from = fill.Fraction;
            target = next;
            elapsed = 0;
        }
        elapsed += Mathf.Max(0, deltaTime);
        float t = transitionSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed/transitionSeconds);
        fill.Fraction = Mathf.Lerp(from, target, 1-Mathf.Pow(1-t, 3));
        if (currentHPText != null) currentHPText.text = Mathf.CeilToInt(Mathf.Max(0, state.Current)).ToString();
    }

    private void OnDisable() { previous = null; }
}
