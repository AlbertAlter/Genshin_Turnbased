using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>状态赋予提示。固定卡面外、水平居中，不使用伤害的随机出生范围。</summary>
public sealed class BattlefieldStatusPopupStyle : BattlefieldPopupStyle
{
    public const string FileName = "StatusPopup.xlsx";
    public float CardGapHeightRatio, StackGapHeightRatio;

    public static BattlefieldStatusPopupStyle Load()
        => ReadFile(BattleTimelineSettings.RuntimePath(FileName));

    public static BattlefieldStatusPopupStyle ReadFile(string path)
        => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));

    public static BattlefieldStatusPopupStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "Enabled", "FontHeightRatio", "LifetimeSeconds", "FadeInSeconds", "FadeOutSeconds",
            "RiseHeightRatio", "CardGapHeightRatio", "StackGapHeightRatio", "MaxVisible", "Color" };
        if (values == null || values.Count != keys.Length) throw new InvalidDataException("状态跳字参数缺少或包含未知字段。");
        foreach (string key in keys)
            if (!values.ContainsKey(key)) throw new InvalidDataException("缺少状态跳字参数：" + key);
        float Number(string key)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidDataException("状态跳字参数必须为有限数字：" + key);
            return number;
        }
        int Integer(string key, int min, int max)
        {
            float number = Number(key);
            if (number < min || number > max || number != (int)number) throw new InvalidDataException("状态跳字整数参数越界：" + key);
            return (int)number;
        }
        var style = new BattlefieldStatusPopupStyle
        {
            Enabled = Integer("Enabled", 0, 1) == 1, FontHeightRatio = Number("FontHeightRatio"),
            Lifetime = Number("LifetimeSeconds"), FadeIn = Number("FadeInSeconds"), FadeOut = Number("FadeOutSeconds"),
            RiseHeightRatio = Number("RiseHeightRatio"), CardGapHeightRatio = Number("CardGapHeightRatio"),
            StackGapHeightRatio = Number("StackGapHeightRatio"), MaxVisible = Integer("MaxVisible", 1, 2048),
        };
        if (style.FontHeightRatio <= 0 || style.Lifetime <= 0 || style.FadeIn < 0 || style.FadeOut < 0 ||
            style.FadeIn + style.FadeOut > style.Lifetime || style.RiseHeightRatio < 0 ||
            style.CardGapHeightRatio < 0 || style.StackGapHeightRatio < 0)
            throw new InvalidDataException("状态跳字字号、时长或卡面外间距无效（仅允许向上移动）。");
        if (!UnityEngine.ColorUtility.TryParseHtmlString(values["Color"], out style.Color))
            throw new InvalidDataException("状态跳字 Color 必须为 HTML 颜色。");
        return style;
    }

    public static string TextFor(StatusEventData fact)
        => fact != null && fact.Kind == BattleEventKind.StatusAdded && fact.Display == 1 &&
            !string.IsNullOrWhiteSpace(fact.StatusName) ? fact.StatusName : string.Empty;

    // 输入输出都在弹字层坐标系：文字矩形底边始终在卡面顶边之外。
    public float CenterAbove(float cardTop, float cardHeight, float textHeight, float? previousTop = null)
    {
        float bottom = cardTop + cardHeight * CardGapHeightRatio;
        if (previousTop.HasValue)
            bottom = Math.Max(bottom, previousTop.Value + cardHeight * StackGapHeightRatio);
        return bottom + textHeight * .5f;
    }
}
