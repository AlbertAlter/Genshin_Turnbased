using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>效果表只覆盖模式和可选hit间隔；范围/默认数值仍属于UI参数表。</summary>
public sealed class BattlePresentationConfiguration
{
    public const string EffectFileName = "SkillUIEffects.xlsx";
    public sealed class EffectOverride
    {
        public int? X, Y, Time;
        public double? HitInterval;
    }
    private readonly Dictionary<string, EffectOverride> effects;
    public double HitInterval { get; }
    public double MediumJitter { get; }
    public double FullJitter { get; }
    public float MediumRandomScale { get; }

    public BattlePresentationConfiguration(Dictionary<string, EffectOverride> effects = null,
        double hitInterval = 0, double mediumJitter = 0, double fullJitter = 0, float mediumRandomScale = .5f)
    {
        new BattlePresentationTiming(hitInterval, mediumJitter);
        new BattlePresentationTiming(hitInterval, fullJitter);
        if (fullJitter < mediumJitter || float.IsNaN(mediumRandomScale) || mediumRandomScale < 0 || mediumRandomScale > 1)
            throw new InvalidDataException("UI随机范围必须满足中等不大于完整范围。");
        this.effects = new Dictionary<string, EffectOverride>(StringComparer.Ordinal);
        if (effects != null)
            foreach (var entry in effects)
            {
                var value = entry.Value ?? throw new InvalidDataException("效果表现配置不能为空。");
                foreach (int? mode in new[] { value.X, value.Y, value.Time })
                    if (mode.HasValue && (mode < 0 || mode > 2)) throw new InvalidDataException("表现模式必须为0/1/2。");
                if (value.HitInterval.HasValue) new BattlePresentationTiming(value.HitInterval.Value);
                this.effects.Add(entry.Key, new EffectOverride { X = value.X, Y = value.Y, Time = value.Time, HitInterval = value.HitInterval });
            }
        HitInterval = hitInterval; MediumJitter = mediumJitter; FullJitter = fullJitter; MediumRandomScale = mediumRandomScale;
    }

    public static BattlePresentationConfiguration Load()
    {
        string data = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Data");
        var values = BattlefieldDamagePopupStyle.ReadParameters(Path.Combine(data, "UI", BattlefieldDamagePopupStyle.FileName));
        return Parse(values, ReadEffects(Path.Combine(data, EffectFileName)));
    }

    public static BattlePresentationConfiguration Parse(IReadOnlyDictionary<string, string> values,
        Dictionary<string, EffectOverride> effects = null)
    {
        if (values == null) throw new InvalidDataException("缺少UI表现参数。");
        double Optional(string key, double fallback)
            => values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value) ? NonNegative(value, key) : fallback;
        bool Has(string key) => values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value);
        double scale = Optional("MediumRandomScale", .5);
        if (scale > 1) throw new InvalidDataException("MediumRandomScale必须在0到1之间。");
        // 新字段优先；保留旧字段兼容。未配置时间参数的老表仍即时显示。
        double interval = Has("TimeBetweenHits") ? Optional("TimeBetweenHits", 0) : Optional("HitIntervalSeconds", 0);
        double full = Has("HitTimeRandomVariant") ? Optional("HitTimeRandomVariant", 0) : Optional("FullTimeJitterSeconds", 0);
        double medium = Has("HitTimeRandomVariant") ? full * scale : Optional("MediumTimeJitterSeconds", full * scale);
        return new BattlePresentationConfiguration(effects, interval, medium, full, (float)scale);
    }

    public BattlePresentationTiming For(BattlePresentationResult result)
    {
        effects.TryGetValue(result.EffectID ?? string.Empty, out var item);
        int time = item?.Time ?? 0;
        return new BattlePresentationTiming(item?.HitInterval ?? HitInterval,
            time == 1 ? MediumJitter : time == 2 ? FullJitter : 0, item?.X ?? 2, item?.Y ?? 2, MediumRandomScale);
    }

    public static Dictionary<string, EffectOverride> ReadEffects(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["SkillUIEffects"];
        if (sheet?.Dimension == null) throw new InvalidDataException("缺少SkillUIEffects子表。");
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 1; c <= sheet.Dimension.End.Column; c++)
        {
            string key = sheet.Cells[1, c].Text.Trim();
            if (key != "EffectID" && key != "XRandomMode" && key != "YRandomMode" && key != "TimeRandomMode" && key != "HitIntervalSeconds" && key != "TimeBetweenHits") continue;
            if (columns.ContainsKey(key)) throw new InvalidDataException("重复的SkillUIEffects表头：" + key);
            columns.Add(key, c);
        }
        foreach (string key in new[] { "EffectID", "XRandomMode", "YRandomMode", "TimeRandomMode" })
            if (!columns.ContainsKey(key)) throw new InvalidDataException("缺少SkillUIEffects表头：" + key);
        var result = new Dictionary<string, EffectOverride>(StringComparer.Ordinal);
        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            string Cell(string key) => columns.TryGetValue(key, out int column)
                ? Convert.ToString(sheet.Cells[row, column].Value, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
            string id = Cell("EffectID");
            if (id.Length == 0) continue;
            if (result.ContainsKey(id)) throw new InvalidDataException("重复的SkillUIEffects效果ID：" + id);
            int? Mode(string key)
            {
                string value = Cell(key);
                if (value.Length == 0) return null;
                if (!int.TryParse(value, out int mode) || mode < 0 || mode > 2)
                    throw new InvalidDataException($"SkillUIEffects第{row}行{key}必须为0/1/2或留空。");
                return mode;
            }
            string intervalKey = Cell("TimeBetweenHits").Length > 0 ? "TimeBetweenHits" : "HitIntervalSeconds";
            string interval = Cell(intervalKey);
            result.Add(id, new EffectOverride { X = Mode("XRandomMode"), Y = Mode("YRandomMode"), Time = Mode("TimeRandomMode"),
                HitInterval = interval.Length == 0 ? (double?)null : NonNegative(interval, intervalKey) });
        }
        return result;
    }

    private static double NonNegative(string text, string key)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new InvalidDataException("UI参数必须为有限非负数：" + key);
        return value;
    }
}
