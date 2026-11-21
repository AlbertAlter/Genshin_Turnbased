using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Published values from the shared status-name HTML/Excel preview.</summary>
public sealed class UIStatusNamePopupStyle : BattlefieldPopupStyle
{
    public const string Folder = "StatusPopUp";
    public const string FileName = "StatusPopupPreview.xlsx";
    public float AnchorY, OffsetX, OffsetY, CardGapHeightRatio, StackGapHeightRatio;
    public float FontSize, LineHeight, MaxWidth, PaddingX, PaddingY;
    public float BackgroundOpacity, EndFadeRatio, GoldBorderWidth, GoldBorderOpacity;
    public Color BackgroundColor, GoldColor;

    public static UIStatusNamePopupStyle Load() => CampaignUiTables.Read(ReadPublished);
    private static UIStatusNamePopupStyle ReadPublished()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", Folder, FileName));
    public static UIStatusNamePopupStyle ReadFile(string path)
        => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));

    public static UIStatusNamePopupStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "CardHeight", "AnchorY", "OffsetX", "OffsetY", "CardGapHeightRatio", "StackGapHeightRatio",
            "RiseHeightRatio", "LifetimeSeconds", "FadeInSeconds", "FadeOutSeconds", "MaxVisible", "SpawnIntervalSeconds",
            "LoopDelaySeconds", "FontSize", "LineHeight", "MaxWidth", "PaddingX", "PaddingY", "BackgroundOpacity",
            "EndFadeRatio", "GoldBorderWidth", "GoldBorderOpacity", "TextColor", "BackgroundColor", "GoldColor",
            "ShowGuides", "BackgroundOnly" };
        if (values == null || values.Count != keys.Length) throw new InvalidDataException("状态名称浮层参数缺少或包含未知字段。");
        foreach (string key in keys) if (!values.ContainsKey(key)) throw new InvalidDataException("缺少状态名称浮层参数：" + key);
        float Number(string key, float minimum = float.NegativeInfinity, float maximum = float.PositiveInfinity)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new InvalidDataException("状态名称浮层参数越界或不是有限数字：" + key);
            return value;
        }
        int Integer(string key, int min, int max)
        {
            float value = Number(key, min, max);
            if (value != (int)value) throw new InvalidDataException("状态名称浮层参数必须为整数：" + key);
            return (int)value;
        }
        Color HtmlColor(string key)
        {
            if (values[key].Length != 7 || !ColorUtility.TryParseHtmlString(values[key], out Color color))
                throw new InvalidDataException("状态名称浮层颜色必须为 #RRGGBB：" + key);
            return color;
        }
        var style = new UIStatusNamePopupStyle
        {
            Enabled = true,
            AnchorY = Number("AnchorY"), OffsetX = Number("OffsetX"), OffsetY = Number("OffsetY"),
            CardGapHeightRatio = Number("CardGapHeightRatio", 0), StackGapHeightRatio = Number("StackGapHeightRatio", 0),
            RiseHeightRatio = Number("RiseHeightRatio", 0), Lifetime = Number("LifetimeSeconds", .001f),
            FadeIn = Number("FadeInSeconds", 0), FadeOut = Number("FadeOutSeconds", 0), MaxVisible = Integer("MaxVisible", 1, 64),
            FontSize = Number("FontSize", 1), LineHeight = Number("LineHeight", 1), MaxWidth = Number("MaxWidth", 1),
            PaddingX = Number("PaddingX", 0), PaddingY = Number("PaddingY", 0),
            BackgroundOpacity = Number("BackgroundOpacity", 0, 1), EndFadeRatio = Number("EndFadeRatio", 0, .45f),
            GoldBorderWidth = Number("GoldBorderWidth", 0), GoldBorderOpacity = Number("GoldBorderOpacity", 0, 1),
            Color = HtmlColor("TextColor"), BackgroundColor = HtmlColor("BackgroundColor"), GoldColor = HtmlColor("GoldColor"),
        };
        if (style.AnchorY != 0 && style.AnchorY != .5f && style.AnchorY != 1)
            throw new InvalidDataException("状态名称浮层 AnchorY 只允许 0、0.5、1。");
        if (style.FadeIn + style.FadeOut > style.Lifetime || style.PaddingX * 2 + style.FontSize > style.MaxWidth)
            throw new InvalidDataException("状态名称浮层淡入淡出总时长或文字留白超出可用范围。");
        // These fields configure the browser preview, not live battle scheduling or card dimensions.
        Number("CardHeight", 1); Number("SpawnIntervalSeconds", 0); Number("LoopDelaySeconds", 0);
        Integer("ShowGuides", 0, 1); Integer("BackgroundOnly", 0, 1);
        return style;
    }

    public float Bottom(float anchorY, float cardHeight, float? previousTop = null)
    {
        float bottom = anchorY + OffsetY + cardHeight * CardGapHeightRatio;
        return previousTop.HasValue ? Math.Max(bottom, previousTop.Value + cardHeight * StackGapHeightRatio) : bottom;
    }
    public Vector2 Size(float preferredTextWidth, int lines)
        => new Vector2(Mathf.Clamp(preferredTextWidth + PaddingX * 2, Mathf.Min(126, MaxWidth), MaxWidth),
            Math.Max(1, lines) * FontSize * LineHeight + PaddingY * 2);
}
