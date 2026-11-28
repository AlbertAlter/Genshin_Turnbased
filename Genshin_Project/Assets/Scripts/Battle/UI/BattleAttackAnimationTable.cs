using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>One EffectID main table. Blank cells inherit its optional preset; explicit numeric values override.</summary>
public sealed class BattleAttackAnimationTable
{
    public const string FileName = "AttackAnimations.xlsx";
    private readonly Dictionary<string, BattleAttackAnimationSpec> effects = new Dictionary<string, BattleAttackAnimationSpec>(StringComparer.Ordinal);
    private readonly Dictionary<string, BattleAttackAnimationSpec> presetSpecs = new Dictionary<string, BattleAttackAnimationSpec>(StringComparer.Ordinal);
    public BattleAttackAnimationSpec Find(string effect) => effect != null && effects.TryGetValue(effect, out var value) ? value : null;
    public BattleAttackAnimationSpec GetPreset(string id) => presetSpecs.TryGetValue(id, out var value) ? value : throw new InvalidDataException("缺少攻击动画预设：" + id);
    public int Count => effects.Count;
    public IEnumerable<BattleAttackAnimationSpec> Effects => effects.Values;
    public static BattleAttackAnimationTable Load() => CampaignUiTables.Read(() => ReadFile(BattleTimelineSettings.RuntimePath(FileName)));
    public static BattleAttackAnimationTable ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var presets = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var result = new BattleAttackAnimationTable();
        string[] fields = { "AttackType", "SelfAnimation", "HitAnimation", "DurationSeconds", "ContactRatio", "TiltDegrees", "Strength", "ScreenShakeAxes", "ScreenShakeAmplitude", "HitEffectPreset", "AnimationHook", "HitTimesSeconds", "RepeatIntervalSeconds", "ProjectileSeconds" };
        bool Required(string key) => key != "ScreenShakeAxes" && key != "ScreenShakeAmplitude" && key != "HitEffectPreset" && key != "AnimationHook"
            && key != "HitTimesSeconds" && key != "RepeatIntervalSeconds" && key != "ProjectileSeconds";
        string Text(ExcelTableHeaders h, int row, string key) => h.Optional(key) < 1 ? "" : Convert.ToString(h.Cell(row, key).Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        var sheet = book.Workbook.Worksheets["Presets"];
        if (sheet?.Dimension == null) throw new InvalidDataException("AttackAnimations 缺少 Presets 子表。");
        var h = ExcelTableHeaders.For(sheet); h.Require("PresetID"); foreach (var key in fields) if (Required(key)) h.Require(key);
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = Text(h, row, "PresetID"); if (id.Length == 0) continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in fields) values.Add(key, Text(h, row, key));
            var spec = Parse(id, values);
            if (presets.ContainsKey(id)) throw new InvalidDataException("重复攻击动画预设：" + id);
            presets.Add(id, values);
            result.presetSpecs.Add(id, spec);
        }
        sheet = book.Workbook.Worksheets["Effects"];
        if (sheet?.Dimension == null) throw new InvalidDataException("AttackAnimations 缺少 Effects 主表。");
        h = ExcelTableHeaders.For(sheet); h.Require("EffectID", "Preset"); foreach (var key in fields) if (Required(key)) h.Require(key);
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string id = Text(h, row, "EffectID"); if (id.Length == 0) continue;
            string preset = Text(h, row, "Preset");
            Dictionary<string, string> inherited = null;
            if (preset.Length > 0 && !presets.TryGetValue(preset, out inherited)) throw new InvalidDataException("攻击动画预设不存在：" + id + "/" + preset);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in fields) { string own = Text(h, row, key); values.Add(key, own.Length > 0 ? own : inherited != null ? inherited[key] : ""); }
            if (result.effects.ContainsKey(id)) throw new InvalidDataException("重复攻击动画 EffectID：" + id);
            result.effects.Add(id, Parse(id, values));
        }
        return result;
    }
    private static BattleAttackAnimationSpec Parse(string id, Dictionary<string, string> values)
    {
        float Number(string key)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || float.IsNaN(n) || float.IsInfinity(n))
                throw new InvalidDataException("攻击动画缺少或填写了非法数值：" + id + "/" + key);
            return n;
        }
        int Choice(string key)
        {
            if ((key == "SelfAnimation" || key == "HitAnimation") && values[key].Length == 0) return 1;
            float n = Number(key); if (n != (int)n) throw new InvalidDataException("动画编号必须是整数：" + id + "/" + key); return (int)n;
        }
        double Seconds(string text, string key)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsNaN(n) || double.IsInfinity(n) || n < 0)
                throw new InvalidDataException("攻击时间轴需要有限非负秒数：" + id + "/" + key);
            return n;
        }
        BattleHitTemplate timeline = null;
        if (values["HitTimesSeconds"].Length > 0)
        {
            var markers = new List<double>();
            foreach (var raw in values["HitTimesSeconds"].Split(';')) markers.Add(Seconds(raw.Trim(), "HitTimesSeconds"));
            timeline = new BattleHitTemplate("Attack/" + id, markers,
                values["RepeatIntervalSeconds"].Length == 0 ? 0 : Seconds(values["RepeatIntervalSeconds"], "RepeatIntervalSeconds"),
                values["ProjectileSeconds"].Length == 0 ? 0 : Seconds(values["ProjectileSeconds"], "ProjectileSeconds"));
        }
        else if (values["RepeatIntervalSeconds"].Length > 0 || values["ProjectileSeconds"].Length > 0)
            throw new InvalidDataException("攻击时间轴填写间隔或飞行时长时必须同时提供HitTimesSeconds：" + id);
        return new BattleAttackAnimationSpec(id, Choice("AttackType"), Choice("SelfAnimation"), Choice("HitAnimation"),
            Number("DurationSeconds"), Number("ContactRatio"), Number("TiltDegrees"), Number("Strength"),
            values["ScreenShakeAxes"], values["ScreenShakeAmplitude"].Length == 0 ? 0 : Number("ScreenShakeAmplitude"), values["HitEffectPreset"], values["AnimationHook"], timeline: timeline);
    }
}
