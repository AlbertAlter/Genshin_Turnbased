using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>独立跳字配表；运行时只读 StreamingAssets/Data/UI，不读取 Charts 源表。</summary>
public class BattlefieldPopupStyle
{
    public bool Enabled;
    public float FontHeightRatio, Lifetime, FadeIn, FadeOut, RiseHeightRatio;
    public int MaxVisible;
    public Color Color;

    public float Alpha(float age)
    {
        float alpha = FadeIn > 0 ? Mathf.Clamp01(age / FadeIn) : 1;
        if (FadeOut > 0) alpha *= Mathf.Clamp01((Lifetime - age) / FadeOut);
        return age >= Lifetime ? 0 : alpha;
    }
}

public sealed class BattlefieldDamagePopupStyle : BattlefieldPopupStyle
{
    public const string FileName = "DamagePopup.xlsx";
    public bool ShowZero;
    public float XMin, XMax, YMin, YMax;
    public float CriticalFontSizeMultiplier = 1;
    public int Decimals, AmountMode;

    public static BattlefieldDamagePopupStyle Load()
    {
        return ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    }

    // 编辑器同步时也使用同一解析器校验，字段错误不能静默忽略或改用另一份参数。
    public static BattlefieldDamagePopupStyle ReadFile(string path)
        => Parse(ReadParameters(path));

    internal static Dictionary<string, string> ReadParameters(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["Parameters"];
        if (sheet == null || sheet.Dimension == null || sheet.Cells[1, 1].Text != "Key" || sheet.Cells[1, 2].Text != "Value")
            throw new InvalidDataException("跳字表需要 Parameters 子表，前两列表头为 Key、Value。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            string key = sheet.Cells[row, 1].Text.Trim();
            if (key.Length == 0) continue;
            if (values.ContainsKey(key)) throw new InvalidDataException("跳字参数重复：" + key);
            values.Add(key, Convert.ToString(sheet.Cells[row, 2].Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
        }
        return values;
    }

    public static BattlefieldDamagePopupStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "Enabled", "CenterXMinRatio", "CenterXMaxRatio", "CenterYMinRatio", "CenterYMaxRatio",
            "FontHeightRatio", "LifetimeSeconds", "FadeOutSeconds", "RiseHeightRatio", "MaxVisible", "Decimals",
            "AmountMode", "ShowZeroDamage", "Color" };
        if (values == null) throw new InvalidDataException("跳字参数缺少。");
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal) { "TimeBetweenHits", "HitTimeRandomVariant", "HitIntervalSeconds", "MediumTimeJitterSeconds", "FullTimeJitterSeconds", "MediumRandomScale", "CriticalFontSizeMultiplier" };
        foreach (string key in values.Keys)
            if (!allowed.Contains(key)) throw new InvalidDataException("未知跳字参数：" + key);
        foreach (string key in keys)
            if (!values.ContainsKey(key)) throw new InvalidDataException("缺少跳字参数：" + key);
        BattlePresentationConfiguration.Parse(values);
        float Number(string key)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("跳字参数必须为有限数字：" + key);
            return value;
        }
        int Integer(string key, int min, int max)
        {
            float value = Number(key);
            if (value < min || value > max || value != (int)value) throw new InvalidDataException("跳字整数参数越界：" + key);
            return (int)value;
        }
        var style = new BattlefieldDamagePopupStyle
        {
            Enabled = Integer("Enabled", 0, 1) == 1,
            XMin = Number("CenterXMinRatio"), XMax = Number("CenterXMaxRatio"),
            YMin = Number("CenterYMinRatio"), YMax = Number("CenterYMaxRatio"),
            FontHeightRatio = Number("FontHeightRatio"), Lifetime = Number("LifetimeSeconds"),
            FadeOut = Number("FadeOutSeconds"), RiseHeightRatio = Number("RiseHeightRatio"),
            MaxVisible = Integer("MaxVisible", 1, 2048), Decimals = Integer("Decimals", 0, 3),
            AmountMode = Integer("AmountMode", 0, 1), ShowZero = Integer("ShowZeroDamage", 0, 1) == 1,
            CriticalFontSizeMultiplier = values.ContainsKey("CriticalFontSizeMultiplier") ? Number("CriticalFontSizeMultiplier") : 1,
        };
        if (style.XMin > style.XMax || style.YMin > style.YMax || style.FontHeightRatio <= 0 ||
            style.Lifetime <= 0 || style.FadeOut < 0 || style.FadeOut > style.Lifetime || style.CriticalFontSizeMultiplier <= 0)
            throw new InvalidDataException("跳字范围、字号或持续/淡出时长无效。");
        if (!ColorUtility.TryParseHtmlString(values["Color"], out style.Color))
            throw new InvalidDataException("跳字 Color 必须为 HTML 颜色，例如 #FFFFFFFF。");
        return style;
    }

    public float FontSizeMultiplier(bool critical) => critical ? CriticalFontSizeMultiplier : 1;

    /// <summary>只有实际扣血才显示伤害数字；仅削盾、全部吸收或免伤仍保留原结算声明。</summary>
    public bool TryGetAmount(DamageEventData damage, out float amount)
    {
        amount = 0f;
        if (damage == null || damage.IsImmune || !(damage.ActualHPDamage > 0f)
            || float.IsInfinity(damage.ActualHPDamage)) return false;
        amount = AmountMode == 0 ? damage.DamageBeforeShield : damage.ActualHPDamage;
        return !float.IsNaN(amount) && !float.IsInfinity(amount) && amount >= 0f
            && (ShowZero || amount > 0f);
    }
}

/// <summary>纯显示的确定性空间采样，不抽取 BattleRandom，也不影响伤害或暴击结果。</summary>
public static class BattlefieldDamagePopupLayout
{
    public static Vector2 Sample(BattlefieldDamagePopupStyle style, long session, long eventId, long hit, int target)
    {
        uint hash = 2166136261;
        unchecked
        {
            foreach (long value in new[] { session, eventId, hit, (long)target })
            { hash = (hash ^ (uint)value) * 16777619; hash = (hash ^ (uint)(value >> 32)) * 16777619; }
        }
        return new Vector2(Mathf.Lerp(style.XMin, style.XMax, Unit(hash)),
            Mathf.Lerp(style.YMin, style.YMax, Unit(hash ^ 0x9E3779B9u)));
    }

    private static float Unit(uint value)
    {
        unchecked { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16; }
        return (value >> 8) / 16777216f;
    }

    public static string Format(float amount, int decimals)
    {
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);
    }
}
