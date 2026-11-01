using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Per-hit shake selection and profiles. Empty override inherits; None explicitly disables.</summary>
public sealed class HitShakeStyle
{
    public const string FileName = "HitShake.xlsx";
    public sealed class Profile
    {
        public string ID, Space;
        public float Duration, X, Y, Rotation, Frequency, Decay, MaxTranslation, MaxRotation;
    }
    public sealed class Selection { public Profile Card, Screen; }
    private sealed class Rule { public string Card, Screen; }
    private readonly Dictionary<string, Profile> profiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
    private readonly Dictionary<(string, int), Rule> rules = new Dictionary<(string, int), Rule>();
    public Profile Get(string id) => id == "None" ? null : profiles[id];
    public static HitShakeStyle Load() => ReadFile(BattleTimelineSettings.RuntimePath(FileName));

    public Selection Resolve(string damageType, string effectID, int hitIndex)
    {
        string card = damageType == "Normal" ? "CardNormal" : damageType == "Heavy" ? "CardHeavy" : "None", screen = "None";
        void Apply(Rule rule) { if (rule.Card.Length > 0) card = rule.Card; if (rule.Screen.Length > 0) screen = rule.Screen; }
        if (rules.TryGetValue((effectID ?? "", -1), out var all)) Apply(all);
        if (rules.TryGetValue((effectID ?? "", hitIndex), out var exact)) Apply(exact);
        return new Selection { Card = Get(card), Screen = Get(screen) };
    }

    public static HitShakeStyle ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["Profiles"];
        if (sheet?.Dimension == null) throw new InvalidDataException("HitShake 缺少 Profiles 子表。");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("Key", "CardNormal", "CardHeavy", "ScreenDefault");
        var style = new HitShakeStyle();
        foreach (var column in h.Columns)
        {
            string id = column.Key;
            if (id == "Key" || id == "Unit" || id == "Description") continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                string key = h.Cell(row, "Key").Text.Trim(); if (key.Length == 0) continue;
                if (values.ContainsKey(key)) throw new InvalidDataException("HitShake 重复参数：" + key);
                values.Add(key, Convert.ToString(sheet.Cells[row, column.Value].Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
            }
            float Number(string key)
            {
                if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < 0)
                    throw new InvalidDataException("HitShake 数字无效：" + id + "/" + key);
                return n;
            }
            var allowed = new HashSet<string> { "Space", "DurationSeconds", "AmplitudeXRatio", "AmplitudeYRatio", "RotationDegrees", "FrequencyHz", "DecayPower", "MaxTranslationRatio", "MaxRotationDegrees" };
            foreach (string key in values.Keys) if (!allowed.Contains(key)) throw new InvalidDataException("HitShake 未知参数：" + key);
            if (!values.TryGetValue("Space", out string space)) throw new InvalidDataException("HitShake 缺少 Space。");
            var p = new Profile { ID = id, Space = space, Duration = Number("DurationSeconds"),
                X = Number("AmplitudeXRatio"), Y = Number("AmplitudeYRatio"), Rotation = Number("RotationDegrees"),
                Frequency = Number("FrequencyHz"), Decay = Number("DecayPower"), MaxTranslation = Number("MaxTranslationRatio"), MaxRotation = Number("MaxRotationDegrees") };
            if (id == "None" || style.profiles.ContainsKey(id) || (p.Space != "Card" && p.Space != "Screen") || p.Duration <= 0 || p.Decay <= 0 || p.Frequency <= 0)
                throw new InvalidDataException("HitShake 档位无效或重复：" + id);
            style.profiles.Add(id, p);
        }
        foreach (string id in new[] { "CardNormal", "CardHeavy", "ScreenDefault" })
            if (!style.profiles.TryGetValue(id, out var p) || p.Space != (id == "ScreenDefault" ? "Screen" : "Card"))
                throw new InvalidDataException("HitShake 缺少正确空间的默认档位：" + id);
        var effects = package.Workbook.Worksheets["Effects"];
        if (effects?.Dimension == null) throw new InvalidDataException("HitShake 缺少 Effects 子表。");
        h = ExcelTableHeaders.For(effects); h.Require("EffectID", "HitIndex", "CardProfile", "ScreenProfile");
        for (int row = h.DataStartRow; row <= effects.Dimension.End.Row; row++)
        {
            string Text(string key) => h.Cell(row, key).Text.Trim();
            string id = Text("EffectID"); if (id.Length == 0) continue;
            if (!int.TryParse(Text("HitIndex"), out int hit) || hit < -1) throw new InvalidDataException("HitShake HitIndex 必须为 -1 或非负整数。");
            var rule = new Rule { Card = Text("CardProfile"), Screen = Text("ScreenProfile") };
            void Check(string profile, string space)
            {
                if (profile.Length == 0 || profile == "None") return;
                if (!style.profiles.TryGetValue(profile, out var p) || p.Space != space) throw new InvalidDataException("HitShake 引用档位不存在或空间不符：" + profile);
            }
            Check(rule.Card, "Card"); Check(rule.Screen, "Screen");
            if (style.rules.ContainsKey((id, hit))) throw new InvalidDataException("HitShake 重复效果/hit：" + id);
            style.rules.Add((id, hit), rule);
        }
        return style;
    }
}

public struct HitShakePose { public Vector2 Offset; public float Rotation; }

/// <summary>Pure damped visual oscillation. No gameplay RNG; no accumulated transform writes.</summary>
public static class HitShakeMath
{
    public static HitShakePose Sample(HitShakeStyle.Profile p, Vector2 size, uint seed, float age)
    {
        if (age < 0 || age >= p.Duration) return default;
        float envelope = Mathf.Pow(1 - age / p.Duration, p.Decay);
        float wave = age * p.Frequency * 2 * Mathf.PI;
        float Osc(uint channel) => Mathf.Cos(wave * (channel == 2 ? 1.19f : 1) + HitEffectMath.Unit(seed, channel + 900) * 2 * Mathf.PI);
        return Clamp(new HitShakePose { Offset = new Vector2(Osc(1) * p.X * size.x, Osc(2) * p.Y * size.y) * envelope,
            Rotation = Osc(3) * p.Rotation * envelope }, size, p.MaxTranslation, p.MaxRotation);
    }
    public static HitShakePose Clamp(HitShakePose pose, Vector2 size, float translationLimit, float rotationLimit)
    {
        pose.Offset = Vector2.ClampMagnitude(pose.Offset, Mathf.Min(size.x, size.y) * translationLimit);
        pose.Rotation = Mathf.Clamp(pose.Rotation, -rotationLimit, rotationLimit); return pose;
    }
}
