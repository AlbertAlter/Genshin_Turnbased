using System;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>Read-only AP presentation: targeting previews never spend battle AP.</summary>
public sealed class BattlefieldApView : MonoBehaviour
{
    public sealed class Style
    {
        public Color Text = new Color32(221, 210, 193, 255);
        public Color Loss = new Color32(255, 105, 105, 255);
        public Color Gain = new Color32(111, 218, 50, 255);
        public Color Outline = new Color32(113, 103, 88, 255);
        public float OutlinePixels = 3f;
        public float CountDuration = .3f;
        public float FloatDuration = .8f;
        public float FloatDistance = 24f;
        public float PulsePeriod = 1.2f;
        public float PulseMinAlpha = .65f;
        public bool PreviewByAffordability;
    }

    private TMP_Text value, loss, gain;
    private Vector2 lossOrigin, gainOrigin;
    private BattleInputController input;
    private BattlefieldSwitchController switcher;
    private ActionPointManager manager;
    private Style style;
    private int lastAP;
    private float displayedAP, countFrom, countStart;
    private int floatingDelta;
    private float floatStart;
    private bool initialized;

    public static Style LoadStyle()
    {
        var result = new Style();
        ApplyAnimation(result);
        return result;
    }

    private static void ApplyAnimation(Style result)
    {
        var p = BattlefieldAnimationParameters.Load();
        result.CountDuration=p.Get("AP.CountDuration",.3f,.001f);
        result.FloatDuration=p.Get("AP.FloatDuration",.8f,.001f);
        result.FloatDistance=p.Get("AP.FloatDistance",24);
        result.PulsePeriod=p.Get("AP.PulsePeriod",1.2f,.001f);
        result.PulseMinAlpha=p.Get("AP.PulseMinAlpha",.65f,0,1);
    }

    public void ReloadAnimationParameters()
    {
        if (style == null) style = new Style();
        ApplyAnimation(style);
    }

    private void Start()
    {
        style = LoadStyle();
        var bindings = FindObjectsOfType<PsdUiBinding>(true)
            .Where(item => item.gameObject.scene == gameObject.scene).ToArray();
        value = FindLabel(bindings, "APValue");
        loss = FindLabel(bindings, "APSpendHint");
        gain = FindLabel(bindings, "APGainHint");
        // The scene, not the table, owns the manually adjusted base colors.
        if (value != null) style.Text = value.color;
        if (loss != null) style.Loss = loss.color;
        if (gain != null) style.Gain = gain.color;
        if (loss != null) lossOrigin = loss.rectTransform.anchoredPosition;
        if (gain != null) gainOrigin = gain.rectTransform.anchoredPosition;
        HideHints();
    }

    private static TMP_Text FindLabel(PsdUiBinding[] bindings, string key) =>
        bindings.FirstOrDefault(item => item.bindingKey == key)?.GetComponent<TMP_Text>();

    private void LateUpdate()
    {
        if (style == null) return;
        var battle = BattleManager.Instance;
        var currentManager = battle != null ? battle.APManager : null;
        if (currentManager == null)
        {
            initialized = false;
            HideHints();
            return;
        }
        if (input == null) input = FindObjectsOfType<BattleInputController>()
            .FirstOrDefault(item => item.gameObject.scene == gameObject.scene);
        if (switcher == null) switcher = FindObjectsOfType<BattlefieldSwitchController>()
            .FirstOrDefault(item => item.gameObject.scene == gameObject.scene);
        float now = Time.unscaledTime;
        int actual = currentManager.CurrentAP;
        if (!initialized || manager != currentManager)
        {
            manager = currentManager;
            initialized = true;
            lastAP = actual;
            countFrom = displayedAP = actual;
            countStart = now;
            floatingDelta = 0;
        }
        else if (actual != lastAP)
        {
            floatingDelta = actual - lastAP;
            floatStart = countStart = now;
            countFrom = displayedAP;
            lastAP = actual;
        }
        float countT = Mathf.Clamp01((now-countStart)/style.CountDuration);
        displayedAP = Mathf.Lerp(countFrom, actual, 1f-Mathf.Pow(1f-countT, 3f));
        if (value != null)
        {
            value.text = $"AP: {Mathf.RoundToInt(displayedAP)}/{manager.MaxAP}";
            value.color = style.Text;
        }

        int? previewCost = GetPreviewCost(battle, input, switcher);
        if (previewCost.HasValue)
        {
            int cost = previewCost.Value;
            if (cost > 0)
            {
                Color color = style.PreviewByAffordability && manager.CanAfford(cost) ? style.Gain : style.Loss;
                float pulse = .5f + .5f * Mathf.Cos(now * Mathf.PI * 2f/style.PulsePeriod);
                color.a *= Mathf.Lerp(style.PulseMinAlpha, 1f, pulse);
                ShowHint(loss, -cost, color, lossOrigin);
            }
            else HideHints();
            // A previous delta must not reappear after a later cancelled selection.
            floatingDelta = 0;
            return;
        }
        float floatT = Mathf.Clamp01((now-floatStart)/style.FloatDuration);
        if (floatingDelta == 0 || floatT >= 1f) { HideHints(); return; }
        bool isGain = floatingDelta > 0;
        Color floatingColor = isGain ? style.Gain : style.Loss;
        floatingColor.a *= 1f-floatT;
        Vector2 origin = isGain ? gainOrigin : lossOrigin;
        ShowHint(isGain ? gain : loss, floatingDelta, floatingColor,
            origin + Vector2.up * (style.FloatDistance*floatT));
    }

    /// <summary>只读费用预览；换人模态优先于其下保留的技能目标选择。</summary>
    public static int? GetPreviewCost(BattleManager battle, BattleInputController input,
        BattlefieldSwitchController switcher)
    {
        if (battle == null) return null;
        if (switcher != null && switcher.IsSwitching)
            return battle.CanSwitchActiveAllyTo(switcher.SelectedSlot)
                ? battle.GetSwitchAPCost(switcher.SelectedSlot) : 0;
        if (input == null || !input.IsSelecting || input.PendingSkill < 0) return null;
        var caster = input.PendingAlly != null ? input.PendingAlly : battle.GetActiveAlly();
        if (caster == null || caster.HasPendingEffectSequence || battle.IsResolving) return null;
        return caster.GetBoundSkillAPCost(input.PendingSkill);
    }

    private void ShowHint(TMP_Text label, int delta, Color color, Vector2 position)
    {
        var other = label == loss ? gain : loss;
        if (other != null) other.gameObject.SetActive(false);
        if (label == null) return;
        label.gameObject.SetActive(true);
        label.text = delta > 0 ? "+"+delta : delta.ToString();
        label.color = color;
        label.rectTransform.anchoredPosition = position;
    }

    private void HideHints()
    {
        if (loss != null) { loss.gameObject.SetActive(false); loss.rectTransform.anchoredPosition = lossOrigin; }
        if (gain != null) { gain.gameObject.SetActive(false); gain.rectTransform.anchoredPosition = gainOrigin; }
    }

    private void OnDisable()
    {
        HideHints();
        initialized = false;
    }
}
