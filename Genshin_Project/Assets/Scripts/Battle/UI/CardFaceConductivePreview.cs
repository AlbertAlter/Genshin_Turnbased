using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Isolated sample counts. The real renderer owns UV selection, tint, transitions and cleanup.</summary>
public sealed class CardFaceConductivePreview : CardFacePreviewEffect
{
    public UICardConductiveAssets resources;
    public CardFaceConductiveStyle layout = new CardFaceConductiveStyle();
#if UNITY_EDITOR
    public static Func<CardFaceConductiveStyle, string> WriteTable;
    public static Func<string, CardFaceConductiveStyle, string> WriteGlyphTable;
#endif
    private readonly List<UICardConductive> views = new List<UICardConductive>();
    private int sample = -1;
    private string failure;
    public override string FolderName => resources != null ? resources.FolderName : UICardConductiveAssets.Folder;
    public override string DisplayName => resources != null ? resources.DisplayName : "丽莎 · 引雷层数";
    public override string ParameterFile => resources != null ? resources.ParameterFile : CardFaceConductiveTable.FileName;
    public override string Error => failure ?? resources?.Error;
    public override int OwnerID => resources != null && resources.IsTalisman ? 1027 : 1011;
    public override string OwnerName => resources != null && resources.IsTalisman ? "七七" : "丽莎";
    public override string EffectType => resources != null && resources.IsTalisman ? "符印" : "层数";
    private static readonly CardFacePreviewParameter[] Fields =
    {
        new CardFacePreviewParameter("EnterSeconds", "出现（秒）", 0, 10),
        new CardFacePreviewParameter("ExitSeconds", "退出（秒）", 0, 10),
        new CardFacePreviewParameter("ChangeSeconds", "变化动效（秒）", 0, 10),
        new CardFacePreviewParameter("StartScale", "出现起始缩放", .1f, 2),
        new CardFacePreviewParameter("ChangeScaleAmount", "层数变化放大（%）", 0, 1, 100),
        new CardFacePreviewParameter("SizeMultiplier", "主体尺寸倍率", .25f, 3),
        new CardFacePreviewParameter("CoreOpacity", "主体不透明度（%）", 0, 1, 100),
        new CardFacePreviewParameter("GlowOpacity", "光晕不透明度（%）", 0, 1, 100)
    };
    private static readonly CardFacePreviewParameter[] TalismanFields = Array.FindAll(Fields,
        field => field.Key != "ChangeSeconds" && field.Key != "ChangeScaleAmount");
    public override IReadOnlyList<CardFacePreviewParameter> Parameters => resources != null && resources.IsTalisman ? TalismanFields : Fields;
    public override IEnumerator Load(bool reloadArt)
    { failure = null; if (resources == null) { failure = "引雷预览缺少共享资源引用。"; yield break; } yield return resources.Load(reloadArt); }
    public override bool Bind(IReadOnlyList<UICardFrameView> cards)
    {
        Clear();
        if (resources == null || !resources.Ready) return false;
        for (int i = 0; i < cards.Count; i++)
        {
            var go = new GameObject("ConductivePreview_" + i); go.transform.SetParent(transform, false);
            var view = go.AddComponent<UICardConductive>(); view.style = layout.Copy(); view.automaticClock = false;
            if (!view.Bind(cards[i].Root, cards[i].FrontEffects, resources))
            { failure = "引雷数字无法绑定卡位。"; Destroy(go); Clear(); return false; }
            views.Add(view); view.SetCount(resources.IsTalisman ? 1 : sample < 0 ? i % 3 + 1 : sample);
        }
        return true;
    }
    public override Dictionary<string, float> ReadParameters() => CardFaceConductiveTable.Values(views[0].style);
    public override void ApplyParameters(IReadOnlyDictionary<string, float> values)
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values) text.Add(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
        var parsed = CardFaceConductiveTable.Parse(text);
        foreach (var view in views) view.ApplyStyle(parsed.ApplyTo(view.style));
    }
    public override bool ReloadConfiguration() => resources != null && resources.ReloadParameters();
    public override string WriteParameters()
    {
#if UNITY_EDITOR
        if (WriteGlyphTable != null) return WriteGlyphTable(ParameterFile, views[0].style.Copy());
        if (WriteTable != null && (resources == null || !resources.IsTalisman)) return WriteTable(views[0].style.Copy());
#endif
        throw new InvalidOperationException("写回仅支持编辑器配表工具。");
    }
    public override void Show(int index) => views[index].Show();
    public override void Hide(int index, bool immediate) => views[index].Hide(immediate);
    public override void Advance(float seconds) { foreach (var view in views) view.Advance(seconds); }
    public override void DrawSampleControls(Rect bounds, GUIStyle label, GUIStyle button)
    {
        if (resources != null && resources.IsTalisman)
        { GUI.Label(bounds, "度厄真符 · 使用公共播放/隐藏控制；无层数，不操作真实状态", label); return; }
        GUI.Label(new Rect(bounds.x, bounds.y, bounds.width, 34), "引雷层数 · 仅作预览输入，不写入参数表", label);
        for (int i = -1; i <= 4; i++)
        {
            string text = i < 0 ? "1 / 2 / 3 对照" : i + " 层";
            if (GUI.Button(new Rect(bounds.x + (i + 1) * 166, bounds.y + 44, 154, 40), text, button))
            {
                sample = i;
                for (int c = 0; c < views.Count; c++) views[c].SetCount(sample < 0 ? c % 3 + 1 : sample);
            }
        }
    }
    public override void Clear()
    {
        foreach (var view in views) if (view != null) { view.Clear(); Destroy(view.gameObject); }
        views.Clear();
    }
}
