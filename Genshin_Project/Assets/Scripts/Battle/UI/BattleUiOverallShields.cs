using System.Collections.Generic;
using UnityEngine;

/// <summary>与 IBattleUiShields 完全独立。一次命令是一份场地盾及其全体受保护卡牌。</summary>
public interface IBattleUiOverallShields
{
    bool SetOverallShield(long instanceId, string element, IReadOnlyList<BattleUiCardBinding> targets);
    void RemoveOverallShield(long instanceId);
    void ClearOverallShields();
}

/// <summary>可选：场景配置的特效Prefab接收元素信息；不由显示模块猜测颜色或材质。</summary>
public interface IOverallShieldVisual
{
    void SetElement(string element);
}

/// <summary>每份场地盾拥有独立的显示实例，可绑定专用Prefab或借用公共护盾膜的样式资源。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Overall Shields")]
public sealed class BattleUiOverallShields : MonoBehaviour, IBattleUiOverallShields
{
    public GameObject effectPrefab;
    public BattleUiShields membraneStyle;
    private sealed class Visual
    {
        public GameObject Node;
        public UICardShield Membrane;
        public UIShieldStyle AppliedStyle;
        public string Element;
        public double RevealStart;
        public double FadeTime;
        public float Alpha = 1;
        public readonly ShieldSweepClock Clock = new ShieldSweepClock();
    }
    private readonly Dictionary<long, Dictionary<RectTransform, Visual>> groups
        = new Dictionary<long, Dictionary<RectTransform, Visual>>();
    private readonly HashSet<RectTransform> current = new HashSet<RectTransform>();
    private readonly List<RectTransform> stale = new List<RectTransform>();
    private readonly List<Visual> fading = new List<Visual>();

    public bool SetOverallShield(long instanceId, string element, IReadOnlyList<BattleUiCardBinding> targets)
    {
        if (!isActiveAndEnabled || targets == null) return false;
        if (effectPrefab == null && (membraneStyle == null || !membraneStyle.isActiveAndEnabled || !membraneStyle.Ready))
        { RemoveOverallShield(instanceId); return false; }
        if (!groups.TryGetValue(instanceId, out var group))
            groups.Add(instanceId, group = new Dictionary<RectTransform, Visual>());
        current.Clear();
        foreach (var card in targets)
        {
            if (card?.art == null) continue;
            var parent = card.shieldLayer != null ? card.shieldLayer : effectPrefab != null ? card.art : null;
            if (parent == null || !current.Add(card.art)) continue;
            bool created = !group.TryGetValue(card.art, out var visual) || visual.Node == null;
            if (created)
            {
                var node = effectPrefab != null ? Instantiate(effectPrefab, parent, false)
                    : new GameObject("OverallShield", typeof(RectTransform), typeof(UICardShield));
                node.name = "OverallShield_" + instanceId;
                group[card.art] = visual = new Visual { Node = node, Membrane = effectPrefab == null ? node.GetComponent<UICardShield>() : null };
            }
            if (created || visual.Node.transform.parent != parent)
            {
                visual.Node.transform.SetParent(parent, false);
                int index = 0;
                foreach (var pair in groups)
                    if (pair.Key < instanceId && pair.Value.TryGetValue(card.art, out var older)
                        && older.Node != null && older.Node.transform.parent == parent) index++;
                visual.Node.transform.SetSiblingIndex(index);
            }
            if (visual.Membrane != null)
            {
                var style = membraneStyle.Settings;
                if (created)
                {
                    visual.RevealStart = membraneStyle.ElapsedSeconds;
                    visual.Clock.Restart(visual.RevealStart + style.Number("RevealDuration"));
                }
                float brightness = style.Number("SweepOpacity") * visual.Clock.Sample(membraneStyle.ElapsedSeconds,
                    style.Number("SweepDuration"), style.Number("SweepInterval"), style.Number("SweepIntervalJitter"),
                    style.Number("SweepFadeFraction"), true, out float progress);
                if (visual.AppliedStyle != style) membraneStyle.InitializeMembrane(visual.Membrane);
                if (visual.AppliedStyle != style || visual.Element != element)
                    visual.Membrane.Apply(style, membraneStyle.Palette.GetColor(element), style.Number("MembraneOpacity"),
                        style.Number(element == "None" ? "NeutralTintOpacity" : "TintOpacity"), Vector2.zero, Vector2.zero,
                        progress, brightness, style.Number("SweepWidth"), style.Number("SweepAngle"), 1);
                else visual.Membrane.SetSweep(progress, brightness);
                visual.Membrane.SetReveal(UIShieldStyle.SampleReveal(membraneStyle.ElapsedSeconds - visual.RevealStart,
                    style.Number("RevealDuration")), style.Number("RevealSoftness"), style.Number("RevealEdgeWidth"), style.Number("RevealEdgeOpacity"));
                visual.AppliedStyle = style;
            }
            else if (created || visual.Element != element)
                foreach (var component in visual.Node.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is IOverallShieldVisual sink) sink.SetElement(element);
            visual.Element = element;
            if (created) visual.Node.SetActive(true);
        }
        stale.Clear();
        foreach (var card in group.Keys)
            if (card == null || !current.Contains(card)) stale.Add(card);
        foreach (var card in stale) { Release(group[card].Node); group.Remove(card); }
        return true;
    }

    public void RemoveOverallShield(long instanceId)
    {
        if (!groups.TryGetValue(instanceId, out var group)) return;
        foreach (var visual in group.Values)
        {
            if (visual.Membrane != null && visual.AppliedStyle != null && membraneStyle != null
                && membraneStyle.isActiveAndEnabled && visual.AppliedStyle.Number("FadeOutSeconds") > 0)
            {
                // 冻结当前展开/扫光，只淡出这份场地盾，不影响其他盾。
                visual.FadeTime = membraneStyle.ElapsedSeconds;
                fading.Add(visual);
            }
            else Release(visual.Node);
        }
        groups.Remove(instanceId);
    }
    public void ClearOverallShields()
    {
        foreach (var group in groups.Values)
            foreach (var visual in group.Values) Release(visual.Node);
        groups.Clear();
        foreach (var visual in fading) Release(visual.Node);
        fading.Clear();
    }
    private void Update()
    {
        for (int i = fading.Count - 1; i >= 0; --i)
        {
            var visual = fading[i];
            if (visual.Node == null || membraneStyle == null || !membraneStyle.isActiveAndEnabled)
            { Release(visual.Node); fading.RemoveAt(i); continue; }
            double now = membraneStyle.ElapsedSeconds;
            visual.Alpha = UIShieldStyle.StepAlpha(visual.Alpha, false, (float)(now - visual.FadeTime),
                visual.AppliedStyle.Number("FadeOutSeconds"));
            visual.FadeTime = now;
            visual.Membrane.SetAlpha(visual.Alpha);
            if (visual.Alpha <= 0) { Release(visual.Node); fading.RemoveAt(i); }
        }
    }
    private void Release(GameObject visual)
    {
        if (visual == null) return;
        visual.SetActive(false); // 同帧全队撤下，Destroy的帧末延迟不留下单张卡残影。
        Destroy(visual);
    }
    private void OnDisable() => ClearOverallShields();
    private void OnDestroy() => ClearOverallShields();
}
