using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>仅读取已发布的元素栏动效参数；图标尺寸、间距及锚点仍由场景保存。</summary>
public static class ElementAuraMotionTable
{
    public const string FileName = "ElementAuraMotion.xlsx";
    public static ElementAuraMotion.Style ReadPublished() => CampaignUiTables.Read(() => ReadFile(
        Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName)));
    public static ElementAuraMotion.Style ReadFile(string path) => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));
    public static ElementAuraMotion.Style Parse(IReadOnlyDictionary<string, string> values)
    {
        if (values == null || values.Count != 12) throw new InvalidDataException("元素栏动效需要12项参数。");
        float N(string key, float min, float max)
        {
            if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max)
                throw new InvalidDataException($"元素栏参数{key}须为{min}～{max}的有限数字。");
            return n;
        }
        var style = new ElementAuraMotion.Style {
            Approach = N("ApproachSeconds", 0, 5), Gather = N("GatherSeconds", 0, 5),
            Flash = N("FlashSeconds", 0, 5), Hold = N("HoldSeconds", 0, 5), Settle = N("SettleSeconds", 0, 5),
            Refresh = N("RefreshSeconds", .01f, 5), Travel = N("Travel", 0, 100),
            ContactX = N("ContactX", 0, 100), ResidentX = N("ResidentX", 0, 100),
            RefreshScale = N("RefreshScale", 0, 1), Brightness = N("Brightness", 0, 1), Burst = N("Burst", 0, 1)
        };
        if (style.ContactX > style.Travel) throw new InvalidDataException("元素栏接触位置不能远于起点。");
        return style;
    }
}
