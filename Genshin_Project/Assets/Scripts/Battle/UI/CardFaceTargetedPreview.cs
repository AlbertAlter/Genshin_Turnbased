using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Preview adapter for the existing shared targeting renderer; contains no duplicate animation.</summary>
public sealed class CardFaceTargetedPreview : CardFacePreviewEffect
{
    public UICardTargetAssets resources;
    public UICardTargetIndicator[] indicators;
    private readonly List<UICardTargetIndicator> views = new List<UICardTargetIndicator>();
    private readonly List<GameObject> owned = new List<GameObject>();
#if UNITY_EDITOR
    public static Func<CardTargetIndicatorStyle, string> WriteTable;
#endif
    public override string FolderName => "Targeted";
    public override string DisplayName => "锁定准心";
    public override string ParameterFile => CardTargetIndicatorTable.FileName;
    public override string Error => failure ?? resources?.Error;
    private string failure;
    private static readonly CardFacePreviewParameter[] Fields =
    {
        new CardFacePreviewParameter("EnterSeconds", "出现（秒）", 0, 10),
        new CardFacePreviewParameter("SettleSeconds", "落稳（秒）", 0, 10),
        new CardFacePreviewParameter("ExitSeconds", "退出（秒）", 0, 10),
        new CardFacePreviewParameter("TipTravel", "四角外移（设计像素）", 0, 100),
        new CardFacePreviewParameter("TipOvershoot", "四角回弹（设计像素）", 0, 100),
        new CardFacePreviewParameter("RingStartScale", "出现时圆弧倍率", 1, 1.2f),
        new CardFacePreviewParameter("SizeMultiplier", "整体尺寸倍率", .25f, 1.5f),
        new CardFacePreviewParameter("IdleMotion", "持续慢转与呼吸", 0, 1, toggle: true),
        new CardFacePreviewParameter("IdleCycleSeconds", "呼吸周期（秒）", .1f, 60),
        new CardFacePreviewParameter("IdleScaleAmount", "呼吸缩放（%）", 0, .1f, 100),
        new CardFacePreviewParameter("IdleGlowDip", "光晕明暗幅度（%）", 0, 1, 100),
        new CardFacePreviewParameter("RotationDegreesPerSecond", "整组旋转（度/秒）", -360, 360),
        new CardFacePreviewParameter("OuterGlow", "外发光", 0, 1, toggle: true),
        new CardFacePreviewParameter("GlowSize", "外发光大小（设计像素）", 0, 120),
        new CardFacePreviewParameter("GlowSpread", "外发光扩展（%）", 0, 100),
        new CardFacePreviewParameter("GlowRed", "外发光红色通道", 0, 1),
        new CardFacePreviewParameter("GlowGreen", "外发光绿色通道", 0, 1),
        new CardFacePreviewParameter("GlowBlue", "外发光蓝色通道", 0, 1),
        new CardFacePreviewParameter("GlowOpacity", "外发光不透明度（%）", 0, 1, 100),
    };
    public override IReadOnlyList<CardFacePreviewParameter> Parameters => Fields;
    public override IEnumerator Load(bool reloadArt)
    {
        failure = null;
        if (resources == null) { failure = "准心预览未绑定共享资源。"; yield break; }
        yield return resources.Load(reloadArt);
    }
    public override bool Bind(IReadOnlyList<UICardFrameView> cards)
    {
        Clear(); failure = null;
        if (resources == null || !resources.Ready || indicators == null || indicators.Length == 0 || indicators[0] == null)
        { failure = "准心预览需要已就绪素材与一个显式布局模板。"; return false; }
        for (int i = 0; i < cards.Count; i++)
        {
            var view = i < indicators.Length ? indicators[i] : null;
            if (i >= indicators.Length)
            {
                var go = new GameObject("TargetedPreview_" + i); go.transform.SetParent(transform, false);
                owned.Add(go); view = go.AddComponent<UICardTargetIndicator>(); view.style = indicators[0].style.Copy();
            }
            if (view == null) { failure = "卡位缺少准心组件：" + i; Clear(); return false; }
            views.Add(view);
            view.automaticClock = false; view.Paused = false;
            if (!view.Bind(cards[i], resources)) { failure = view.Error; Clear(); return false; }
        }
        return true;
    }
    public override Dictionary<string, float> ReadParameters() => CardTargetIndicatorTable.Values(views[0].style);
    public override void ApplyParameters(IReadOnlyDictionary<string, float> values)
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values) text.Add(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
        var parsed = CardTargetIndicatorTable.Parse(text);
        foreach (var view in views) view.ApplyStyle(parsed.ApplyTo(view.style));
    }
    public override bool ReloadConfiguration() => resources != null && resources.ReloadParameters();
    public override string WriteParameters()
    {
#if UNITY_EDITOR
        if (WriteTable != null) return WriteTable(views[0].style.Copy());
#endif
        throw new InvalidOperationException("写回仅在编辑器中通过配表写回工具执行。");
    }
    public override void Show(int index) => views[index].Show();
    public override void Hide(int index, bool immediate) => views[index].Hide(immediate);
    public override void Advance(float seconds) { foreach (var view in views) if (view != null) view.Advance(seconds); }
    public override void Clear()
    {
        foreach (var view in views) if (view != null) view.Clear();
        foreach (var go in owned) if (go != null) Destroy(go);
        views.Clear(); owned.Clear();
    }
}
