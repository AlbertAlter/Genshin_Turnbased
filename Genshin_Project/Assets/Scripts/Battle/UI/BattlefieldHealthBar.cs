using TMPro;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation only. Entity identity is side + slot, never the enemy ID.</summary>
public sealed class BattlefieldHealthBar : MonoBehaviour
{
    public BattleSide side;
    public int slot;
    public RoundedHealthFill fill;
    public TMP_Text currentHPText;
    public MonoBehaviour dataSource;
    public EnemyHealthPipRow enemyPips = new EnemyHealthPipRow();
    private IBattleUiHealthSource boundSource;
    [Min(0)] public float transitionSeconds = .2f;
    private object previous;
    private int previousPhase;
    private float from, target, elapsed;

    private void Start()
    {
        if (boundSource == null) boundSource = dataSource as IBattleUiHealthSource;
        if (dataSource != null && boundSource == null) Debug.LogError("Health bar Data Source must implement IBattleUiHealthSource.", this);
        ReloadAnimationParameters();
    }

    public void Bind(IBattleUiHealthSource source)
    {
        boundSource = source;
        previous = null;
        enemyPips.Show(0, 0);
    }

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
        var phases = state.HealthPhases;
        enemyPips.Show(state.Visible && phases.Count > 1 ? phases.Count : 0, phases.Remaining(state.Current));
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
        if (!ReferenceEquals(previous, state.Identity) || previousPhase != phases.Current)
        {
            previous = state.Identity;
            previousPhase = phases.Current;
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

    private void OnDisable() { previous = null; enemyPips.Show(0, 0); }
    private void OnDestroy() { enemyPips.Release(); }
}

/// <summary>从场景中未启用的三个 PSD 图层取样。前两个点的中心决定整排的间距。</summary>
[System.Serializable]
public sealed class EnemyHealthPipRow
{
    public RectTransform outline, core, empty;
    private readonly List<(GameObject outline, GameObject core, GameObject empty)> pips =
        new List<(GameObject, GameObject, GameObject)>();
    private int shownCount, shownFilled;

    public void Show(int count, int filled)
    {
        filled = Mathf.Clamp(filled, 0, count);
        if (shownCount == count && shownFilled == filled) return;
        if (outline == null || core == null || empty == null) return;
        Vector2 step = empty.anchoredPosition - outline.anchoredPosition;
        while (pips.Count < count)
        {
            int index = pips.Count;
            pips.Add((Copy(outline, step * index), Copy(core, step * index), Copy(empty, step * (index - 1))));
        }
        for (int i = 0; i < Mathf.Max(count, shownCount); i++)
        {
            var pip = pips[i];
            pip.outline.SetActive(i < filled);
            pip.core.SetActive(i < filled);
            pip.empty.SetActive(i >= filled && i < count);
        }
        shownCount = count;
        shownFilled = filled;
    }

    private static GameObject Copy(RectTransform template, Vector2 offset)
    {
        var copy = Object.Instantiate(template, template.parent, false);
        copy.anchoredPosition += offset;
        copy.SetSiblingIndex(template.GetSiblingIndex() + 1);
        return copy.gameObject;
    }

    public void Release()
    {
        foreach (var pip in pips)
        {
            if (pip.outline != null) Object.Destroy(pip.outline);
            if (pip.core != null) Object.Destroy(pip.core);
            if (pip.empty != null) Object.Destroy(pip.empty);
        }
        pips.Clear();
    }
}
